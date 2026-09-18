using IracingLiveCoach.Core.Telemetry;
using IracingLiveCoach.OverlayHost.Widgets;

namespace IracingLiveCoach.ControlCenter;

/// <summary>Simulated rows for <see cref="OverlayPreviewHost"/> -- delegates to the overlay's own
/// <see cref="SimulationData"/> so the preview and the overlay's simulation mode never drift apart.
/// Never used as a stand-in for real telemetry.</summary>
internal static class PreviewData
{
    public static List<StandingsRow> StandingsRows() => SimulationData.StandingsRows();
    public static List<RelativeRow> RelativeRows() => SimulationData.RelativeRows();
}
