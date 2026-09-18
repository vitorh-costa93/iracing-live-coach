namespace IracingLiveCoach.Core.Telemetry;

/// <summary>One driver as far as the interval calculation is concerned.</summary>
public readonly record struct IntervalInput(int ClassId, int Position, int ClassPosition, int Laps, double? GapToLeader);

/// <summary>
/// Standings "interval" = time to the car directly ahead IN THE SAME CLASS (the mockups' per-class
/// panels: a class leader shows a dash, the next car "+3.816" behind it). The previous version used
/// the car ahead in the overall order, which in a multiclass field is a different class entirely,
/// and its fallback could return large negative numbers. Pure so it is unit-tested.
/// </summary>
public static class ClassIntervals
{
    /// <returns>One entry per input, aligned by index; null = no car ahead in class (leader) or not
    /// computable. Never negative and never a fabricated zero for missing data.</returns>
    public static double?[] Compute(IReadOnlyList<IntervalInput> drivers, IReadOnlyDictionary<int, double> estimatedTimeByPosition)
    {
        var result = new double?[drivers.Count];
        foreach (var group in Enumerable.Range(0, drivers.Count).GroupBy(i => drivers[i].ClassId))
        {
            var ranked = group.OrderBy(i => drivers[i].ClassPosition > 0 ? drivers[i].ClassPosition : drivers[i].Position).ToList();
            for (int k = 1; k < ranked.Count; k++)
            {
                var me = drivers[ranked[k]];
                var ahead = drivers[ranked[k - 1]];

                double? value = null;
                if (me.Laps == ahead.Laps
                    && estimatedTimeByPosition.TryGetValue(me.Position, out var mine)
                    && estimatedTimeByPosition.TryGetValue(ahead.Position, out var theirs))
                    value = mine - theirs;
                else if (me.GapToLeader is double gapMe && ahead.GapToLeader is double gapAhead)
                    value = gapMe - gapAhead;

                result[ranked[k]] = value is >= 0 ? value : null;
            }
        }
        return result;
    }
}
