namespace IracingLiveCoach.Core.Telemetry;

/// <summary>
/// Recent (session time, lap fraction) trail of every car. The in-sim black box Relative is the REAL time
/// between two cars at the same spot: "how long ago did the car ahead pass where I am now" (ahead) and "how
/// long ago did I pass where the car behind is now" (behind). Checked on a 12/10/2026 race log against the
/// black box: the CarIdxEstTime difference read ~0.5 s low ahead and matched behind, the passage time matches
/// both. Pure so it is unit-tested.
/// </summary>
public sealed class PassageHistory
{
    private const double MinStep = 0.05;      // s between stored samples (keeps ~1800 samples per 90 s)
    private const double MaxAge = 90;         // s of trail kept
    private const double MaxSegment = 1.5;    // s: longer holes are not interpolated across

    private readonly Dictionary<int, List<(double T, double Pct)>> _trails = new();

    public void Record(int car, double time, double pct)
    {
        if (!double.IsFinite(time) || !double.IsFinite(pct)) return;
        if (pct < 0 || pct >= 1) { _trails.Remove(car); return; }
        if (!_trails.TryGetValue(car, out var trail)) _trails[car] = trail = new List<(double, double)>();
        if (trail.Count > 0)
        {
            var last = trail[^1];
            if (time < last.T) { trail.Clear(); } // session time went backwards (new session / replay)
            else if (time - last.T < MinStep) return;
        }
        trail.Add((time, pct));
        int drop = 0;
        while (drop < trail.Count - 1 && time - trail[drop].T > MaxAge) drop++;
        if (drop > 0) trail.RemoveRange(0, drop);
    }

    /// <summary>Session time at which the car last crossed <paramref name="pct"/>; null when its trail does not
    /// cover that spot recently.</summary>
    public double? TimeAt(int car, double pct)
    {
        if (!_trails.TryGetValue(car, out var trail) || trail.Count < 2) return null;
        for (int i = trail.Count - 1; i > 0; i--)
        {
            var (t1, p1) = trail[i - 1];
            var (t2, p2) = trail[i];
            if (t2 - t1 > MaxSegment) continue;
            double span = p2 - p1;
            if (span < -0.5) span += 1;                  // finish-line wrap inside the segment
            if (span <= 0) continue;                      // stopped or a backwards jitter
            double into = pct - p1;
            if (into < -0.5) into += 1; else if (into > 0.5) into -= 1;
            if (into >= 0 && into < span) return t1 + (t2 - t1) * into / span;
        }
        return null;
    }

    /// <summary>Seconds since the car at <paramref name="car"/> passed <paramref name="pct"/>, at <paramref name="now"/>.</summary>
    public double? SecondsSince(int car, double pct, double now) =>
        TimeAt(car, pct) is double t && now - t >= 0 ? now - t : null;

    /// <summary>The passage time is only trusted while it agrees with the EstTime-curve gap (within 1 s or 30 %).
    /// A parked car (grid, pit stall, stopped on track) keeps "passing my spot" further into the past, so its
    /// passage time grows without bound while the black box still reads the small distance (photo, 29/10/2026:
    /// 1.6 vs 0.1 on the grid). Disagreement = fall back to the curve gap.</summary>
    public static double Trusted(double? passage, double curveGap) =>
        passage is double p && Math.Abs(p - curveGap) <= Math.Max(1.0, 0.3 * curveGap) ? p : curveGap;

    public void Clear() => _trails.Clear();
}
