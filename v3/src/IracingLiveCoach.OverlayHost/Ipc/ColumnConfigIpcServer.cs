using System.IO.Pipes;
using System.Text.Json;

namespace IracingLiveCoach.OverlayHost.Ipc;

/// <summary>Same fresh-pipe-per-connection pattern as <see cref="PlacementIpcServer"/>/<see cref="EditModeIpcServer"/>.</summary>
public sealed class ColumnConfigIpcServer : IDisposable
{
    public const string PipeName = "iracinglivecoach-v3-columns";

    private readonly CancellationTokenSource _cts = new();
    private readonly Thread _thread;

    public event Action<ColumnConfigMessage>? MessageReceived;

    public ColumnConfigIpcServer()
    {
        _thread = new Thread(RunLoop) { IsBackground = true, Name = "ColumnConfigIpcServer" };
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
            var message = JsonSerializer.Deserialize<ColumnConfigMessage>(line);
            if (message is { SchemaVersion: ColumnConfigMessage.CurrentSchemaVersion })
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
