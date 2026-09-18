using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace IracingLiveCoach.ControlCenter;

/// <summary>Same short-lived-connection pattern as the other IPC clients, on the dedicated rules pipe.</summary>
public sealed class RulesIpcClient
{
    private const string PipeName = "iracinglivecoach-v3-rules";
    private const int SchemaVersion = 1;
    private const int ConnectTimeoutMs = 200;

    public async Task<bool> SendAsync(int topNPerClass, int ownClassRows, int otherClassRows, bool keepPlayerWindow)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            using var cts = new CancellationTokenSource(ConnectTimeoutMs);
            await pipe.ConnectAsync(cts.Token);

            var message = new RulesMessageWire(SchemaVersion, topNPerClass, ownClassRows, otherClassRows, keepPlayerWindow);
            string json = JsonSerializer.Serialize(message) + "\n";
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            await pipe.WriteAsync(bytes);
            await pipe.FlushAsync();
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (IOException) { return false; }
        catch (TimeoutException) { return false; }
    }
}

file sealed record RulesMessageWire(int SchemaVersion, int TopNPerClass, int OwnClassRows, int OtherClassRows, bool KeepPlayerWindow);
