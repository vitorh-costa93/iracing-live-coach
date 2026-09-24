namespace IracingLiveCoach.Core.Telemetry;

/// <summary>
/// Kapps' Relative gap, reverse-engineered from 40 synchronized print+SDK samples (24/09/2026, multiclass AI
/// race, cars ahead and behind, braking and accelerating): the time the PLAYER needs, on the player's own
/// fastest recorded lap, from the rear car's spot to the front car's spot -- for cars ahead (me -> them) and
/// behind (them -> me) alike. 36/40 within 0.1 s, mean error 0.05 s; every class/speed-based alternative
/// (distance x reference lap, EstTime differences, class curves, distance / current speed, the real elapsed
/// gap) scored 7-28/40. Because it reads a stored trace it does not jump when the player brakes or accelerates.
///
/// Records the player's lap-distance -> elapsed-time trace; keeps the fastest complete lap without pit road.
/// Pure, unit-tested.
/// </summary>
public sealed class OwnLapTrace
{
    public const int Bins = 1000;
    private double[] _current = NewTrace();
    private int _lap = int.MinValue;
    private double _lapStart = double.NaN;
    private bool _dirty;
    private double _lastPct = double.NaN, _lastTime = double.NaN;
    private double[]? _best;
    public double? BestLapSeconds { get; private set; }

    private static double[] NewTrace() => Enumerable.Repeat(double.NaN, Bins).ToArray();

    /// <param name="lapCompleted">CarIdxLapCompleted of the player.</param>
    /// <param name="pct">CarIdxLapDistPct of the player (&lt; 0 = not in the world).</param>
    /// <param name="sessionTime">SessionTime (s).</param>
    /// <param name="onPitRoad">Pit road makes the lap unusable.</param>
    public void Update(int lapCompleted, double pct, double sessionTime, bool onPitRoad)
    {
        if (pct < 0 || pct >= 1) { _dirty = true; _lastPct = double.NaN; return; }
        if (lapCompleted != _lap)
        {
            // Crossed the line: close the lap; the crossing instant is interpolated from the last two samples.
            bool crossed = _lap != int.MinValue && lapCompleted == _lap + 1 && !double.IsNaN(_lastPct) && pct < _lastPct;
            double cross = sessionTime;
            if (crossed)
            {
                double span = pct + 1 - _lastPct;
                if (span > 0) cross = _lastTime + (sessionTime - _lastTime) * (1 - _lastPct) / span;
                if (!double.IsNaN(_lapStart)) Close(cross - _lapStart);
            }
            _lap = lapCompleted;
            _current = NewTrace();
            _lapStart = crossed ? cross : double.NaN;
            _dirty = !crossed; // joined mid-lap (or a reset): this lap's start is unknown
        }
        if (onPitRoad) _dirty = true;
        if (!double.IsNaN(_lapStart)) _current[Math.Min(Bins - 1, (int)(pct * Bins))] = sessionTime - _lapStart;
        _lastPct = pct; _lastTime = sessionTime;
    }

    private void Close(double lapSeconds)
    {
        if (_dirty || lapSeconds <= 1) return;
        int filled = _current.Count(v => !double.IsNaN(v));
        if (filled < Bins * 0.5) return;
        var trace = Fill(_current, lapSeconds);
        if (BestLapSeconds is null || lapSeconds < BestLapSeconds) { _best = trace; BestLapSeconds = lapSeconds; }
    }

    /// <summary>Interpolates the empty bins of a trace (0 at the line, the lap time at the end).</summary>
    private static double[] Fill(double[] raw, double lapSeconds)
    {
        var t = (double[])raw.Clone();
        int prev = -1; double prevV = 0;
        for (int i = 0; i <= Bins; i++)
        {
            double v = i == Bins ? lapSeconds : t[i];
            if (i < Bins && double.IsNaN(v)) continue;
            for (int k = prev + 1; k < i; k++) t[k] = prevV + (v - prevV) * (k - prev) / (i - prev);
            prev = i; prevV = v;
        }
        return t;
    }

    private double At(double pct)
    {
        double x = pct * Bins; int i = Math.Min(Bins - 1, (int)x);
        double a = _best![i], b = i + 1 < Bins ? _best[i + 1] : BestLapSeconds!.Value;
        return a + (b - a) * (x - i);
    }

    /// <summary>Seconds on the player's best lap from <paramref name="rearPct"/> to <paramref name="frontPct"/>
    /// (across the line when needed); null until a complete clean lap has been recorded.</summary>
    public double? Seconds(double rearPct, double frontPct)
    {
        if (_best is null || BestLapSeconds is not double lap || rearPct < 0 || frontPct < 0) return null;
        double d = At(frontPct % 1.0) - At(rearPct % 1.0);
        if (d < 0) d += lap;
        return d;
    }
}
