namespace IracingLiveCoach.Core.Telemetry;

/// <summary>One line of the race session's ResultsPositions (Position is 1-based, ClassPosition 0-based;
/// Time is the gap to the overall leader, -1 when unknown).</summary>
public readonly record struct RaceResult(int CarIdx, int Position, int ClassPosition, int LapsComplete, double Time, double LastTime);

/// <summary>One qualifying/grid classification line (Position and ClassPosition 0-based).</summary>
public readonly record struct GridResult(int CarIdx, int Position, int ClassPosition, double FastestTime);

/// <summary>One car's live telemetry for the line-crossing record.</summary>
public readonly record struct CarLapTick(int CarIdx, int ClassId, int Lap, double LapDistPct);

/// <summary>Everything <see cref="RaceLengthEstimator"/> reads in one tick.</summary>
public sealed record RaceLengthTick(
    int SessionState,
    int SessionFlags,
    double SessionTimeRemain,
    bool ResultsOfficial,
    IReadOnlyList<RaceResult> Results,
    IReadOnlyList<CarLapTick> Cars,
    IReadOnlyDictionary<int, int> ClassOfCar,
    IReadOnlyDictionary<int, double> ClassEstLapTime,
    IReadOnlyList<GridResult> Grid,
    double SessionLengthSeconds,
    int? SessionLapLimit);

/// <summary>A class's estimated race length. <see cref="Exact"/>: the session lap limit decides it (no "≈").</summary>
public readonly record struct ClassLapEstimate(double Laps, bool Exact);

/// <summary>
/// Kapps' estimated race length per class ("LAP n/≈X" and the fuel panel's "Laps in Race"). Rules (spec A):
///  1. class lap time: when the class leader's LapsComplete (ResultsPositions) changes in the green race, his
///     LastTime joins a list of the last 5 (not under caution); the lap time is the mean of the list entries
///     below min + 2 s. The same event stores the session time remaining (backup) and the leader's laps;
///  2. every car's line crossing (CarIdxLap +1) stores the time remaining minus the part of the lap already
///     driven (pct x class lap time) and the lap it completed;
///  3. on the class leader's lap change the class's remaining time is rebuilt from the first car in result
///     order whose crossing record matches its ResultsPositions laps, corrected by the gaps (Time); it is used
///     for 2 more leader laps, then the backup + 3 s takes over;
///  4. laps = leader laps + remaining / lap time; every class other than the overall leader's is aligned to the
///     moment the overall leader takes the flag;
///  5. before the class has a lap (or a lap time): the qualifying grid (session length / pole, or the overall leader's whole laps
///     x pole / class pole);
///  6. a session lap limit below the estimate wins, as an exact value.
/// Values only change when ResultsPositions changes (the leader crossing), so they are stable in between.
/// Pure (no SDK); unit-tested.
/// </summary>
public sealed class RaceLengthEstimator
{
    public const int LapWindow = 5;
    private const int CautionMask = 0x4000 | 0x8000;
    private const int Racing = 4;

    private sealed class ClassState
    {
        public readonly List<double> Laps = new();
        public int LapsComplete = -1;
        public double BackupRemain;
        public double? ClassRemain;
        public int ComputedAt = int.MinValue;
        public double? AvgLapTime
        {
            get
            {
                if (Laps.Count == 0) return null;
                double limit = Laps.Min() + 2;
                var kept = Laps.Where(l => l < limit).ToList();
                return kept.Count > 0 ? kept.Average() : null;
            }
        }
        public double? Remain => LapsComplete < 0 ? null : ClassRemain is double r && LapsComplete <= ComputedAt + 2 ? r : BackupRemain + 3;
    }

    private readonly Dictionary<int, ClassState> _classes = new();
    private readonly Dictionary<int, int> _lastCarLap = new();
    private readonly Dictionary<int, (double Remain, int Laps)> _line = new();
    private Dictionary<int, ClassLapEstimate> _estimates = new();

    /// <summary>Class id -> estimate (the last computed; frozen outside SessionState 1..4).</summary>
    public IReadOnlyDictionary<int, ClassLapEstimate> Estimates => _estimates;

    /// <summary>Class lap time used by the rules (null before the class leader has a lap time).</summary>
    public double? AverageLapTime(int classId) => _classes.TryGetValue(classId, out var s) ? s.AvgLapTime : null;

    public void Reset()
    {
        _classes.Clear();
        _lastCarLap.Clear();
        _line.Clear();
        _estimates = new();
    }

    private ClassState State(int classId)
    {
        if (!_classes.TryGetValue(classId, out var s)) _classes[classId] = s = new ClassState();
        return s;
    }

    private double? LapTime(int classId, RaceLengthTick t) =>
        State(classId).AvgLapTime ?? (t.ClassEstLapTime.TryGetValue(classId, out var est) && est > 0 ? est : null);

    public IReadOnlyDictionary<int, ClassLapEstimate> Update(RaceLengthTick t)
    {
        RecordCrossings(t);

        var byClass = t.Results
            .Where(r => t.ClassOfCar.ContainsKey(r.CarIdx))
            .GroupBy(r => t.ClassOfCar[r.CarIdx])
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.Position).ToList());
        var overallLeader = t.Results.Where(r => r.Position > 0).OrderBy(r => r.Position).Cast<RaceResult?>().FirstOrDefault();

        if (t.SessionState == Racing && !t.ResultsOfficial)
            foreach (var (classId, cars) in byClass)
            {
                var leader = cars.Where(r => r.ClassPosition == 0).Cast<RaceResult?>().FirstOrDefault() ?? cars[0];
                var s = State(classId);
                if (leader.LapsComplete == s.LapsComplete) continue;
                if (leader.LastTime > 0 && (t.SessionFlags & CautionMask) == 0)
                {
                    s.Laps.Add(leader.LastTime);
                    if (s.Laps.Count > LapWindow) s.Laps.RemoveAt(0);
                }
                s.BackupRemain = t.SessionTimeRemain;
                s.LapsComplete = leader.LapsComplete;
                RebuildClassRemain(s, leader, cars, overallLeader);
            }

        if (t.SessionState is -1 or 0 or 5 or 6) return _estimates;
        _estimates = Compute(t, overallLeader);
        return _estimates;
    }

    /// <summary>Rule 2: time remaining when each car crossed the line (minus what it already drove of the new lap).</summary>
    private void RecordCrossings(RaceLengthTick t)
    {
        foreach (var car in t.Cars)
        {
            if (_lastCarLap.TryGetValue(car.CarIdx, out var previous) && car.Lap == previous + 1)
            {
                double lapTime = LapTime(car.ClassId, t) ?? 0;
                double remain = t.SessionTimeRemain - Math.Max(0, car.LapDistPct) * lapTime;
                if (double.IsNaN(remain) || remain < 0) remain = 0;
                _line[car.CarIdx] = (remain, car.Lap - 1);
            }
            _lastCarLap[car.CarIdx] = car.Lap;
        }
    }

    /// <summary>Rule 3.</summary>
    private void RebuildClassRemain(ClassState s, RaceResult leader, List<RaceResult> cars, RaceResult? overallLeader)
    {
        if (leader.LapsComplete < 2) return;
        foreach (var p in cars)
        {
            if (!_line.TryGetValue(p.CarIdx, out var line) || line.Laps != p.LapsComplete) continue;
            if (p.Time == -1 || leader.Time == -1) continue;
            if (leader.LapsComplete == line.Laps)
            {
                s.ClassRemain = line.Remain + (p.Time - leader.Time);
                s.ComputedAt = s.LapsComplete;
                return;
            }
            if (leader.LapsComplete == line.Laps + 1 && overallLeader is { } ol && ol.LastTime != -1 && leader.LastTime != -1)
            {
                s.ClassRemain = line.Remain + p.Time - (ol.LastTime + leader.Time);
                s.ComputedAt = s.LapsComplete;
                return;
            }
        }
    }

    private Dictionary<int, ClassLapEstimate> Compute(RaceLengthTick t, RaceResult? overallLeader)
    {
        var classIds = t.ClassOfCar.Values.Distinct().ToList();
        var laps = new Dictionary<int, double>();
        var grid = GridEstimates(t);

        int? leaderClass = overallLeader is { } ol && t.ClassOfCar.TryGetValue(ol.CarIdx, out var lc) ? lc : null;
        foreach (var c in classIds)
        {
            var s = State(c);
            // Before the class has a lap (or a lap time: the leader's first LastTime after the start is -1 -- Kapps kept
            // the grid "≈38.34" = 2700 / 70.4252 through the leader's first lap, 24/09/2026) the grid decides.
            bool hasLaps = s.LapsComplete > 0 && s.Remain is not null && LapTime(c, t) is not null;
            if (s.AvgLapTime is null && grid.TryGetValue(c, out var g)) laps[c] = g;
            else if (hasLaps) laps[c] = s.LapsComplete + s.Remain!.Value / LapTime(c, t)!.Value;
            else if (grid.TryGetValue(c, out var g2)) laps[c] = g2;
        }

        // Rule 4: every other class ends when the overall leader takes the flag.
        if (leaderClass is int L && laps.ContainsKey(L) && State(L) is { LapsComplete: > 0, AvgLapTime: not null } ls && ls.Remain is double remainL && LapTime(L, t) is double lapL)
            foreach (var c in classIds)
            {
                if (c == L) continue;
                var s = State(c);
                if (s.LapsComplete <= 0 || s.AvgLapTime is null || s.Remain is not double remainC || LapTime(c, t) is not double lapC) continue;
                double leaderRemain = Math.Ceiling(laps[L] - ls.LapsComplete - 1e-9) * lapL - (remainL - remainC);
                leaderRemain = Math.Max(1, leaderRemain);
                laps[c] = s.LapsComplete + leaderRemain / lapC;
            }

        var result = new Dictionary<int, ClassLapEstimate>();
        foreach (var (c, v) in laps)
            result[c] = t.SessionLapLimit is int limit && limit > 0 && limit < v ? new ClassLapEstimate(limit, true) : new ClassLapEstimate(v, false);
        // A lap-limited session with no estimate yet still has its exact length.
        if (t.SessionLapLimit is int only && only > 0)
            foreach (var c in classIds)
                result.TryAdd(c, new ClassLapEstimate(only, true));
        return result;
    }

    /// <summary>Rule 5: from the qualifying grid.</summary>
    private static Dictionary<int, double> GridEstimates(RaceLengthTick t)
    {
        var result = new Dictionary<int, double>();
        if (t.SessionLengthSeconds <= 0 || t.Grid.Count == 0) return result;
        var pole = t.Grid.OrderBy(g => g.Position).First();
        if (pole.FastestTime <= 0) return result;
        double lapsLeader = t.SessionLengthSeconds / pole.FastestTime;
        foreach (var cls in t.Grid.Where(g => t.ClassOfCar.ContainsKey(g.CarIdx)).GroupBy(g => t.ClassOfCar[g.CarIdx]))
        {
            var pc = cls.OrderBy(g => g.ClassPosition).First();
            if (pc.FastestTime <= 0) continue;
            result[cls.Key] = pc.CarIdx == pole.CarIdx
                ? t.SessionLengthSeconds / pc.FastestTime
                : Math.Ceiling(lapsLeader - 1e-9) * pole.FastestTime / pc.FastestTime;
        }
        return result;
    }
}
