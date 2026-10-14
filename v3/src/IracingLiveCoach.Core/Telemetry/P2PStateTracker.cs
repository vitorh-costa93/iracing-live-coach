namespace IracingLiveCoach.Core.Telemetry;

/// <summary>Preserves the SDK status and decoded SF23 bank, with availability tracked per session.</summary>
public sealed class P2PStateTracker
{
    public const int MaxSeconds = 200;
    private readonly P2PCooldownTracker _cooldown = new();
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
        // A decreasing bank by itself does not prove that the car is still active in this frame.
        return (active, seconds, _cooldown.Update(carIdx, active, now));
    }

    public void Reset()
    {
        SessionHasP2P = false;
        _cooldown.Reset();
    }
}
