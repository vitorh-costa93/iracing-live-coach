using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace IracingLiveCoach.ControlCenter;

public sealed record ColumnConfigEntry(
    string Key, bool Visible, int Order, float WidthPx, float MinWidthPx,
    string WidthMode, string Alignment, int? DecimalPlaces,
    float PaddingLeftPx, float PaddingRightPx,
    string? FontFamily = null, int? FontWeight = null, int? LapWindow = null);

/// <summary>Same short-lived-connection pattern as <see cref="PlacementIpcClient"/>/<see cref="EditModeIpcClient"/>,
/// on the dedicated column-config pipe.</summary>
public sealed class ColumnConfigIpcClient
{
    private const string PipeName = "iracinglivecoach-v3-columns";
    private const int SchemaVersion = 2;
    private const int ConnectTimeoutMs = 200;

    public async Task<bool> SendAsync(string widget, List<ColumnConfigEntry> columns)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            using var cts = new CancellationTokenSource(ConnectTimeoutMs);
            await pipe.ConnectAsync(cts.Token);

            var message = new ColumnConfigMessageWire(SchemaVersion, widget, columns);
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

file sealed record ColumnConfigMessageWire(int SchemaVersion, string Widget, List<ColumnConfigEntry> Columns);
