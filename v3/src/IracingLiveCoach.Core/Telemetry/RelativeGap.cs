namespace IracingLiveCoach.Core.Telemetry;

/// <summary>
/// Live reference-curve gap calculation and the linear distance fallback used when a curve is unknown.
/// Relative uses the nearest gap on the other car's class curve; standings retain the forward interval.
/// </summary>
public static class RelativeGap
{
    /// <summary>Live reference-curve gap shared by Relative and race intervals. Relative asks for the
    /// nearest physical gap; a standings interval supplies the forward race distance so a car 0.8 laps
    /// ahead is measured along those 0.8 laps, rather than the nearest 0.2 laps behind.</summary>
    public static double? LiveSeconds(double? curveAtOther, double? curveAtOrigin, double? classLapSeconds,
        double lapDelta, double? fallbackLapSeconds, bool forward = false, bool crossedLine = false)
    {
        if (curveAtOther is double a && double.IsFinite(a) && curveAtOrigin is double b && double.IsFinite(b))
        {
            if (classLapSeconds is > 0 and double lap && double.IsFinite(lap))
            {
                double delta = Math.Abs(a - b) % lap;
                if (!forward) return Math.Min(delta, lap - delta);
                // Progress identifies a real finish-line wrap; the sign alone cannot distinguish it
                // from a small local EstTime inversion. Spatial half-laps cannot split a nonuniform curve.
                if (!crossedLine) return delta;
                double wrapped = (a - b) % lap;
                return wrapped < 0 ? wrapped + lap : wrapped;
            }
            if (forward && a >= b) return a - b;
        }
        return fallbackLapSeconds is > 0 and double fallback && double.IsFinite(fallback) && double.IsFinite(lapDelta)
            ? Math.Abs(lapDelta) * fallback : null;
    }

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

