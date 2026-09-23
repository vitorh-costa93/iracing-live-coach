namespace IracingLiveCoach.Core.Telemetry;

/// <summary>
/// Push-to-pass availability. iRacing publishes only "active or not" and the remaining bank; after a
/// car switches it off it cannot re-activate for a fixed period. Measured on 253 real reactivations
/// (SF23, iRacing): none came back sooner than ~99.7 s, so the rule is a 100 s lock-out after each use
/// -- "unavailable" (yellow). Off-on blips shorter than a couple of seconds are status noise inside
/// one activation, not a new use, and simply read as active again.
/// </summary>
public sealed class P2PCooldownTracker
{
    public static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(100);

    private readonly HashSet<int> _active = new();
    private readonly Dictionary<int, DateTime> _offAt = new();

    /// <summary>Feeds one car's status; returns true while that car is locked out after a use.</summary>
    public bool Update(int carIdx, bool? active, DateTime now)
    {
        if (active == true)
        {
            _active.Add(carIdx);
            _offAt.Remove(carIdx);
            return false;
        }
        if (active == false && _active.Remove(carIdx)) _offAt[carIdx] = now;
        if (!_offAt.TryGetValue(carIdx, out var off)) return false;
        if (now - off < Cooldown) return active == false;
        _offAt.Remove(carIdx);
        return false;
    }

    public void Reset()
    {
        _active.Clear();
        _offAt.Clear();
    }
}
