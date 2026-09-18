using IracingLiveCoach.Core.Telemetry;

namespace IracingLiveCoach.ControlCenter;

/// <summary>
/// Fixed simulated rows for <see cref="OverlayPreviewHost"/> -- the exact same shape/values as
/// OverlayHost's own <c>Program.BuildSimulatedStandingsRows</c>/<c>BuildSimulatedRelativeRows</c>
/// (spec §12's mandated simulation preset), duplicated here rather than shared because those are
/// private to the console app's <c>Program</c> class in a different project. Never used as a
/// stand-in for real telemetry -- this process never opens its own SDK connection at all.
/// </summary>
internal static class PreviewData
{
    public static List<StandingsRow> StandingsRows() =>
    [
        new StandingsRow(1, "Max Verstappen", 12, 88.412, null, false, "🇳🇱", "A", null, 4820, 1,
            "RedBull", null, 14.2, -0.412, "GT3", "#FFD400", 1, null, null, null, null, false, "—"),
        new StandingsRow(2, "Lewis Hamilton", 12, 88.901, null, false, "🇬🇧", "A", null, 4650, 1,
            "Mercedes", 1.8, -3.6, 0.077, "GT3", "#FFD400", 2, 1.8, null, null, null, false, "—"),
        new StandingsRow(3, "Vitor Costa", 12, 89.150, null, true, "🇧🇷", "B", null, 3200, 1,
            "Ferrari", 3.1, 0.0, 0.0, "GT3", "#FFD400", 3, 1.3, null, null, null, false, "—"),
        new StandingsRow(4, "Charles Leclerc", 11, 89.740, null, false, "🇲🇨", "A", null, 4400, 1,
            "Ferrari", 12.6, 5.9, 0.590, "GT3", "#FFD400", 4, 9.5, null, null, null, false, "—"),
    ];

    public static List<RelativeRow> RelativeRows() =>
    [
        new RelativeRow(-3, "Oliver Wilson", -8.912, null, null, null, null, false, "🇬🇧", "A", null, 4100, 1, "Aston Martin", false, 1, "GT3", "#FFD400"),
        new RelativeRow(-2, "Max Hoffmann", -5.201, null, null, null, null, false, "🇩🇪", "A", null, 3980, 1, "BMW", false, 2, "GT3", "#FFD400"),
        new RelativeRow(-1, "Vitor Costa", -1.892, null, null, null, null, false, "🇧🇷", "B", null, 3200, 1, "Ferrari", false, 3, "GT3", "#FFD400"),
        new RelativeRow(0, "Vitor Costa", 0, null, null, null, null, false, "🇧🇷", "B", null, 3200, 1, "Ferrari", true, 4, "GT3", "#FFD400"),
        new RelativeRow(1, "Daniel Walker", 1.304, null, null, null, null, false, "🇺🇸", "A", null, 3012, 1, "Mercedes", false, 5, "GT3", "#FFD400"),
        new RelativeRow(2, "Simon Wagner", 2.910, null, null, null, null, false, "🇩🇪", "A", null, 3455, 1, "Ford", false, 6, "GT3", "#FFD400"),
        new RelativeRow(3, "James Carter", 5.330, null, null, null, null, false, "🇺🇸", "B", null, 3298, 1, "McLaren", false, 7, "GT3", "#FFD400"),
    ];
}
