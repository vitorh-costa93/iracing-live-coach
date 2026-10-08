namespace Ams2.Core.Calc;

/// <summary>Horizontal plate reveal. Entry .24 s / exit .20 s are calibrated approximations, not measured broadcast timings.</summary>
public sealed class Broadcast18Motion
{
    public const double EntrySeconds = .24, ExitSeconds = .20;
    double _last = double.NaN, _start = double.NaN, _stop = double.NaN, _session = double.NaN;
    bool _seen, _visible;
    public void Reset() { _last = _start = _stop = _session = double.NaN; _seen = _visible = false; }

    void Clock(double now, double session)
    {
        if (now < _last || (double.IsFinite(session) && double.IsFinite(_session) && session != _session)) Reset();
        _last = now;
        if (double.IsFinite(session)) _session = session;
    }

    public static float Ease(double fraction)
    {
        double p = Math.Clamp(fraction, 0, 1);
        return (float)(1 - Math.Pow(1 - p, 3));
    }

    /// <summary>Overlapping event windows extend the plate without restarting its entrance.</summary>
    public float Evaluate(double now, double start, double stop, double session = double.NaN)
    {
        if (!double.IsFinite(now) || !double.IsFinite(start) || double.IsNaN(stop) || stop <= start || now < start)
        { Reset(); return 0; }
        Clock(now, session);
        if (double.IsNaN(_start) || start >= _stop || stop <= _start) _start = start;
        _stop = stop;
        return now >= stop ? 0 : Math.Min(Ease((now - _start) / EntrySeconds), Ease((stop - now) / ExitSeconds));
    }

    /// <summary>Stable presence, with an optional hold after disappearance. A first already-visible frame is settled for static previews.</summary>
    public float Presence(double now, bool visible, double hold = 0, double session = double.NaN)
    {
        if (!double.IsFinite(now)) { Reset(); return 0; }
        Clock(now, session);
        if (!_seen)
        {
            _start = visible ? now - EntrySeconds : double.NaN;
            _stop = visible ? double.PositiveInfinity : now - ExitSeconds;
            _seen = true;
        }
        else if (visible && !_visible)
        {
            if (now >= _stop) _start = now;
            _stop = double.PositiveInfinity;
        }
        if (!visible && _visible) _stop = now + Math.Max(0, hold) + ExitSeconds;
        _visible = visible;
        if (double.IsNaN(_start) || now >= _stop) return 0;
        return Math.Min(Ease((now - _start) / EntrySeconds), Ease((_stop - now) / ExitSeconds));
    }
}
