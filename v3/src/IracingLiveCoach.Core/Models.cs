using System.Text.Json;

namespace IracingLiveCoach.Core;

/// <summary>
/// Shared JSON options for every deserialization in this app -- camelCase naming policy matches
/// iracing-analytics's endpoint field names (trackLengthMeters, brakingPointPct, etc.) without
/// needing a [JsonPropertyName] attribute on every property, so a new field the endpoint adds
/// later round-trips automatically as long as the C# property name matches it case-insensitively
/// once camelCased.
/// </summary>
public static class JsonOptions
{
    public static readonly JsonSerializerOptions Baseline = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}

/// <summary>RPM = A*speed + B for one gear, pooled across this driver's own historical laps
/// (see iracing-analytics's lib/local-coach-baselines.ts buildGearModel for how this is computed
/// server-side). Used live to judge "is the engine spinning faster than this car/gear combo
/// normally does at this speed" -- the same corner-relative-baseline philosophy as every other
/// signal in this app.</summary>
public record GearFit(double A, double B);

/// <summary>One corner's historical baseline for all four coaching signals. Any field can be
/// null -- not enough historical laps to trust that specific signal for that specific corner.
/// A null field must never be treated as zero or compared against; skip that signal for that
/// corner instead.</summary>
public record CornerBaseline(
    int Number,
    string? Name,
    double StartPct,
    double EndPct,
    double? BrakingPointPct,
    double? BrakingPointStdDev,
    double? CorrectionBaselineDeg,
    double? WheelspinRatePct,
    double? LapTimeContributionSeconds,
    double? LapTimeStdDev);

/// <summary>The full response from GET /api/telemetry/local-coach/baselines. `Corners` can be
/// an empty list -- "no history yet for this car/track", not an error.</summary>
public record BaselineResponse(
    string Status,
    double? TrackLengthMeters,
    Dictionary<string, GearFit>? GearModel,
    List<CornerBaseline> Corners);
