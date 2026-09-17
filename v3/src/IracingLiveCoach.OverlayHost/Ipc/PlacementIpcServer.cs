using System.IO.Pipes;
using System.Text.Json;

namespace IracingLiveCoach.OverlayHost.Ipc;

/// <summary>
/// Named-pipe server accepting one <see cref="PlacementMessage"/> per line from the Control Center
/// (spec §3: "aplicação de configurações sem reiniciar a corrida"). Runs a fresh
/// <see cref="NamedPipeServerStream"/> per connection attempt in a background thread: a client that
/// disconnects (Control Center closed, or briefly unreachable) never kills this loop, and the next
/// connection attempt is served immediately -- this IS the "reconexão" spec §3 asks for, achieved
/// by never holding a single long-lived session open rather than by explicit reconnect logic.
/// </summary>
public sealed class PlacementIpcServer : IDisposable
{
    public const string PipeName = "iracinglivecoach-v3-placement";

    private readonly CancellationTokenSource _cts = new();
    private readonly Thread _thread;

    public event Action<PlacementMessage>? MessageReceived;

    public PlacementIpcServer()
    {
        _thread = new Thread(RunLoop) { IsBackground = true, Name = "PlacementIpcServer" };
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
                {
                    TryHandleLine(line);
                }
            }
            catch (OperationCanceledException) { /* shutting down */ }
            catch { /* a malformed client or a broken pipe must never take the overlay process down */ }
        }
    }

    private void TryHandleLine(string line)
    {
        try
        {
            var message = JsonSerializer.Deserialize<PlacementMessage>(line);
            // Unknown/newer schema versions are ignored, not guessed at -- spec §3's own
            // versioning requirement exists precisely so an old host and a new panel (or vice
            // versa) fail safe instead of misapplying a field they don't understand.
            if (message is { SchemaVersion: PlacementMessage.CurrentSchemaVersion })
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
