namespace Ams2.Core.Calc;

/// <summary>Refreshes a visible event's lifetime without making its entrance replay on every update.</summary>
public sealed class Broadcast04Pulse
{
    double _last = double.NegativeInfinity, _start = double.NaN, _stop = double.NaN;
    public void Reset() { _last = double.NegativeInfinity; _start = _stop = double.NaN; }

    public float Evaluate(double now, double start, double stop)
    {
        if (!double.IsFinite(now) || !double.IsFinite(start) || double.IsNaN(stop) || stop <= start || now < start)
        { Reset(); return 0; }
        if (now < _last) Reset();
        _last = now;
        // Overlapping windows represent refreshes, not a new card. A disjoint one enters again.
        if (double.IsNaN(_start) || start >= _stop || stop <= _start) _start = start;
        _stop = stop;
        if (now >= stop) return 0;
        return (float)Math.Clamp(Math.Min((now - _start) / .16, (stop - now) / .16), 0, 1);
    }
}
