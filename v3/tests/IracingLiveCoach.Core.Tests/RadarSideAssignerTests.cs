using IracingLiveCoach.Core.Telemetry;

namespace IracingLiveCoach.Core.Tests;

public class RadarSideAssignerTests
{
    private const int Clear = 1, CarLeft = 2, CarRight = 3, Both = 4, TwoLeft = 5;

    [Fact]
    public void A_car_approaching_from_behind_sits_in_the_player_lane_until_it_is_alongside()
    {
        var a = new RadarSideAssigner();
        Assert.Equal(RadarSide.Center, a.Assign([(7, -15.0)], Clear)[7]);
        Assert.Equal(RadarSide.Left, a.Assign([(7, -3.0)], CarLeft)[7]);
    }

    [Fact]
    public void A_car_keeps_its_side_while_it_pulls_away_and_is_forgotten_once_out_of_range()
    {
        var a = new RadarSideAssigner();
        a.Assign([(7, -2.0)], CarRight);
        Assert.Equal(RadarSide.Right, a.Assign([(7, 12.0)], Clear)[7]);
        a.Assign([], Clear);
        Assert.Equal(RadarSide.Center, a.Assign([(7, 12.0)], Clear)[7]);
    }

    [Fact]
    public void Both_sides_go_to_the_two_closest_cars_and_nobody_swaps_lanes()
    {
        var a = new RadarSideAssigner();
        a.Assign([(1, -1.0)], CarLeft);                    // #1 took the left
        var sides = a.Assign([(1, 0.5), (2, -2.0)], Both); // #2 arrives: gets the right, #1 stays left
        Assert.Equal(RadarSide.Left, sides[1]);
        Assert.Equal(RadarSide.Right, sides[2]);
    }

    [Fact]
    public void Two_cars_on_the_left()
    {
        var sides = new RadarSideAssigner().Assign([(1, -1.0), (2, 4.0), (3, -20.0)], TwoLeft);
        Assert.Equal(RadarSide.Left, sides[1]);
        Assert.Equal(RadarSide.Left, sides[2]);
        Assert.Equal(RadarSide.Center, sides[3]);
    }

    [Fact]
    public void An_overlapping_car_iRacing_does_not_flag_stays_in_the_player_lane()
    {
        // e.g. a car right behind in the slipstream: close, but CarLeftRight is clear.
        Assert.Equal(RadarSide.Center, new RadarSideAssigner().Assign([(4, -4.0)], Clear)[4]);
    }
}
