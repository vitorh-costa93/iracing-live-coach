using System.IO.Pipes;
using System.Text;

namespace Ams2.Shared.Ipc;

/// <summary>
/// Servidor do named pipe (fica no host). Aceita varios clientes; cada um e atendido em sua propria tarefa: le uma mensagem por linha,
/// chama o handler (que pode bloquear esperando a thread de render) e escreve a resposta. Cliente que cai nunca derruba o laco.
/// </summary>
public sealed class IpcServer : IDisposable
{
    const int MaxInstances = 4;

    readonly string _pipeName;
    readonly Func<IpcMessage, IpcMessage> _handler;
    readonly CancellationTokenSource _cts = new();
    readonly List<Client> _clients = [];
    readonly Task _loop;

    sealed class Client(NamedPipeServerStream pipe)
    {
        public NamedPipeServerStream Pipe { get; } = pipe;
        public SemaphoreSlim WriteLock { get; } = new(1, 1);

        public async Task SendAsync(IpcMessage m, CancellationToken ct)
        {
            var bytes = Encoding.UTF8.GetBytes(IpcProtocol.Serialize(m) + "\n");
            await WriteLock.WaitAsync(ct).ConfigureAwait(false);
            try { await Pipe.WriteAsync(bytes, ct).ConfigureAwait(false); await Pipe.FlushAsync(ct).ConfigureAwait(false); }
            finally { WriteLock.Release(); }
        }
    }

    public int ClientCount { get { lock (_clients) return _clients.Count; } }

    public IpcServer(string pipeName, Func<IpcMessage, IpcMessage> handler)
    {
        _pipeName = pipeName;
        _handler = handler;
        _loop = Task.Run(AcceptLoop);
    }

    async Task AcceptLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, MaxInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await pipe.WaitForConnectionAsync(_cts.Token).ConfigureAwait(false);
                var client = new Client(pipe);
                lock (_clients) _clients.Add(client);
                _ = Task.Run(() => Serve(client));
            }
            catch (OperationCanceledException) { pipe?.Dispose(); }
            catch (Exception ex)
            {
                pipe?.Dispose();
                Console.Error.WriteLine($"[Ipc] accept: {ex.Message}");
                try { await Task.Delay(200, _cts.Token).ConfigureAwait(false); } catch (OperationCanceledException) { }
            }
        }
    }

    async Task Serve(Client c)
    {
        try
        {
            using var reader = new StreamReader(c.Pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
            string? line;
            while (!_cts.IsCancellationRequested && (line = await reader.ReadLineAsync(_cts.Token).ConfigureAwait(false)) is not null)
            {
                var req = IpcProtocol.TryParse(line);
                if (req is not { Kind: "req" }) continue;
                IpcMessage res;
                try { res = _handler(req); }
                catch (Exception ex) { res = new IpcMessage { Ok = false, Error = ex.Message }; }
                await c.SendAsync(res with { Kind = "res", Id = req.Id, Cmd = req.Cmd }, _cts.Token).ConfigureAwait(false);
            }
        }
        catch (Exception) when (!_cts.IsCancellationRequested) { /* cliente caiu */ }
        catch (OperationCanceledException) { }
        finally
        {
            lock (_clients) _clients.Remove(c);
            c.Pipe.Dispose();
        }
    }

    /// <summary>Envia um evento a todos os clientes conectados (melhor esforco).</summary>
    public void Broadcast(IpcMessage evt)
    {
        Client[] snapshot;
        lock (_clients) snapshot = _clients.ToArray();
        foreach (var c in snapshot)
            _ = c.SendAsync(evt with { Kind = "evt" }, _cts.Token).ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnFaulted);
    }

    public void Dispose()
    {
        _cts.Cancel();
        Client[] snapshot;
        lock (_clients) snapshot = _clients.ToArray();
        foreach (var c in snapshot) c.Pipe.Dispose();
        try { _loop.Wait(500); } catch { }
        _cts.Dispose();
    }
}
