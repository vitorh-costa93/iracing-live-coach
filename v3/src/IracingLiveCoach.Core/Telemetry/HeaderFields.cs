using System.Globalization;

namespace IracingLiveCoach.Core.Telemetry;

/// <summary>One entry in a widget's configurable header (spec §12: "cabeçalhos configuráveis, com
/// campos reordenáveis"). List order in the owning collection IS the display order.</summary>
public sealed record HeaderFieldConfig(string Key, bool Visible, string? FontFamily = null, int? FontWeight = null);

/// <summary>Builds header text from an ordered field list. Pure and GPU-free so it is unit-tested
/// and shared by Standings/Relative. A field whose data isn't available yet is skipped, never
/// replaced with a placeholder (spec §15: never invent a value).</summary>
public static class HeaderFields
{
    public static readonly IReadOnlyList<string> AllKeys =
        ["class", "type", "lap", "sof", "drivers", "clock", "bb", "track", "rubber", "best", "last", "local", "incidents", "remain"];

    public static List<HeaderFieldConfig> DefaultStandings() =>
        Complete([new("class", true), new("lap", true), new("sof", true), new("clock", true)]);

    public static List<HeaderFieldConfig> DefaultRelative() =>
        Complete([new("bb", true), new("track", true), new("local", true)]);

    /// <summary>Appends every known key missing from <paramref name="fields"/> as hidden, so the
    /// Control Center always lists every available field, and drops unknown keys.</summary>
    public static List<HeaderFieldConfig> Complete(IEnumerable<HeaderFieldConfig> fields)
    {
        var result = fields.Where(f => AllKeys.Contains(f.Key)).GroupBy(f => f.Key).Select(g => g.First()).ToList();
        foreach (var key in AllKeys)
            if (result.All(f => f.Key != key)) result.Add(new HeaderFieldConfig(key, false));
        return result;
    }

    /// <summary>Lap-limited: "30". Time-limited: Kapps' "≈34.53" -- the projection ROUNDED to two decimals (live
    /// 24/09/2026: 2700 / 78.1975 = 34.5279 shown as "34.53"; the earlier truncation had no numeric evidence);
    /// the integer "≈12" only when no projection is known.</summary>
    public static string TotalLapsText(SessionStatus session)
    {
        if (session.TotalLapsEstimated && session.TotalLapsProjected is double p && p > 0)
            return "≈" + Math.Round(p, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);
        return (session.TotalLapsEstimated ? "≈" : "") + (session.TotalLaps?.ToString(CultureInfo.InvariantCulture) ?? "—");
    }

    /// <summary>Lap field of one class's Standings panel (Kapps, live 24/09/2026): "LAP 15/≈33.05" with that
    /// class's own projection (ClassRaceProjection); before one exists the overall leader's class shows the
    /// session projection ("R 0/≈34.53") and every other class just its leader's lap ("Lap 1").</summary>
    public static string? ClassLapText(SessionStatus? session, int classId, bool isOverallLeaderClass)
    {
        if (session?.ClassLaps is { } laps && laps.TryGetValue(classId, out var info))
        {
            if (info.FinalTotal is int final)
                return $"LAP {info.Lap.ToString(CultureInfo.InvariantCulture)}/{final.ToString(CultureInfo.InvariantCulture)}";
            if (info.Projected is double p)
                return $"LAP {info.Lap.ToString(CultureInfo.InvariantCulture)}/≈" + Math.Round(p, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);
            if (!isOverallLeaderClass) return $"LAP {info.Lap.ToString(CultureInfo.InvariantCulture)}";
        }
        return Text("lap", session, null, DateTime.Now);
    }

    public static string? Text(string key, SessionStatus? session, PlayerCarStatus? player, DateTime now)
    {
        string Int(int? v) => v?.ToString(CultureInfo.InvariantCulture) ?? "—";
        return key switch
        {
            "type" => session is { SessionTypeText.Length: > 0 } ? session.SessionTypeText : null,
            "class" => session is { CarClassShortName.Length: > 0 } ? session.CarClassShortName : null,
            "lap" => session is null ? null : $"LAP {Int(session.CurrentLap)}/{TotalLapsText(session)}",
            "sof" => session?.StrengthOfField is double s ? "SOF " + NumberFormatConfig.GroupThousands((int)Math.Round(s)) : null,
            "drivers" => session is null ? null : $"{session.DriverCountText(session.PlayerClassId)} DRIVERS",
            "bb" => player?.BrakeBiasPct is double b ? "BB " + b.ToString("0.0", CultureInfo.InvariantCulture) + "%" : null,
            "track" => player?.TrackTempC is double t ? "TRACK " + t.ToString("0", CultureInfo.InvariantCulture) + "°C" : null,
            "rubber" => player?.TrackRubberState is { Length: > 0 } r ? "RUBBER " + r.ToUpperInvariant() : null,
            "best" => player?.BestLapTimeSeconds is double bl ? "BEST " + LapTimeFormatting.Format(bl) : null,
            "last" => player?.LastLapTimeSeconds is double ll ? "LAST " + LapTimeFormatting.Format(ll) : null,
            "remain" => session?.TimeRemainSeconds is double rem ? "REMAIN " + ((int)Math.Ceiling(rem / 60.0 - 1e-9)).ToString(CultureInfo.InvariantCulture) + " MIN" : null,
            "clock" => now.ToString("HH:mm", CultureInfo.InvariantCulture),
            "local" => "LOCAL " + now.ToString("HH:mm", CultureInfo.InvariantCulture),
            "incidents" => player?.Incidents is int inc ? $"INC {inc}x" + (player.IncidentLimit is int lim ? $"/{lim}x" : "") : null,
            _ => null,
        };
    }

    public static string Compose(IEnumerable<HeaderFieldConfig> fields, SessionStatus? session, PlayerCarStatus? player, DateTime now) =>
        string.Join("   ", fields.Where(f => f.Visible).Select(f => Text(f.Key, session, player, now)).Where(t => t is not null));
}
