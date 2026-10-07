using Ams2.Core.Calc;

namespace Ams2.Core.Tests;

public class Broadcast18ValueHoldTests
{
    static Broadcast18ValueKey Key(int car = 2, int reference = 1, string mode = "gap") =>
        new(car, reference, mode, 2, 0, 0, PitState.None, RaceState.Racing, PitState.None, RaceState.Racing);

    [Fact]
    public void First_sample_is_immediate_then_holds_until_period_without_interpolation()
    {
        var hold = new Broadcast18ValueHold();
        hold.BeginFrame(10, "race");
        Assert.Equal(3.259, hold.Sample(Key(), 3.259, 10));
        hold.BeginFrame(10.4, "race");
        Assert.Equal(3.259, hold.Sample(Key(), 3.211, 10.4));
        hold.BeginFrame(10.999, "race");
        Assert.Equal(3.259, hold.Sample(Key(), 3.100, 10.999));
        hold.BeginFrame(11, "race");
        Assert.Equal(3.100, hold.Sample(Key(), 3.100, 11));
        Assert.Equal(3.100, hold.Sample(Key(), 3.000, 11.1));
    }

    [Fact]
    public void Rows_and_gap_interval_channels_have_independent_publication_times()
    {
        var hold = new Broadcast18ValueHold();
        hold.BeginFrame(0, "race");
        Assert.Equal(4d, hold.Sample(Key(), 4, 0));
        Assert.Equal(8d, hold.Sample(Key(car: 3), 8, .4));
        Assert.Equal(2d, hold.Sample(Key(mode: "interval"), 2, .6));
        Assert.Equal(5d, hold.Sample(Key(), 5, 1));
        Assert.Equal(8d, hold.Sample(Key(car: 3), 9, 1));
        Assert.Equal(2d, hold.Sample(Key(mode: "interval"), 3, 1));
    }

    [Fact]
    public void Reference_mode_laps_position_and_status_changes_are_immediate()
    {
        var hold = new Broadcast18ValueHold();
        var key = Key();
        foreach (var changed in new[] { key with { Reference = 3 }, key with { Mode = "interval" },
            key with { Laps = 1 }, key with { ReferenceLaps = 1 }, key with { Position = 3 },
            key with { Pit = PitState.InPit }, key with { Race = RaceState.Retired },
            key with { ReferencePit = PitState.InPit }, key with { ReferenceRace = RaceState.Finished } })
        {
            hold.Reset(); hold.BeginFrame(10, "race");
            Assert.Equal(4d, hold.Sample(key, 4, 10));
            Assert.Equal(5d, hold.Sample(changed, 5, 10.1));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1d)]
    public void Missing_or_invalid_values_clear_immediately_and_recovery_is_immediate(double? invalid)
    {
        var hold = new Broadcast18ValueHold();
        hold.BeginFrame(10, "race");
        Assert.Equal(4d, hold.Sample(Key(), 4, 10));
        Assert.Null(hold.Sample(Key(), invalid, 10.1));
        Assert.Equal(5d, hold.Sample(Key(), 5, 10.2));
    }

    [Fact]
    public void Session_track_roster_configuration_rewind_and_disconnect_reset()
    {
        var hold = new Broadcast18ValueHold();
        var context = (Session: 1, Track: "Monza", Roster: "1,2", Period: 1d);
        foreach (object changed in new object[] { context with { Session = 2 }, context with { Track = "Spa" },
            context with { Roster = "1,3" }, context with { Period = 2 } })
        {
            hold.Reset(); hold.BeginFrame(10, context);
            Assert.Equal(4d, hold.Sample(Key(), 4, 10));
            hold.BeginFrame(10.1, changed);
            Assert.Equal(5d, hold.Sample(Key(), 5, 10.1));
        }
        hold.BeginFrame(1, context);
        Assert.Equal(6d, hold.Sample(Key(), 6, 1));
        hold.Reset(); // widget disconnect
        hold.BeginFrame(1.1, context);
        Assert.Equal(7d, hold.Sample(Key(), 7, 1.1));
    }

    [Theory]
    [InlineData(.01, .25)]
    [InlineData(10, 3)]
    [InlineData(double.NaN, 1)]
    [InlineData(double.PositiveInfinity, 1)]
    public void Period_is_bounded(double configured, double expected) =>
        Assert.Equal(expected, Broadcast18ValueHold.Period(configured));
}
