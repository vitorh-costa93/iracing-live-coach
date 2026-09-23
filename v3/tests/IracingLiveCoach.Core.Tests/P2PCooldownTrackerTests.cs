using IracingLiveCoach.Core.Telemetry;

namespace IracingLiveCoach.Core.Tests;

public class P2PCooldownTrackerTests
{
    private static readonly DateTime T0 = new(2026, 9, 23, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Unused_car_is_available_not_locked()
        => Assert.False(new P2PCooldownTracker().Update(3, false, T0));

    [Fact]
    public void After_a_use_the_car_is_locked_for_100_seconds()
    {
        var t = new P2PCooldownTracker();
        t.Update(3, true, T0);
        Assert.True(t.Update(3, false, T0.AddSeconds(12)));
        Assert.True(t.Update(3, false, T0.AddSeconds(111)));   // 99 s after switching off
        Assert.False(t.Update(3, false, T0.AddSeconds(112.5))); // 100.5 s: available again
    }

    [Fact]
    public void A_status_blip_inside_one_activation_reads_as_active()
    {
        var t = new P2PCooldownTracker();
        t.Update(3, true, T0);
        t.Update(3, false, T0.AddSeconds(5));
        Assert.False(t.Update(3, true, T0.AddSeconds(5.3)));
        Assert.True(t.Update(3, false, T0.AddSeconds(10)));
    }

    [Fact]
    public void Cars_are_tracked_independently()
    {
        var t = new P2PCooldownTracker();
        t.Update(1, true, T0);
        t.Update(1, false, T0.AddSeconds(1));
        Assert.False(t.Update(2, false, T0.AddSeconds(2)));
        Assert.True(t.Update(1, false, T0.AddSeconds(2)));
    }
}
