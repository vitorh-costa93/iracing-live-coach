using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;

namespace Ams2.Shared.Ipc;

/// <summary>
/// Cliente do named pipe (Control Center). Mantem uma conexao e reconecta sozinho: o host pode abrir depois ou reiniciar.
/// Requisicoes pendentes falham quando a conexao cai. Eventos e mudancas de conexao chegam em threads de fundo.
/// </summary>
public sealed class IpcClient : IDisposable
{
    readonly string _pipeName;
    readonly CancellationTokenSource _cts = new();
    readonly ConcurrentDictionary<long, TaskCompletionSource<IpcMessage>> _pending = new();
    readonly SemaphoreSlim _writeLock = new(1, 1);
    NamedPipeClientStream? _pipe;
    long _nextId;
    volatile bool _connected;
    Task? _loop;

    public bool Connected => _connected;
    public event Action<bool>? ConnectionChanged;
    public event Action<IpcMessage>? EventReceived;

    public IpcClient(string pipeName = IpcProtocol.DefaultPipeName) => _pipeName = pipeName;

    public void Start() => _loop ??= Task.Run(Run);

    async Task Run()
    {
        while (!_cts.IsCancellationRequested)
        {
            var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                using (var to = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token))
                {
                    to.CancelAfter(400);
                    await pipe.ConnectAsync(to.Token).ConfigureAwait(false);
                }
                _pipe = pipe;
                SetConnected(true);
                using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
                string? line;
                while (!_cts.IsCancellationRequested && (line = await reader.ReadLineAsync(_cts.Token).ConfigureAwait(false)) is not null)
                {
                    var m = IpcProtocol.TryParse(line);
                    if (m is null) continue;
                    if (m.Kind == "res") { if (_pending.TryRemove(m.Id, out var tcs)) tcs.TrySetResult(m); }
                    else if (m.Kind == "evt") { try { EventReceived?.Invoke(m); } catch { } }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception) { }
            finally
            {
                _pipe = null;
                pipe.Dispose();
                SetConnected(false);
                foreach (var kv in _pending.ToArray())
                    if (_pending.TryRemove(kv.Key, out var tcs)) tcs.TrySetException(new IOException("Conexao com o host perdida."));
            }
            try { await Task.Delay(500, _cts.Token).ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
    }

    void SetConnected(bool value)
    {
        if (_connected == value) return;
        _connected = value;
        try { ConnectionChanged?.Invoke(value); } catch { }
    }

    /// <summary>Envia o pedido e espera a resposta. Lanca IOException se nao ha conexao e TimeoutException se o host nao responde.</summary>
    public async Task<IpcMessage> RequestAsync(IpcMessage request, int timeoutMs = 3000)
    {
        var pipe = _pipe;
        if (pipe is null || !_connected) throw new IOException("Host nao conectado.");
        long id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<IpcMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;
        try
        {
            var bytes = Encoding.UTF8.GetBytes(IpcProtocol.Serialize(request with { Kind = "req", Id = id }) + "\n");
            await _writeLock.WaitAsync(_cts.Token).ConfigureAwait(false);
            try { await pipe.WriteAsync(bytes, _cts.Token).ConfigureAwait(false); await pipe.FlushAsync(_cts.Token).ConfigureAwait(false); }
            finally { _writeLock.Release(); }
            using var to = new CancellationTokenSource(timeoutMs);
            using var reg = to.Token.Register(() => tcs.TrySetException(new TimeoutException("O host nao respondeu.")));
            return await tcs.Task.ConfigureAwait(false);
        }
        finally { _pending.TryRemove(id, out _); }
    }

    /// <summary>Atalho: manda e devolve o estado da resposta; erro do host vira InvalidOperationException.</summary>
    public async Task<HostState?> SendAsync(string cmd, Func<IpcMessage, IpcMessage>? fill = null)
    {
        var req = new IpcMessage { Cmd = cmd };
        if (fill is not null) req = fill(req);
        var res = await RequestAsync(req).ConfigureAwait(false);
        if (!res.Ok) throw new InvalidOperationException(res.Error ?? "Erro do host.");
        return res.State;
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _pipe?.Dispose(); } catch { }
        try { _loop?.Wait(500); } catch { }
        _cts.Dispose();
    }
}
