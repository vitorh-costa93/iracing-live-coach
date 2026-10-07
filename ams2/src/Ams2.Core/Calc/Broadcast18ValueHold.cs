namespace Ams2.Core.Calc;

/// <summary>Identity and immediate invalidation state of a displayed gap or interval.</summary>
public readonly record struct Broadcast18ValueKey(int Car, int Reference, string Mode,
    int Position, int Laps, int ReferenceLaps, PitState Pit, RaceState Race,
    PitState ReferencePit, RaceState ReferenceRace);

/// <summary>
/// Holds numeric values independently per car/channel, without interpolation. Monza 2021 reference frames
/// show persistence over multiple frames and independent row changes; 1 s is a conservative presentation
/// adjustment, not a measured universal broadcast cadence. Status and missing data always change immediately.
/// </summary>
public sealed class Broadcast18ValueHold
{
    readonly Dictionary<(int Car, string Mode), (Broadcast18ValueKey Key, double Value, double At)> _values = [];
    object? _context;
    double _last = double.NegativeInfinity;

    public static double Period(double seconds) => double.IsFinite(seconds) ? Math.Clamp(seconds, .25, 3) : 1;

    public void Reset() { _values.Clear(); _context = null; _last = double.NegativeInfinity; }

    /// <summary>Context includes session, track, roster and configuration. Call before sampling a frame.</summary>
    public void BeginFrame(double now, object context)
    {
        if (!double.IsFinite(now) || now < _last || !Equals(context, _context)) Reset();
        _context = context;
        _last = now;
    }

    public double? Sample(Broadcast18ValueKey key, double? value, double now, double seconds = 1)
    {
        var slot = (key.Car, key.Mode);
        if (!double.IsFinite(now) || value is not { } v || !double.IsFinite(v) || v < 0)
        { _values.Remove(slot); return null; }
        if (!_values.TryGetValue(slot, out var previous) || previous.Key != key ||
            now < previous.At || now - previous.At >= Period(seconds))
        {
            _values[slot] = (key, v, now);
            return v;
        }
        return previous.Value;
    }
}
