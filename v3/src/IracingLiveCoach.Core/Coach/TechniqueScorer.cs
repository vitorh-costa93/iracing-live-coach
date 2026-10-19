using System;
using System.Collections.Generic;
using System.Linq;

namespace IracingLiveCoach.Core.Coach;

/// <summary>Technique score 0-100 per lap: braking, trail-braking, throttle and consistency
/// (each 0-100, total = mean).</summary>
public static class TechniqueScorer
{
    public static TechniqueBreakdown Score(IReadOnlyList<CornerReport> corners, IReadOnlyList<double> recentValidLapTimes,
        double deltaToRefSec)
    {
        int braking = 100, trail = 100, throttle = 100;
        if (corners.Count > 0)
        {
            braking = Category(corners, c => Math.Abs(c.BrakePointDeltaM) * 2.0 + Math.Max(0, -c.PeakBrakeDelta) * 100.0);
            trail = Category(corners, c => Math.Max(0, -c.TrailBrakeDeltaM) * 2.5);
            throttle = Category(corners, c => Math.Max(0, c.ThrottleOnDeltaM) * 2.5);
        }
        return new TechniqueBreakdown(braking, trail, throttle, Consistency(recentValidLapTimes, deltaToRefSec));
    }

    internal static int Consistency(IReadOnlyList<double> lapTimes, double deltaToRefSec)
    {
        var recent = lapTimes.Skip(Math.Max(0, lapTimes.Count - 5)).ToArray();
        double penalty;
        if (recent.Length >= 2)
        {
            double mean = recent.Average();
            double sd = Math.Sqrt(recent.Sum(t => (t - mean) * (t - mean)) / recent.Length);
            penalty = sd * 50.0; // 1 s of spread = -50
        }
        else penalty = Math.Abs(deltaToRefSec) * 20.0;
        return Clamp100(100 - penalty);
    }

    private static int Category(IReadOnlyList<CornerReport> corners, Func<CornerReport, double> penalty) =>
        Clamp100(100 - corners.Average(c => Math.Min(100, penalty(c))));

    private static int Clamp100(double x) => (int)Math.Round(Math.Clamp(x, 0, 100));
}
