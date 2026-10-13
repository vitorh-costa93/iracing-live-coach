using Ams2.Core;
using Ams2.Core.Calc;

namespace Ams2.Core.Tests;

public class Broadcast93Tests
{
    static BoardGap93? Run(Sim sim, BoardGap93Tracker tracker, double end)
    {
        if (sim.Now == 0) tracker.Update(0, sim.Snapshot());
        while (sim.Now < end - 1e-9) { sim.Step(Math.Min(.05, end - sim.Now)); tracker.Update(sim.Now, sim.Snapshot()); }
        return tracker.State;
    }

    [Fact]
    public void Gap_neighbor_is_the_smallest_gap_in_seconds_not_meters()
    {
        // Car 2 is physically closer (30 m behind) but slow: 30 m / 10 m/s = 3 s. Car 1 is 50 m ahead = 0.5 s at the player's speed.
        var sim = new Sim(3000, (2900, 100), (2950, 100), (2915, 10)) { PlayerIndex = 0 };
        var gap = Run(sim, new BoardGap93Tracker(), 1.1)!;
        Assert.Equal(1, gap.Neighbor.CarIndex);
        Assert.True(gap.NeighborAhead);
        Assert.Equal(.5, gap.GapSeconds, 6);
    }

    [Fact]
    public void Gap_neighbor_behind_wins_when_its_gap_in_seconds_is_smaller()
    {
        // Ahead car 80 m (0.8 s); the car behind is 40 m back at the same speed (0.4 s).
        var sim = new Sim(3000, (2900, 100), (2980, 100), (2860, 100)) { PlayerIndex = 0 };
        var gap = Run(sim, new BoardGap93Tracker(), 1.5)!;
        Assert.Equal(2, gap.Neighbor.CarIndex);
        Assert.False(gap.NeighborAhead);
        Assert.Equal(.4, gap.GapSeconds, 6);
    }

    [Fact]
    public void Gap_shows_only_every_third_completed_lap()
    {
        var sim = new Sim(3000, (2900, 100), (2950, 100)) { PlayerIndex = 0 };
        var tracker = new BoardGap93Tracker();
        tracker.SetOptions(0, 7, true, Broadcast93Rules.GapEveryLaps);
        Assert.Null(Run(sim, tracker, 1.1));    // lap 1
        Assert.Null(Run(sim, tracker, 31.1));   // lap 2
        var third = Run(sim, tracker, 61.1)!;   // lap 3
        Assert.Equal(61, third.CompletedT, 6);
        Assert.Equal(.5, third.GapSeconds, 6);
        Assert.Null(Run(sim, tracker, 91.1));   // lap 4
        Assert.Null(Run(sim, tracker, 121.1));  // lap 5
        Assert.NotNull(Run(sim, tracker, 151.1)); // lap 6
    }

    [Theory]
    [InlineData(0, false)] [InlineData(1, false)] [InlineData(3, true)] [InlineData(4, false)] [InlineData(6, true)]
    public void Gap_lap_rule(int laps, bool due) => Assert.Equal(due, Broadcast93Rules.IsGapLap(laps));

    [Theory]
    [InlineData(10, true)] [InlineData(5, true)] [InlineData(1, false)] [InlineData(0, false)] [InlineData(7, false)] [InlineData(15, true)]
    public void Laps_to_go_every_five_remaining(int remaining, bool shown) => Assert.Equal(shown, Broadcast93Rules.IsLapsToGo(remaining));

    [Fact]
    public void Laps_to_go_is_null_for_timed_races_and_non_races()
    {
        var session = new Sim(3000, (2900, 100)) { PlayerIndex = 0 }.Snapshot();
        Assert.Equal(20, Broadcast93Rules.LapsRemaining(session)); // LapsInEvent 20, 0 laps completed
        Assert.Null(Broadcast93Rules.LapsRemaining(session with { LapsInEvent = 0 }));
        Assert.Null(Broadcast93Rules.LapsRemaining(session with { Kind = SessionKind.Qualify }));
        Assert.False(Broadcast93Rules.IsLapsToGo(null));
    }

    [Fact]
    public void Board_state_carries_laps_remaining_from_player_laps()
    {
        var rig = new BoardRig(new Sim(3000, (2900, 100), (2950, 100)));
        Assert.Equal(20, rig.RunTo(.5).LapsRemaining);
        Assert.Equal(19, rig.RunTo(1.5).LapsRemaining);
    }

    static SessionSnapshot Session(Sim sim, params (int Car, double Best)[] bests)
    {
        var s = sim.Snapshot();
        return s with { Cars = s.Cars.Select(c => bests.Any(b => b.Car == c.Index) ? c with { BestLapTime = bests.First(b => b.Car == c.Index).Best } : c).ToArray() };
    }

    [Fact]
    public void Fastest_detector_seeds_baseline_then_fires_only_on_a_new_session_best()
    {
        var sim = new Sim(3000, (2900, 100), (2950, 100)) { PlayerIndex = 0 };
        var d = new FastestLapDetector();
        Assert.Null(d.Update(1, Session(sim, (1, 80))));            // attach: baseline only
        Assert.Null(d.Update(2, Session(sim, (1, 80))));
        Assert.Null(d.Update(3, Session(sim, (1, 80), (0, 81))));   // slower than the best
        var e = d.Update(4, Session(sim, (1, 80), (0, 79.5)))!;     // player beats it
        Assert.Equal(0, e.Car.Index);
        Assert.Equal(79.5, e.LapTime);
        Assert.Equal(4, e.At);
        Assert.Same(e, d.Latest);
        Assert.Null(d.Update(5, Session(sim, (1, 80), (0, 79.5))));
        Assert.Equal(1, d.Update(6, Session(sim, (1, 79.0), (0, 79.5)))!.Car.Index); // another driver
    }

    [Fact]
    public void Fastest_detector_first_time_after_empty_baseline_and_reset_on_new_session()
    {
        var sim = new Sim(3000, (2900, 100), (2950, 100)) { PlayerIndex = 0 };
        var d = new FastestLapDetector();
        Assert.Null(d.Update(1, Session(sim)));
        Assert.NotNull(d.Update(2, Session(sim, (0, 90))));
        sim.Track = "Other";
        Assert.Null(d.Update(3, Session(sim, (0, 85))));   // new session: baseline again, no stale event
        Assert.Null(d.Latest);
        Assert.Null(d.Update(4, Session(sim, (0, 85)), generation: 1));
        Assert.NotNull(d.Update(5, Session(sim, (0, 84)), generation: 1));
        Assert.Null(d.Update(1, Session(sim, (0, 80)), generation: 1)); // clock went back: reset
    }

    [Fact]
    public void Fastest_event_window_is_seven_seconds_and_can_be_delayed()
    {
        var e = new FastestLapEvent(new Sim(3000, (0, 1)).Snapshot().Cars[0], 80, 100);
        Assert.False(e.ActiveAt(99.9));
        Assert.True(e.ActiveAt(100));
        Assert.True(e.ActiveAt(106.9));
        Assert.False(e.ActiveAt(107));
        Assert.False(e.ActiveAt(103, notBefore: 106));
        Assert.True(e.ActiveAt(110, notBefore: 106));
    }
}
