using System.IO.Pipes;
using System.Text.Json;

namespace IracingLiveCoach.OverlayHost.Ipc;

/// <summary>Same fresh-pipe-per-connection pattern as the other four IPC servers.</summary>
public sealed class FuelConfigIpcServer : IDisposable
{
    public const string PipeName = "iracinglivecoach-v3-fuelconfig";

    private readonly CancellationTokenSource _cts = new();
    private readonly Thread _thread;

    public event Action<FuelConfigMessage>? MessageReceived;

    public FuelConfigIpcServer()
    {
        _thread = new Thread(RunLoop) { IsBackground = true, Name = "FuelConfigIpcServer" };
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
            var message = JsonSerializer.Deserialize<FuelConfigMessage>(line);
            if (message is { SchemaVersion: FuelConfigMessage.CurrentSchemaVersion })
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
