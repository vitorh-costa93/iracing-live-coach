namespace IracingLiveCoach.Core.Telemetry;

/// <summary>
/// Relative gap = how long the PLAYER would take to cover the physical distance to the other car right
/// now: the lap fraction between the two (wrapped to -0.5..0.5) x the player's own class reference lap.
/// One base for every car, any class. CarIdxEstTime is not used: each car's EstTime runs on ITS class's
/// reference curve, so between classes the difference mixes two curves and drifts while a faster class
/// passes (the user saw the Relative show an interval-like number where Kapps showed the real distance),
/// and even inside one class it is not monotonic along the lap (live start 24/09/2026). Pure, unit-tested.
/// </summary>
public static class RelativeGap
{
    /// <summary>Lap fraction from the player to the other car, wrapped to (-0.5, 0.5]; &gt; 0 = ahead.</summary>
    public static double WrappedDelta(double otherPct, double myPct)
    {
        double delta = otherPct - myPct;
        if (delta > 0.5) delta -= 1;
        else if (delta <= -0.5) delta += 1;
        return delta;
    }

    /// <summary>Seconds (always &gt;= 0) at the player's reference lap time; 0 when no lap time is known.</summary>
    public static double Seconds(double otherPct, double myPct, double myReferenceLapSeconds) =>
        myReferenceLapSeconds > 0 ? Math.Abs(WrappedDelta(otherPct, myPct)) * myReferenceLapSeconds : 0;
}

/// <summary>
/// Each class's reference EstTime curve (CarIdxEstTime as a function of CarIdxLapDistPct), learned live from
/// every car of the class: the same curve for all cars of a class, so a handful of ticks fills it. Pure.
/// </summary>
public sealed class EstTimeCurves
{
    public const int Bins = 1000;
    private readonly Dictionary<int, double[]> _curves = new();

    public void Add(int classId, double pct, double estTime)
    {
        if (pct < 0 || pct >= 1 || estTime <= 0) return;
        if (!_curves.TryGetValue(classId, out var c)) _curves[classId] = c = Enumerable.Repeat(double.NaN, Bins).ToArray();
        c[(int)(pct * Bins)] = estTime; // latest sample per bin
    }

    /// <summary>EstTime of the class at <paramref name="pct"/>, interpolated between the nearest learned bins; null
    /// when the class has no bin within 2 % of the lap.</summary>
    public double? At(int classId, double pct)
    {
        if (!_curves.TryGetValue(classId, out var c) || pct < 0 || pct >= 1) return null;
        int b = (int)(pct * Bins);
        if (!double.IsNaN(c[b])) return c[b];
        int lo = b, hi = b;
        for (int k = 1; k <= Bins / 50; k++)
        {
            if (double.IsNaN(c[lo]) && b - k >= 0) lo = b - k;
            if (double.IsNaN(c[hi]) && b + k < Bins) hi = b + k;
        }
        if (double.IsNaN(c[lo]) || double.IsNaN(c[hi])) return !double.IsNaN(c[lo]) ? c[lo] : !double.IsNaN(c[hi]) ? c[hi] : null;
        return hi == lo ? c[lo] : c[lo] + (c[hi] - c[lo]) * (b - lo) / (double)(hi - lo);
    }

    public void Clear() => _curves.Clear();
}

public static class RelativeGapKapps
{
    /// <summary>
    /// Kapps' Relative gap (fitted on 28 cars over 7 synchronized print+SDK samples, 24/09/2026, multiclass AI
    /// race): the time the OTHER car's class reference needs between the player's spot and the other car's
    /// spot -- EstTime_theirClass(theirPct) - EstTime_theirClass(myPct), wrapped to half a lap of that class.
    /// Mean error 0.15 s (ahead) / 0.28 s (behind, mostly samples with the player crawling out of the pits),
    /// against 0.37 / 0.32 for distance x the player's lap. Same class = the plain EstTime difference; a slower
    /// class through a slow corner reads bigger (the "Greg Hill 2.4" case). Null when the curve is unknown.
    /// </summary>
    public static double? Seconds(double? theirCurveAtThem, double? theirCurveAtMe, double theirClassLap)
    {
        if (theirCurveAtThem is not double a || theirCurveAtMe is not double b || theirClassLap <= 0) return null;
        double d = Math.Abs(a - b) % theirClassLap;
        return Math.Min(d, theirClassLap - d);
    }
}

