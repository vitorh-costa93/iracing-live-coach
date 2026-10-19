using IracingLiveCoach.Core.Coach;

namespace IracingLiveCoach.OverlayHost.Coach;

/// <summary>Immutable copy of the coach state the three coach widgets draw (see <see cref="CoachHost.Snapshot"/>).</summary>
public sealed record CoachSnapshot(
    CoachSettings Settings,
    IReadOnlyList<TracePoint> Trace,
    bool HasReference,
    LapReport? Report,
    TechniqueBreakdown? Breakdown,
    int SessionScore,
    CoachMessage? LastMessage,
    string? WeeklyFocus)
{
    public static readonly CoachSnapshot Empty = new(new CoachSettings(), Array.Empty<TracePoint>(), false, null, null, 0, null, null);
}
