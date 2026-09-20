using IracingLiveCoach.Core.Telemetry;

namespace IracingLiveCoach.Core.Tests;

public class RaceFinishDetectorTests
{
    private static readonly DateTime T0 = new(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);
    private static DateTime At(double seconds) => T0.AddSeconds(seconds);

    [Fact]
    public void Winner_whose_lap_ticks_in_the_same_frame_as_the_flag_is_reported_once_after_the_settle_delay()
    {
        var d = new RaceFinishDetector();
        Assert.False(d.Update(true, false, 27, At(0)));      // last racing tick
        Assert.False(d.Update(true, true, 28, At(1)));       // flag out + lap completed together
        Assert.False(d.Update(true, true, 28, At(2.5)));     // settling (1.5 s)
        Assert.True(d.Update(true, true, 28, At(3.1)));      // 2.1 s after the line
        Assert.False(d.Update(true, true, 28, At(4)));       // never again
    }

    [Fact]
    public void A_non_leader_finishes_only_when_their_own_lap_completes_after_the_flag()
    {
        var d = new RaceFinishDetector();
        d.Update(true, false, 27, At(0));
        Assert.False(d.Update(true, true, 27, At(5)));       // flag out (leader), player still racing
        Assert.False(d.Update(true, true, 27, At(20)));      // never finishes without a new lap
        Assert.False(d.Update(true, true, 28, At(30)));      // player takes the flag now
        Assert.True(d.Update(true, true, 28, At(32.5)));
    }

    [Fact]
    public void Non_race_sessions_never_report()
    {
        var d = new RaceFinishDetector();
        d.Update(false, false, 5, At(0));
        Assert.False(d.Update(false, true, 6, At(1)));
        Assert.False(d.Update(false, true, 6, At(10)));
    }

    [Fact]
    public void Flag_clearing_resets_so_the_next_race_can_report_again()
    {
        var d = new RaceFinishDetector();
        d.Update(true, false, 10, At(0));
        d.Update(true, true, 11, At(1));
        Assert.True(d.Update(true, true, 11, At(4)));
        d.Update(true, false, 0, At(100));                   // new session
        d.Update(true, false, 9, At(200));
        d.Update(true, true, 10, At(201));
        Assert.True(d.Update(true, true, 10, At(204)));
    }

    [Fact]
    public void Joining_after_the_flag_needs_a_real_lap_before_reporting()
    {
        var d = new RaceFinishDetector();
        Assert.False(d.Update(true, true, 20, At(0)));       // first tick already under the flag
        Assert.False(d.Update(true, true, 20, At(10)));
        Assert.False(d.Update(true, true, 21, At(20)));
        Assert.True(d.Update(true, true, 21, At(23)));
    }
}

public class VictoryConfigTests
{
    [Theory]
    [InlineData(VictoryRule.ClassWin, 3, 1, true)]     // won the class while 3rd overall
    [InlineData(VictoryRule.ClassWin, 1, 2, false)]
    [InlineData(VictoryRule.OverallWin, 1, 1, true)]
    [InlineData(VictoryRule.OverallWin, 3, 1, false)]  // class win is not an overall win
    [InlineData(VictoryRule.ClassWin, 1, 0, true)]     // no class channel: overall decides
    [InlineData(VictoryRule.ClassWin, 2, 0, false)]
    public void IsWin_follows_the_configured_rule(VictoryRule rule, int overall, int inClass, bool expected)
        => Assert.Equal(expected, new VictoryConfig(true, "x.mp3", 70, rule).IsWin(new RaceFinish(overall, inClass)));

    [Fact]
    public void Config_round_trips_through_json_as_the_ipc_and_profile_use_it()
    {
        var config = new VictoryConfig(true, @"C:\x\tema.mp3", 55, VictoryRule.OverallWin);
        var back = System.Text.Json.JsonSerializer.Deserialize<VictoryConfig>(System.Text.Json.JsonSerializer.Serialize(config));
        Assert.Equal(config, back);
    }
}
