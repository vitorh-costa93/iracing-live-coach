using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace IracingLiveCoach.ControlCenter;

/// <summary>Same short-lived-connection pattern as the other IPC clients, on the dedicated number-format pipe.</summary>
public sealed class NumberFormatIpcClient
{
    private const string PipeName = "iracinglivecoach-v3-numberformat";
    private const int SchemaVersion = 1;
    private const int ConnectTimeoutMs = 200;

    public async Task<bool> SendAsync(string iRatingFormat, string safetyRatingFormat, string nameFormat, bool showIRatingDelta = true)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            using var cts = new CancellationTokenSource(ConnectTimeoutMs);
            await pipe.ConnectAsync(cts.Token);

            var message = new NumberFormatMessageWire(SchemaVersion, iRatingFormat, safetyRatingFormat, nameFormat, showIRatingDelta);
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

file sealed record NumberFormatMessageWire(int SchemaVersion, string IRatingFormat, string SafetyRatingFormat, string NameFormat, bool ShowIRatingDelta);
