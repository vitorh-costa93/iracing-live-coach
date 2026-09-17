using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace IracingLiveCoach.ControlCenter;

/// <summary>
/// The Control Center's half of the typed, versioned IPC channel (spec §3). Opens a fresh
/// connection per message rather than holding one open: the OverlayHost may not be running yet, or
/// may restart independently, and a short-lived connection attempt naturally tolerates both without
/// needing explicit reconnect/retry state on this side -- the "next" edit just tries again.
///
/// <see cref="PlacementMessage"/> here is an independently-defined twin of the identically-named
/// record in <c>IracingLiveCoach.OverlayHost.Ipc</c> (see that file's own doc comment for why the
/// two processes don't share a compiled type for their wire contract).
/// </summary>
public sealed class PlacementIpcClient
{
    private const string PipeName = "iracinglivecoach-v3-placement";
    private const int SchemaVersion = 1;
    private const int ConnectTimeoutMs = 200;

    public async Task<bool> SendAsync(string widget, WidgetUiState state)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            using var cts = new CancellationTokenSource(ConnectTimeoutMs);
            await pipe.ConnectAsync(cts.Token);

            var message = new PlacementMessage(SchemaVersion, widget, state.X, state.Y, state.Width, state.Height,
                state.Scale, state.Locked, state.Visible, state.Opacity);
            string json = JsonSerializer.Serialize(message) + "\n";
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            await pipe.WriteAsync(bytes);
            await pipe.FlushAsync();
            return true;
        }
        catch (OperationCanceledException) { return false; } // overlay not running / pipe not open yet
        catch (IOException) { return false; } // overlay just exited mid-write
        catch (TimeoutException) { return false; }
    }
}

file sealed record PlacementMessage(
    int SchemaVersion,
    string Widget,
    float X,
    float Y,
    float WidthDip,
    float HeightDip,
    float Scale,
    bool Locked,
    bool Visible,
    float Opacity);
