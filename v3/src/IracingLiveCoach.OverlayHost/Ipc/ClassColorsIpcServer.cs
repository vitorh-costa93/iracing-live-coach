using System.IO.Pipes;
using System.Text.Json;

namespace IracingLiveCoach.OverlayHost.Ipc;

/// <summary>Same fresh-pipe-per-connection pattern as the other five IPC servers.</summary>
public sealed class ClassColorsIpcServer : IDisposable
{
    public const string PipeName = "iracinglivecoach-v3-classcolors";

    private readonly CancellationTokenSource _cts = new();
    private readonly Thread _thread;

    public event Action<ClassColorsMessage>? MessageReceived;

    public ClassColorsIpcServer()
    {
        _thread = new Thread(RunLoop) { IsBackground = true, Name = "ClassColorsIpcServer" };
        _thread.Start();
    }

    private void RunLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                pipe.WaitForConnectionAsync(_cts.Token).GetAwaiter().GetResult();

                using var reader = new StreamReader(pipe);
                string? line;
                while ((line = reader.ReadLine()) is not null)
                    TryHandleLine(line);
            }
            catch (OperationCanceledException) { /* shutting down */ }
            catch { /* a malformed client or a broken pipe must never take the overlay process down */ }
        }
    }

    private void TryHandleLine(string line)
    {
        try
        {
            var message = JsonSerializer.Deserialize<ClassColorsMessage>(line);
            if (message is { SchemaVersion: ClassColorsMessage.CurrentSchemaVersion })
                MessageReceived?.Invoke(message);
        }
        catch (JsonException) { /* malformed line from an incompatible/broken client -- skip it */ }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
