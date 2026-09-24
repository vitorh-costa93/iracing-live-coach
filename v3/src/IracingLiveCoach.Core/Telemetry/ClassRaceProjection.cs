namespace IracingLiveCoach.Core.Telemetry;

/// <summary>Per-car race lap times, lap number -> time. CarIdxLastLapTime lags a tick at the line, so the
/// value is (re)written every tick while the car is on that lap and the settled value wins. Pure.</summary>
public sealed class LapHistory
{
    private readonly Dictionary<int, SortedDictionary<int, double>> _laps = new();

    public void Update(int carIdx, int lapCompleted, double lastLapTime)
    {
        if (lapCompleted < 1 || lastLapTime <= 1) return;
        if (!_laps.TryGetValue(carIdx, out var laps)) _laps[carIdx] = laps = new SortedDictionary<int, double>();
        laps[lapCompleted] = lastLapTime;
    }

    /// <summary>Average of the last <paramref name="window"/> recorded laps; null when none.</summary>
    public double? RecentAverage(int carIdx, int window = ClassRaceProjection.Window)
    {
        if (!_laps.TryGetValue(carIdx, out var laps) || laps.Count == 0) return null;
        var recent = laps.Values.Skip(Math.Max(0, laps.Count - window)).ToList();
        return recent.Average();
    }

    public void Clear() => _laps.Clear();
}

/// <summary>One class leader for <see cref="ClassRaceProjection"/>.</summary>
public readonly record struct ClassLeader(int ClassId, double Progress, double? AverageLap);

/// <summary>
/// Kapps' per-class "LAP x/≈y" projection in a time-limited race, fitted on a live print (24/09/2026,
/// 45-min 3-class AI race, 25:00 left: Kapps GTP 35.22 / LMP2 33.05 / GT3 31.53; this rule: 35.23 / 33.08 /
/// 31.55, best of every window/variant tried):
///  * the overall leader's class: leaderProgress + timeRemaining / leaderAverage;
///  * every other class: its leader's progress + T / its leader's average, where T is when the race really
///    ends = the overall leader finishing the lap in progress at 0:00: (ceil(projection) - progress) x average.
/// Average = the car's last <see cref="Window"/> race laps. Pure, unit-tested.
/// </summary>
public static class ClassRaceProjection
{
    public const int Window = 5;

    public static Dictionary<int, double> Compute(IReadOnlyList<ClassLeader> classLeaders, int overallLeaderClassId, double timeRemaining)
    {
        var result = new Dictionary<int, double>();
        var overall = classLeaders.FirstOrDefault(l => l.ClassId == overallLeaderClassId);
        if (overall.AverageLap is not double leaderLap || leaderLap <= 1 || timeRemaining < 0) return result;
        double own = overall.Progress + timeRemaining / leaderLap;
        result[overallLeaderClassId] = own;
        double untilEnd = (Math.Ceiling(own - 1e-9) - overall.Progress) * leaderLap;
        foreach (var leader in classLeaders)
        {
            if (leader.ClassId == overallLeaderClassId || leader.AverageLap is not double lap || lap <= 1) continue;
            result[leader.ClassId] = leader.Progress + untilEnd / lap;
        }
        return result;
    }
}
