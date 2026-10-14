namespace IracingLiveCoach.Core.Telemetry;

/// <summary>Preserves the SDK status and decoded SF23 bank, with availability tracked per session.</summary>
public sealed class P2PStateTracker
{
    public const int MaxSeconds = 200;
    private readonly P2PCooldownTracker _cooldown = new();
    /// <summary>The bank only ever goes down, and only while the overtake is in use. A drop seen within this window counts as active.</summary>
    public static readonly TimeSpan DrainHold = TimeSpan.FromSeconds(2.5);
    private readonly Dictionary<int, (int Seconds, DateTime DrainedAt)> _bank = new();
    public bool SessionHasP2P { get; private set; }

    /// <summary>
    /// The SDK declares Int32 slots. Real SF23 captures contain integer seconds for the player and
    /// IEEE-754 values in tens of seconds for opponents. Decode the observed encodings without
    /// interpreting unavailable (-1) or corrupt values as an empty bank.
    /// </summary>
    public static int? DecodeSeconds(int? bits)
    {
        if (bits is null) return null;
        if (bits is >= 0 and <= MaxSeconds) return bits;
        float raw = BitConverter.Int32BitsToSingle(bits.Value);
        return raw is >= 0 and <= 20 ? (int)Math.Round(raw * 10) : null;
    }

    public (bool? Active, int? Seconds, bool InCooldown) Update(int carIdx, bool? active, int? rawCountBits, DateTime now)
    {
        int? seconds = DecodeSeconds(rawCountBits);
        if (active == true || seconds > 0) SessionHasP2P = true;
        if (!SessionHasP2P) return (null, null, false);
        // Opponents' CarIdxP2P_Status stayed off while their bank visibly drained (SF23 race, 14/10/2026):
        // a falling bank is the real evidence of use, so it counts as active even when the status says otherwise.
        if (seconds is int s)
        {
            if (_bank.TryGetValue(carIdx, out var prev) && s < prev.Seconds) _bank[carIdx] = (s, now);
            else _bank[carIdx] = (s, _bank.TryGetValue(carIdx, out prev) ? prev.DrainedAt : DateTime.MinValue);
            if (active != true && _bank[carIdx].DrainedAt != DateTime.MinValue && now - _bank[carIdx].DrainedAt <= DrainHold) active = true;
        }
        return (active, seconds, _cooldown.Update(carIdx, active, now));
    }

    public void Reset()
    {
        SessionHasP2P = false;
        _cooldown.Reset();
        _bank.Clear();
    }
}
