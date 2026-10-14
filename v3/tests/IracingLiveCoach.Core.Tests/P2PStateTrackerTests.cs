using IracingLiveCoach.Core.Telemetry;

namespace IracingLiveCoach.Core.Tests;

public class P2PStateTrackerTests
{
    private static readonly DateTime T0 = new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
    private static int OpponentBank(float tensOfSeconds) => BitConverter.SingleToInt32Bits(tensOfSeconds);

    [Fact]
    public void Opponent_raw_status_drives_active_and_cooldown_independently_of_player()
    {
        var tracker = new P2PStateTracker();
        tracker.Update(3, false, 150, T0);
        var active = tracker.Update(17, true, OpponentBank(19.985f), T0);
        Assert.Equal((true, 200, false), active);
        Assert.Equal((false, 150, false), tracker.Update(3, false, 150, T0.AddSeconds(1)));
        Assert.Equal((false, 197, true), tracker.Update(17, false, OpponentBank(19.7f), T0.AddSeconds(3)));
        Assert.Equal((false, 197, false), tracker.Update(17, false, OpponentBank(19.7f), T0.AddSeconds(103)));
    }

    [Fact]
    public void Depleted_bank_remains_known_after_30_seconds_without_fresh_evidence()
    {
        var tracker = new P2PStateTracker();
        tracker.Update(17, false, OpponentBank(1f), T0);
        Assert.Equal((false, 0, false), tracker.Update(17, false, 0, T0.AddMinutes(5)));
    }

    [Fact]
    public void Reset_drops_session_evidence_and_opponent_cooldown()
    {
        var tracker = new P2PStateTracker();
        tracker.Update(17, true, OpponentBank(20f), T0);
        Assert.True(tracker.Update(17, false, OpponentBank(19f), T0.AddSeconds(2)).InCooldown);
        tracker.Reset();
        Assert.False(tracker.SessionHasP2P);
        Assert.Equal(((bool?)null, (int?)null, false), tracker.Update(17, false, 0, T0.AddSeconds(3)));
        Assert.Equal((false, 200, false), tracker.Update(17, false, OpponentBank(20f), T0.AddSeconds(4)));
    }

    [Fact]
    public void Unknown_opponent_status_is_not_fabricated_from_a_decreasing_bank()
    {
        var tracker = new P2PStateTracker();
        tracker.Update(17, null, OpponentBank(20f), T0);
        Assert.Equal(((bool?)null, (int?)190, false), tracker.Update(17, null, OpponentBank(19f), T0.AddSeconds(10)));
        Assert.Equal((false, 180, false), tracker.Update(17, false, OpponentBank(18f), T0.AddSeconds(20)));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(200, 200)]
    [InlineData(196, 196)]
    [InlineData(1101004800, 200)] // observed opponent bits: float 20.0
    [InlineData(-1, null)] // SDK unavailable sentinel
    [InlineData(2147483647, null)]
    [InlineData(-1082130432, null)] // float -1.0
    [InlineData(1101529088, null)] // float 21.0, outside SF23 bank
    public void Raw_bank_decodes_observed_encodings_and_rejects_unavailable(int bits, int? expected)
        => Assert.Equal(expected, P2PStateTracker.DecodeSeconds(bits));

    [Fact]
    public void Missing_channels_stay_unknown_and_zero_only_session_has_no_overtake()
    {
        var tracker = new P2PStateTracker();
        Assert.Equal(((bool?)null, (int?)null, false), tracker.Update(17, false, 0, T0));
        Assert.Null(P2PStateTracker.DecodeSeconds(null));
        tracker.Update(3, false, 200, T0);
        Assert.Equal(((bool?)null, (int?)null, false), tracker.Update(17, null, -1, T0.AddSeconds(1)));
    }
}
