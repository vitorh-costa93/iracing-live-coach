using System;
using System.Collections.Generic;
using System.Linq;
using IracingLiveCoach.Core.Telemetry;
using Xunit;

namespace IracingLiveCoach.Core.Tests;

public class IRatingProjectionTests
{
    [Fact]
    public void Two_equal_drivers_winner_gains_fifty_loser_loses_forty_nine()
    {
        var r = IRatingProjection.Compute([new IRatingEntry(10, 1500, 1), new IRatingEntry(11, 1500, 2)]);
        Assert.Equal(50.0, r[10], 6);
        Assert.Equal(-49.0, r[11], 6);
    }

    [Fact]
    public void Chance_is_symmetric_and_half_between_equals()
    {
        Assert.Equal(0.5, IRatingProjection.Chance(2000, 2000), 9);
        Assert.Equal(1.0, IRatingProjection.Chance(3000, 1500) + IRatingProjection.Chance(1500, 3000), 9);
        Assert.True(IRatingProjection.Chance(3000, 1500) > 0.5);
    }

    [Fact]
    public void Beating_stronger_drivers_is_worth_more_than_beating_weaker_ones()
    {
        var upset = IRatingProjection.Compute([new IRatingEntry(1, 1200, 1), new IRatingEntry(2, 3000, 2), new IRatingEntry(3, 3000, 3)]);
        var expected = IRatingProjection.Compute([new IRatingEntry(1, 3000, 1), new IRatingEntry(2, 1200, 2), new IRatingEntry(3, 1200, 3)]);
        Assert.True(upset[1] > expected[1]);
        Assert.True(upset[1] > 0 && expected[1] > 0);
    }

    [Fact]
    public void Ai_and_unrated_cars_are_excluded_and_the_field_is_reranked()
    {
        // AI in P1 (iRating 1) is ignored: the rated P2 is the winner of a 2-driver field.
        var r = IRatingProjection.Compute([new IRatingEntry(0, 1, 1), new IRatingEntry(5, 1500, 2), new IRatingEntry(6, 1500, 3)]);
        Assert.False(r.ContainsKey(0));
        Assert.Equal(50.0, r[5], 6);
        Assert.Equal(-49.0, r[6], 6);
    }

    [Fact]
    public void A_single_rated_driver_gets_no_projection()
        => Assert.Empty(IRatingProjection.Compute([new IRatingEntry(1, 1500, 1), new IRatingEntry(2, 0, 2)]));

    [Theory]
    [InlineData(SessionKind.Practice, true, true, false)]
    [InlineData(null, true, true, false)]
    [InlineData(SessionKind.Qualify, false, true, false)]
    [InlineData(SessionKind.Qualify, true, false, false)]
    [InlineData(SessionKind.Qualify, true, true, true)]
    [InlineData(SessionKind.Race, false, false, true)]
    public void Session_gate(SessionKind? kind, bool playerLap, bool carLap, bool expected)
        => Assert.Equal(expected, IRatingProjection.ShowFor(kind, playerLap, carLap));
}

public class PitStopTrackerTests
{
    [Theory]
    [InlineData(58.83, "58.8")]
    [InlineData(55.9, "55.9")]
    [InlineData(86.4, "1:26")]
    [InlineData(125, "2:05")]
    public void Finished_stop_duration_is_formatted_like_kapps(double seconds, string text) =>
        Assert.Equal(text, PitStopTracker.StopDuration(seconds));

    [Fact]
    public void The_players_tow_shows_tow_and_minutes_like_kapps()
    {
        var t = new PitStopTracker();
        t.Update(0, false, 1, true, T0);
        Assert.Equal("TOW 0", t.Update(0, false, 1, true, T0.AddSeconds(0.5), towed: true));   // tow starts on track
        Assert.Equal("TOW 28m", t.Update(0, true, 1, true, T0.AddMinutes(28.2)));             // still in the stall
        Assert.Equal("L1 29:00", t.Update(0, false, 1, true, T0.AddMinutes(29).AddSeconds(0.5)));
    }

    private static readonly DateTime T0 = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Sitting_in_the_stall_before_the_start_is_not_a_stop()
    {
        var t = new PitStopTracker();
        Assert.Equal("", t.Update(3, onPitRoad: true, lap: 0, stopsCount: false, T0));                // gridding in the pits
        Assert.Equal("", t.Update(3, onPitRoad: true, lap: 0, stopsCount: true, T0.AddSeconds(5)));   // green while still there
        Assert.Equal("", t.Update(3, onPitRoad: false, lap: 0, stopsCount: true, T0.AddSeconds(20))); // leaves the pit lane
        Assert.Equal("", t.Update(3, onPitRoad: false, lap: 1, stopsCount: true, T0.AddSeconds(90)));
    }

    [Fact]
    public void Car_already_on_pit_road_when_first_seen_is_not_a_stop()
    {
        var t = new PitStopTracker();
        Assert.Equal("", t.Update(3, true, 5, true, T0));
        Assert.Equal("", t.Update(3, false, 5, true, T0.AddSeconds(30)));
    }

    [Fact]
    public void A_real_entry_during_green_shows_the_timer_then_the_last_stop()
    {
        var t = new PitStopTracker();
        t.Update(3, false, 7, true, T0);
        Assert.Equal("PIT 0", t.Update(3, true, 7, true, T0.AddSeconds(0.2)));
        Assert.Equal("PIT 12", t.Update(3, true, 7, true, T0.AddSeconds(12.2)));
        Assert.Equal("L8 24.0", t.Update(3, false, 8, true, T0.AddSeconds(24.2)));
        Assert.Equal("L8 24.0", t.Update(3, false, 12, true, T0.AddSeconds(400)));
    }

    [Fact]
    public void Entries_outside_a_green_race_do_not_count_and_reset_forgets_everything()
    {
        var t = new PitStopTracker();
        t.Update(3, false, 7, stopsCount: false, T0);
        Assert.Equal("", t.Update(3, true, 7, stopsCount: false, T0.AddSeconds(1))); // cool-down lap / practice
        t.Update(4, false, 7, true, T0);
        t.Update(4, true, 7, true, T0.AddSeconds(1));
        t.Update(4, false, 7, true, T0.AddSeconds(20));
        t.Reset();
        Assert.Equal("", t.Update(4, false, 7, true, T0.AddSeconds(30)));
    }
}

public class SessionLatchTests
{
    [Fact]
    public void Captures_the_first_value_of_a_session_and_keeps_it()
    {
        var l = new SessionLatch<string>();
        var k = new SessionKey(100, 0);
        Assert.Null(l.Get(k, null));
        Assert.Null(l.Get(k, " "));
        Assert.Equal("moderate usage", l.Get(k, "moderate usage"));
        Assert.Equal("moderate usage", l.Get(k, "high usage"));
    }

    [Fact]
    public void A_new_session_captures_again_without_carry_over()
    {
        var l = new SessionLatch<string>();
        l.Get(new SessionKey(100, 0), "low usage");
        Assert.Equal("carry over", l.Get(new SessionKey(100, 1), "carry over"));   // practice -> qualifying
        Assert.Null(l.Get(new SessionKey(200, 0), null));                           // new event: nothing kept
        Assert.Equal("clean", l.Get(new SessionKey(200, 0), "clean"));
    }
}

public class StartingGridTests
{
    [Fact]
    public void Zero_based_session_info_positions_become_one_based()
    {
        var g = StartingGrid.Normalize([(12, 0), (4, 1), (7, 2)]);
        Assert.Equal(1, g[12]);
        Assert.Equal(2, g[4]);
        Assert.Equal(3, g[7]);
    }

    [Fact]
    public void One_based_input_is_kept_and_bad_entries_are_ignored()
    {
        var g = StartingGrid.Normalize([(12, 1), (4, 2), (4, 9), (-1, 3), (7, -1)]);
        Assert.Equal(2, g.Count);
        Assert.Equal(2, g[4]);
    }

    [Fact]
    public void Class_grid_ranks_inside_each_class()
    {
        var overall = new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3, [4] = 4 };
        var cls = new Dictionary<int, int> { [1] = 9, [2] = 5, [3] = 9, [4] = 5 };
        var g = StartingGrid.ByClass(overall, i => cls[i]);
        Assert.Equal(1, g[1]);
        Assert.Equal(2, g[3]);
        Assert.Equal(1, g[2]);
        Assert.Equal(2, g[4]);
    }

    [Fact]
    public void Kapps_interlagos_print_pole_running_twenty_sixth_is_down_twenty_five()
    {
        Assert.Equal(-25, StartingGrid.Change(1, 26));
        Assert.Equal(3, StartingGrid.Change(8, 5));
        Assert.Equal(0, StartingGrid.Change(4, 4));
        Assert.Null(StartingGrid.Change(null, 4));
        Assert.Null(StartingGrid.Change(3, 0));
    }
}

public class TimedSessionOrderTests
{
    [Fact]
    public void Timed_cars_follow_iracing_best_lap_order_untimed_cars_in_the_world_follow()
    {
        var (overall, byClass) = TimedSessionOrder.Compute(
        [
            new TimedCar(CarIdx: 9, ClassId: 1, Position: 0, ClassPosition: 0, InWorld: true),   // no lap yet
            new TimedCar(CarIdx: 3, ClassId: 1, Position: 2, ClassPosition: 2, InWorld: true),
            new TimedCar(CarIdx: 5, ClassId: 1, Position: 1, ClassPosition: 1, InWorld: false), // left, time kept
            new TimedCar(CarIdx: 7, ClassId: 1, Position: 0, ClassPosition: 0, InWorld: false), // gone, no time
        ]);
        Assert.Equal(1, overall[5]);
        Assert.Equal(2, overall[3]);
        Assert.Equal(3, overall[9]);
        Assert.False(overall.ContainsKey(7));
        Assert.Equal(3, byClass[9]);
    }

    [Fact]
    public void Class_positions_are_per_class()
    {
        var (_, byClass) = TimedSessionOrder.Compute(
        [
            new TimedCar(1, 2, 1, 1, true),
            new TimedCar(2, 1, 2, 1, true),
            new TimedCar(3, 2, 3, 2, true),
        ]);
        Assert.Equal(1, byClass[1]);
        Assert.Equal(1, byClass[2]);
        Assert.Equal(2, byClass[3]);
    }
}

public class RadarSideOffsetsTests
{
    [Fact]
    public void Nearest_car_on_each_reported_side()
    {
        var blips = new List<RadarBlip>
        {
            new(-3.2, "A", RadarSide.Left), new(1.1, "B", RadarSide.Left),
            new(4.0, "C", RadarSide.Right), new(-30, "D", RadarSide.Center),
        };
        var (l, r) = RadarSideOffsets.Nearest(blips, true, true);
        Assert.Equal(1.1, l);
        Assert.Equal(4.0, r);
    }

    [Fact]
    public void Side_not_reported_is_null_even_with_a_remembered_car_there()
    {
        var blips = new List<RadarBlip> { new(9.0, "A", RadarSide.Left) };
        Assert.Equal((null, null), RadarSideOffsets.Nearest(blips, false, false));
        Assert.Equal((null, null), RadarSideOffsets.Nearest([], true, true)); // reported, unresolved -> full bar
    }
}

public class RadarSideFillTests
{
    [Fact]
    public void Kapps_print_car_one_point_eight_metres_behind_fills_the_lower_part()
    {
        var (from, to) = RadarSideOffsets.Fill(-1.8);
        Assert.Equal(0, from, 6);
        Assert.Equal(0.625, to, 6);
    }

    [Fact]
    public void Level_car_fills_everything_car_ahead_fills_the_top()
    {
        Assert.Equal((0.0, 1.0), RadarSideOffsets.Fill(0));
        var (from, to) = RadarSideOffsets.Fill(2.4);
        Assert.Equal(0.5, from, 6);
        Assert.Equal(1.0, to, 6);
        Assert.Equal((0.0, 1.0), RadarSideOffsets.Fill(null));
    }

    [Fact]
    public void No_overlap_keeps_a_sliver_at_the_nearer_end()
    {
        Assert.Equal((0.92, 1.0), RadarSideOffsets.Fill(6));
        Assert.Equal((0.0, 0.08), RadarSideOffsets.Fill(-6));
    }
}
