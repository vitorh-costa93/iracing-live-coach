namespace IracingLiveCoach.Core.Telemetry;

/// <summary>One driver as far as the interval calculation is concerned.</summary>
/// <param name="Progress">Race distance covered: CarIdxLapCompleted + CarIdxLapDistPct (null when the
/// car is not in the world, i.e. LapDistPct &lt; 0).</param>
/// <param name="EstTime">CarIdxEstTime: the car's estimated time from the line to its current spot,
/// on its CLASS's reference lap -- the same curve for every car of a class, so the difference between
/// two cars of one class is the time the one behind needs to reach where the one ahead is.</param>
/// <param name="GapToLeader">CarIdxF2Time in a race (time behind the leader, updated at timing lines);
/// only a fallback here.</param>
/// <param name="BestLap">Best lap (practice/qualifying: CarIdxF2Time is the car's own fastest lap).</param>
public readonly record struct IntervalInput(int ClassId, int Position, int ClassPosition, double? Progress, double? EstTime, double? GapToLeader, double? BestLap = null);

/// <summary>Interval to the car directly ahead in the class: either seconds or whole laps (never both).
/// Both null = class leader or not computable.</summary>
public readonly record struct IntervalValue(double? Seconds, int? Laps)
{
    public static readonly IntervalValue None = new(null, null);
}

/// <summary>
/// Standings "interval" = gap to the car directly ahead IN THE SAME CLASS, the way Kapps shows it
/// ("INT" on the class leader, "1.5", "0.4", ... and "1L" when that car is a lap or more up the road).
///
/// Race rule:
///  * laps: the car ahead has covered >= 1 full lap more (LapCompleted + LapDistPct) -> floor(diff) "L".
///  * otherwise seconds from CarIdxEstTime: est(ahead) - est(me); when the car ahead has already crossed
///    the line and I have not, that is negative and one class lap time is added. This moves every
///    frame, unlike CarIdxF2Time, which only updates at timing lines.
///  * fallback (no position on track for one of the two): CarIdxF2Time difference.
/// The previous version compared CarIdxLap (lap NUMBER): two cars a few tenths apart either side of
/// the line have different lap numbers, so it fell back to the stale F2Time difference, which is often
/// negative right after the line -> null -> the "—" seen in real sessions where Kapps showed 0.4/1.5.
///
/// Practice / qualifying rule: difference of best laps to the car ahead in class (the order is by best
/// lap there); no time for either -> null.
/// Never negative and never a fabricated zero for missing data. Pure so it is unit-tested.
/// </summary>
public static class ClassIntervals
{
    /// <param name="timeBetween">Kapps' track-time function (rear lap fraction, front lap fraction) -> seconds, the
    /// same base as the Relative (OwnLapTrace: the player's best lap between the two spots); null/none = the
    /// distance x class-lap fallback.</param>
    public static IntervalValue[] Compute(IReadOnlyList<IntervalInput> drivers, bool raceMode, IReadOnlyDictionary<int, double>? lapTimeByClass = null, Func<double, double, double?>? timeBetween = null)
    {
        var result = new IntervalValue[drivers.Count];
        foreach (var group in Enumerable.Range(0, drivers.Count).GroupBy(i => drivers[i].ClassId))
        {
            var ranked = group.OrderBy(i => drivers[i].ClassPosition > 0 ? drivers[i].ClassPosition : int.MaxValue)
                .ThenBy(i => drivers[i].Position > 0 ? drivers[i].Position : int.MaxValue).ToList();
            double? lapTime = lapTimeByClass is not null && lapTimeByClass.TryGetValue(group.Key, out var lt) && lt > 1 ? lt : null;
            for (int k = 1; k < ranked.Count; k++)
            {
                var me = drivers[ranked[k]];
                var ahead = drivers[ranked[k - 1]];
                result[ranked[k]] = raceMode ? RaceInterval(me, ahead, lapTime, timeBetween) : TimedInterval(me, ahead);
            }
        }
        return result;
    }

    public static IntervalValue RaceInterval(IntervalInput me, IntervalInput ahead, double? classLapTime, Func<double, double, double?>? timeBetween = null)
    {
        if (me.Progress is double mine && ahead.Progress is double theirs)
        {
            double diff = theirs - mine;
            if (diff >= 1.0) return new IntervalValue(null, (int)Math.Floor(diff + 1e-9));
            // After the flag (official classification: no live EstTime) the same-lap interval is the difference
            // of the final race times -- Kapps "17.5" for ResultsPositions Time 17.5409 vs the winner's 0.
            if (me.EstTime is null && ahead.EstTime is null && me.GapToLeader is double finalMe && ahead.GapToLeader is double finalAhead && finalMe - finalAhead >= 0)
                return new IntervalValue(finalMe - finalAhead, null);
            // Kapps: the distance between the two cars x the class reference lap (CarClassEstLapTime).
            // Live start 24/09/2026: Sean 0.021 laps behind Ben -> 1.42, Kapps "1.4" (EstTime gave 1.22);
            // Josh 0.005 behind Jonas -> 0.34, Kapps "0.3" (EstTime was negative there: not monotonic).
            // Same base as the Relative: time along a reference lap between the two spots, so braking or
            // accelerating does not make the interval jump (user, 24/09/2026).
            if (diff >= 0 && me.EstTime is not null && timeBetween?.Invoke(mine - Math.Floor(mine), theirs - Math.Floor(theirs)) is double t0)
                return new IntervalValue(t0, null);
            if (diff >= 0 && classLapTime is double lap) return new IntervalValue(diff * lap, null);
            if (diff >= 0 && me.EstTime is double myEst && ahead.EstTime is double theirEst)
            {
                double t = theirEst - myEst;
                if (t >= 0) return new IntervalValue(t, null);
            }
        }
        if (me.GapToLeader is double gapMe && ahead.GapToLeader is double gapAhead && gapMe - gapAhead >= 0)
            return new IntervalValue(gapMe - gapAhead, null);
        return IntervalValue.None;
    }

    public static IntervalValue TimedInterval(IntervalInput me, IntervalInput ahead) =>
        me.BestLap is > 0 and double mine && ahead.BestLap is > 0 and double theirs && mine - theirs >= 0
            ? new IntervalValue(mine - theirs, null)
            : IntervalValue.None;
}
