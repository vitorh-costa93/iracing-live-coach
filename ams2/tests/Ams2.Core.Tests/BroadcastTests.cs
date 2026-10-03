using Ams2.Core.Calc;

namespace Ams2.Core.Tests;

public class BroadcastTrackerTests
{
    const double Len = 5000;

    [Fact]
    public void Counts_one_stop_per_pit_lane_entry_during_the_race_and_ignores_garage_exits()
    {
        var sim = new Sim(Len, (3000, 50), (2900, 50), (2800, 50)) { PlayerIndex = 0 };
        sim.Pit[2] = PitState.InGarage;
        var t = new BroadcastTracker();
        void Run(double secs) { for (int i = 0; i < secs * 10; i++) { sim.Step(0.1); t.Update(sim.Now, sim.Snapshot()); } }

        Run(1);
        sim.Pit[2] = PitState.DrivingOutOfGarage; Run(1);
        sim.Pit[2] = PitState.None; Run(1);
        Assert.Equal(0, t.State.StopsOf(2));
        Assert.Equal(-1, t.State.LastPitCarIndex);

        sim.Pit[1] = PitState.DrivingIntoPits; Run(1);
        sim.Pit[1] = PitState.InPit; Run(3);
        sim.Pit[1] = PitState.DrivingOutOfPits; Run(2);
        Assert.Equal(1, t.State.StopsOf(1));
        Assert.Equal(1, t.State.LastPitCarIndex);
        double firstEntry = t.State.LastPitEntryT;

        sim.Pit[1] = PitState.None; Run(1);
        sim.Pit[1] = PitState.DrivingIntoPits; Run(1);
        sim.Pit[1] = PitState.None; Run(1);
        Assert.Equal(2, t.State.StopsOf(1));
        Assert.True(t.State.LastPitEntryT > firstEntry);
        Assert.Equal(0, t.State.StopsOf(0));
    }

    [Fact]
    public void Player_stop_timer_measures_time_in_the_box_and_keeps_the_last_value()
    {
        var sim = new Sim(Len, (3000, 50), (2900, 50)) { PlayerIndex = 0 };
        var t = new BroadcastTracker();
        void Run(double secs) { for (int i = 0; i < secs * 10; i++) { sim.Step(0.1); t.Update(sim.Now, sim.Snapshot()); } }
        Run(1);
        sim.Pit[0] = PitState.DrivingIntoPits; Run(2);
        Assert.False(t.State.PlayerStopped);
        sim.Pit[0] = PitState.InPit; Run(3);
        Assert.True(t.State.PlayerStopped);
        Assert.Equal(3.0, t.State.PlayerStopNow(sim.Now), 0.25);
        sim.Pit[0] = PitState.DrivingOutOfPits; Run(2);
        Assert.False(t.State.PlayerStopped);
        Assert.Equal(3.0, t.State.PlayerStopNow(sim.Now), 0.25);
        Assert.True(sim.Now - t.State.PlayerStopEndT < 2.5);
    }

    [Fact]
    public void Detects_player_position_change_and_line_crossing()
    {
        var sim = new Sim(Len, (3000, 50), (4950, 50)) { PlayerIndex = 1 };
        var t = new BroadcastTracker();
        t.Update(0, sim.Snapshot());
        Assert.Equal(double.NegativeInfinity, t.State.PlayerPositionChangedT);
        var snap = sim.Snapshot();
        var swapped = snap with { Cars = snap.Cars.Select(c => c with { Position = 3 - c.Position }).ToList() };
        t.Update(5, swapped);
        Assert.Equal(5, t.State.PlayerPositionChangedT);
        for (int i = 0; i < 20; i++) { sim.Step(0.1); t.Update(5 + sim.Now, sim.Snapshot()); }
        Assert.True(t.State.PlayerLapChangedT > 5);
    }

    [Fact]
    public void Winner_appears_when_the_leader_finishes_with_summed_lap_time()
    {
        var sim = new Sim(Len, (3000, 50), (2900, 50)) { PlayerIndex = 1 };
        sim.LastLap[0] = 100;
        var t = new BroadcastTracker();
        for (int i = 0; i < 1200; i++) { sim.Step(0.1); t.Update(sim.Now, sim.Snapshot()); } // 120 s: 1+ volta completa do lider
        Assert.Null(t.State.Winner);
        sim.Race[0] = RaceState.Finished;
        t.Update(sim.Now, sim.Snapshot());
        var w = t.State.Winner!;
        Assert.Equal(0, w.Car.Index);
        Assert.True(w.TotalSeconds >= 100);
        Assert.Equal(20 * Len / 1000, w.DistanceKm, 3);
        Assert.Equal(w.DistanceKm / (w.TotalSeconds / 3600), w.AvgKmh, 3);
    }
}
