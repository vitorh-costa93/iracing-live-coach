using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace IracingLiveCoach.ControlCenter;

/// <summary>Same short-lived-connection pattern as <see cref="PlacementIpcClient"/>, on the
/// dedicated edit-mode pipe -- lets the Control Center's Lock/Unlock control flip the same global
/// edit mode the "E" key toggles on the overlay itself.</summary>
public sealed class EditModeIpcClient
{
    private const string PipeName = "iracinglivecoach-v3-editmode";
    private const int SchemaVersion = 1;
    private const int ConnectTimeoutMs = 200;

    public async Task<bool> SendAsync(bool enabled)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            using var cts = new CancellationTokenSource(ConnectTimeoutMs);
            await pipe.ConnectAsync(cts.Token);

            var message = new EditModeMessage(SchemaVersion, enabled);
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

file sealed record EditModeMessage(int SchemaVersion, bool Enabled);
