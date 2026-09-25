using IracingLiveCoach.Core.Telemetry;
using Xunit;

namespace IracingLiveCoach.Core.Tests;

public class CarFlagBadgeTests
{
    [Theory]
    [InlineData(0x10000)]
    [InlineData(0x20000)]
    [InlineData(0x100000)]
    [InlineData(0x100000 | 0x40000)]
    public void Black_disqualify_or_repair_flags_mean_mandatory_pit(int flags)
    {
        var t = new CarFlagBadgeTracker();
        Assert.Equal(FlagBadge.MandatoryPit, t.Update(3, flags, false, 101)); // odd second: blink on
    }

    [Fact]
    public void Furled_means_slow_down()
    {
        var t = new CarFlagBadgeTracker();
        Assert.Equal(FlagBadge.SlowDown, t.Update(3, 0x80000, false, 101));
    }

    [Fact]
    public void Blinks_for_six_seconds_then_holds()
    {
        var t = new CarFlagBadgeTracker();
        Assert.Equal(FlagBadge.SlowDown, t.Update(1, 0x80000, false, 101.2));
        Assert.Equal(FlagBadge.None, t.Update(1, 0x80000, false, 102.2));
        Assert.Equal(FlagBadge.SlowDown, t.Update(1, 0x80000, false, 103.2));
        Assert.Equal(FlagBadge.SlowDown, t.Update(1, 0x80000, false, 108.0)); // 6 s passed: fixed, even second too
    }

    [Theory]
    [InlineData(0x40000, false)]
    [InlineData(0x80000, true)]
    public void Servicible_only_or_pit_road_clears_the_badge(int clearing, bool onPit)
    {
        var t = new CarFlagBadgeTracker();
        t.Update(1, 0x80000, false, 101);
        Assert.Equal(FlagBadge.None, t.Update(1, clearing, onPit, 120));
        Assert.Equal(FlagBadge.None, t.Update(1, 0, false, 121)); // stays cleared
    }

    [Fact]
    public void Zero_flags_keep_the_badge()
    {
        var t = new CarFlagBadgeTracker();
        t.Update(1, 0x80000, false, 101);
        Assert.Equal(FlagBadge.SlowDown, t.Update(1, 0, false, 130));
    }

    [Fact]
    public void Unrelated_flag_change_clears_the_badge()
    {
        var t = new CarFlagBadgeTracker();
        t.Update(1, 0x80000, false, 101);
        Assert.Equal(FlagBadge.None, t.Update(1, 0x1, false, 130));
    }
}
