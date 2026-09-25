namespace IracingLiveCoach.Core.Telemetry;

/// <summary>Per-driver black-flag state shown next to a Relative row.</summary>
public enum FlagBadge { None, SlowDown, MandatoryPit }

/// <summary>
/// Kapps' per-car flag badge, from CarIdxSessionFlags. A car whose flags are exactly the normal "serviceable"
/// bit, or that sits on pit road, has no badge. Otherwise, when its flags CHANGE: black / disqualify / repair
/// (meatball) mean a mandatory pit visit, furled means slow down; any other change clears the badge. Flags at 0
/// leave the badge as it was. For 6 s after a change the badge blinks (visible on odd seconds), then it holds.
/// Pure; unit-tested.
/// </summary>
public sealed class CarFlagBadgeTracker
{
    public const int Servicible = 0x40000;
    public const int MandatoryPitMask = 0x10000 | 0x20000 | 0x100000;
    public const int Furled = 0x80000;
    public const double BlinkSeconds = 6;

    private sealed record State(int Flags, double ChangedAt, FlagBadge Badge);
    private readonly Dictionary<int, State> _cars = new();

    public void Reset() => _cars.Clear();

    /// <summary>One tick for one car; returns the badge to draw right now (None while blinking off).</summary>
    public FlagBadge Update(int carIdx, int flags, bool onPitRoad, double sessionTime)
    {
        if (flags == Servicible || (flags != 0 && onPitRoad))
        {
            _cars.Remove(carIdx);
            return FlagBadge.None;
        }
        if (flags != 0 && (!_cars.TryGetValue(carIdx, out var known) || known.Flags != flags))
        {
            var badge = (flags & MandatoryPitMask) != 0 ? FlagBadge.MandatoryPit
                : (flags & Furled) != 0 ? FlagBadge.SlowDown
                : FlagBadge.None;
            _cars[carIdx] = new State(flags, Math.Floor(sessionTime), badge);
        }
        if (!_cars.TryGetValue(carIdx, out var state) || state.Badge == FlagBadge.None) return FlagBadge.None;
        bool blinking = sessionTime - state.ChangedAt < BlinkSeconds;
        return blinking && ((long)Math.Floor(sessionTime) % 2 == 0) ? FlagBadge.None : state.Badge;
    }
}
