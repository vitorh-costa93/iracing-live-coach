using Ams2.Core.Calc;

namespace Ams2.Core.Tests;

public class BoardGap93Tests
{
    sealed class Rig(params (double Distance, double Speed)[] cars)
    {
        public Sim Sim { get; } = new(3000, cars) { PlayerIndex = 0 };
        public BoardGap93Tracker Tracker { get; } = new();
        bool _started;
        public BoardGap93? RunTo(double end)
        {
            if (!_started) { Tracker.Update(Sim.Now, Sim.Snapshot()); _started = true; }
            while (Sim.Now < end - 1e-9)
            {
                Sim.Step(Math.Min(.05, end - Sim.Now));
                Tracker.Update(Sim.Now, Sim.Snapshot());
            }
            return Tracker.State;
        }
    }

    [Fact]
    public void Ahead_result_only_appears_after_second_crossing_and_stays_fixed()
    {
        var rig = new Rig((2900, 100), (2950, 100), (2800, 100));
        Assert.Null(rig.RunTo(.7));
        var gap = rig.RunTo(1.1)!;
        Assert.True(gap.NeighborAhead);
        Assert.Equal(1, gap.Neighbor.CarIndex);
        Assert.Equal(.5, gap.FirstCrossedT, 8);
        Assert.Equal(1, gap.CompletedT, 8);
        Assert.Equal(.5, gap.GapSeconds, 8);
        Assert.Equal("0.500", gap.GapText);
        Assert.Equal(gap, rig.RunTo(7.9));
        Assert.Null(rig.RunTo(8.1));
    }

    [Fact]
    public void Rear_result_uses_player_first_and_physical_order_not_ranking()
    {
        // Official P2 is farther ahead; P3 is physically closest behind.
        var rig = new Rig((2900, 100), (3050, 100), (2875, 100));
        Assert.Null(rig.RunTo(1.1));
        var gap = rig.RunTo(1.3)!;
        Assert.False(gap.NeighborAhead);
        Assert.Equal(2, gap.Neighbor.CarIndex);
        Assert.Equal(0, gap.Ahead.CarIndex);
        Assert.Equal(2, gap.Behind.CarIndex);
        Assert.Equal(.25, gap.GapSeconds, 8);
    }

    [Fact]
    public void Lapped_car_is_selected_across_finish_wrap_without_using_race_distance()
    {
        var rig = new Rig((5900, 100), (2950, 100), (5750, 100));
        var gap = rig.RunTo(1.1)!;
        Assert.Equal(1, gap.Neighbor.CarIndex);
        Assert.True(gap.NeighborAhead);
        Assert.Equal(.5, gap.GapSeconds, 8);
    }

    [Fact]
    public void One_configured_point_measures_once_per_lap_without_sector_events()
    {
        var rig = new Rig((700, 100), (740, 100));
        rig.Tracker.SetOptions(25, 3);
        var gap = rig.RunTo(.6)!;
        Assert.Equal(.5, gap.CompletedT, 8);
        Assert.Equal(.4, gap.GapSeconds, 8);
        Assert.Null(rig.RunTo(20)); // S1, S2 and finish never open another gap.
        var next = rig.RunTo(30.6)!;
        Assert.Equal(30.5, next.CompletedT, 8);
    }

    [Theory]
    [InlineData(PitState.DrivingIntoPits)]
    [InlineData(PitState.InPit)]
    [InlineData(PitState.DrivingOutOfPits)]
    [InlineData(PitState.InGarage)]
    [InlineData(PitState.DrivingOutOfGarage)]
    public void Pit_cancels_pending_pair_without_substituting_another_car(PitState pit)
    {
        var rig = new Rig((2900, 100), (2950, 100), (2800, 100));
        rig.RunTo(.6);
        rig.Sim.Pit[1] = pit;
        Assert.Null(rig.RunTo(2.1));
    }

    [Fact]
    public void Slot_identity_replacement_cancels_pending_and_completed_result()
    {
        var rig = new Rig((2900, 100), (2950, 100));
        rig.RunTo(.6);
        rig.Sim.Names = ["C0", "Replacement"];
        Assert.Null(rig.RunTo(1.1));
        var other = new Rig((2900, 100), (2950, 100));
        Assert.NotNull(other.RunTo(1.1));
        other.Sim.CarNames = ["car", "Replacement car"];
        Assert.Null(other.RunTo(1.2));
    }

    [Fact]
    public void Overtake_clears_result_without_reversing_pair()
    {
        var rig = new Rig((2900, 100), (2950, 100));
        rig.RunTo(.6);
        rig.Sim.Speeds[0] = 300;
        rig.Sim.Speeds[1] = 0;
        Assert.Null(rig.RunTo(1.1));
    }

    [Theory]
    [InlineData(1u)]
    [InlineData(4u)]
    [InlineData(5u)]
    [InlineData(6u)]
    public void Menu_pause_and_replay_clear_result_and_pending_measurement(uint gameState)
    {
        var rig = new Rig((2900, 100), (2950, 100));
        rig.RunTo(.6);
        Assert.Null(rig.Tracker.Update(.7, rig.Sim.Snapshot() with { GameState = gameState }));
        Assert.Null(rig.RunTo(1.1));
    }

    [Fact]
    public void Non_driving_disconnect_session_switch_and_clock_reset_clear_comparison()
    {
        foreach (int scenario in Enumerable.Range(0, 4))
        {
            var rig = new Rig((2900, 100), (2950, 100));
            rig.RunTo(.6);
            var s = rig.Sim.Snapshot();
            if (scenario == 0) rig.Tracker.Update(.7, s, playerDriving: false);
            if (scenario == 1) rig.Tracker.Update(.7, s with { InSession = false });
            if (scenario == 2) { rig.Sim.Track = "Other"; rig.RunTo(.7); }
            if (scenario == 3) rig.Tracker.Update(.1, s);
            Assert.Null(rig.RunTo(1.1));
        }
    }

    [Fact]
    public void Data_gap_or_teleport_cannot_create_a_passage_estimate()
    {
        var rig = new Rig((2900, 100), (2950, 100));
        rig.RunTo(.6);
        rig.Sim.Step(3);
        Assert.Null(rig.Tracker.Update(rig.Sim.Now, rig.Sim.Snapshot()));
        var jump = new Rig((2900, 100), (2950, 100));
        jump.RunTo(.6);
        jump.Sim.Speeds[0] = 12000;
        Assert.Null(jump.RunTo(.7));
    }

    [Fact]
    public void Repeated_telemetry_sequence_does_not_move_crossing_sample_timestamp()
    {
        var sim = new Sim(3000, (2900, 100), (2950, 100));
        var tracker = new BoardGap93Tracker();
        var first = sim.Snapshot() with { Sequence = 10 };
        tracker.Update(0, first);
        tracker.Update(.2, first);
        tracker.Update(.4, first);
        sim.Step(1.1);
        var gap = tracker.Update(1.1, sim.Snapshot() with { Sequence = 11 })!;
        Assert.Equal(.5, gap.FirstCrossedT, 8);
        Assert.Equal(1, gap.CompletedT, 8);
        Assert.Equal(.5, gap.GapSeconds, 8);
    }

    [Fact]
    public void Simultaneous_passages_produce_zero_interval_and_three_decimals()
    {
        var gap = new Rig((2900, 100), (2900, 100)).RunTo(1.1)!;
        Assert.Equal("0.000", gap.GapText);
        Assert.Equal(0, gap.GapSeconds, 8);
    }

    [Fact]
    public void Board_only_computes_93_when_enabled_and_does_not_change_legacy_mode()
    {
        var rig = new BoardRig(new Sim(3000, (2900, 100), (2950, 100)));
        Assert.Null(rig.RunTo(1.1).Gap93);
        rig.Board.SetGap93Options(0, 7);
        var state = rig.RunTo(31.1);
        Assert.NotNull(state.Gap93);
        Assert.Equal(BoardMode.LineTower, state.Mode);
        rig.Board.SetGap93Options(0, 7, enabled: false);
        Assert.Null(rig.RunTo(31.2).Gap93);
    }

    [Fact]
    public void Race_track_limit_lap_invalidity_does_not_invalidate_physical_passages()
    {
        var sim = new Sim(3000, (2900, 100), (2950, 100));
        var tracker = new BoardGap93Tracker();
        SessionSnapshot Snapshot() => sim.Snapshot() with
        { Cars = sim.Snapshot().Cars.Select(c => c with { LapInvalid = true }).ToArray() };
        tracker.Update(0, Snapshot());
        sim.Step(1.1);
        var gap = tracker.Update(1.1, Snapshot())!;
        Assert.Equal(.5, gap.GapSeconds, 8);
    }

    [Fact]
    public void Earlier_choice_wins_a_physical_distance_tie_on_next_lap()
    {
        var rig = new Rig((2900, 100), (2875, 100), (3050, 100));
        Assert.Equal(1, rig.RunTo(1.3)!.Neighbor.CarIndex);
        rig.Sim.Speeds[2] = 100 - 125 / (31 - 1.3);
        var next = rig.RunTo(31.3)!;
        Assert.Equal(1, next.Neighbor.CarIndex);
        Assert.False(next.NeighborAhead);
        Assert.Equal(.25, next.GapSeconds, 8);
    }

    [Fact]
    public void A_closer_new_car_does_not_replace_pending_identity()
    {
        var rig = new Rig((2900, 100), (2950, 100), (2800, 100));
        rig.RunTo(.6);
        rig.Sim.Speeds[2] = 500;
        var gap = rig.RunTo(1.1)!;
        Assert.Equal(1, gap.Neighbor.CarIndex);
        Assert.Equal(.5, gap.GapSeconds, 8);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Session_restart_without_lap_change_discards_pending_measurement(bool raceStateReset)
    {
        var sim = new Sim(3000, (2900, 100), (2950, 100));
        var tracker = new BoardGap93Tracker();
        tracker.Update(0, sim.Snapshot() with { TimeRemainingSeconds = 100 });
        sim.Step(.6);
        tracker.Update(.6, sim.Snapshot() with { TimeRemainingSeconds = 99.4 });
        sim.Step(.1);
        var restart = sim.Snapshot() with { TimeRemainingSeconds = raceStateReset ? 99.3 : 200 };
        if (raceStateReset) restart = restart with { Cars = restart.Cars.Select(c => c with { RaceState = RaceState.NotStarted }).ToArray() };
        Assert.Null(tracker.Update(.7, restart));
        sim.Step(.4);
        Assert.Null(tracker.Update(1.1, sim.Snapshot() with { TimeRemainingSeconds = raceStateReset ? 98.9 : 199.6 }));
    }

    [Fact]
    public void Changing_marker_discards_pending_old_point_and_waits_for_new_passage()
    {
        var rig = new Rig((2900, 100), (2950, 100));
        rig.RunTo(.6);
        rig.Tracker.SetOptions(25, 7);
        Assert.Null(rig.RunTo(1.1));
        var gap = rig.RunTo(8.6)!;
        Assert.Equal(8.5, gap.CompletedT, 8);
        Assert.Equal(.5, gap.GapSeconds, 8);
    }

    [Fact]
    public void Recovery_from_invalid_distance_requires_two_valid_samples()
    {
        var sim = new Sim(3000, (2990, 100), (2980, 100));
        var tracker = new BoardGap93Tracker();
        var invalid = sim.Snapshot();
        invalid = invalid with { Cars = invalid.Cars.Select(c => c with { LapDistance = -1 }).ToArray() };
        tracker.Update(0, invalid);
        sim.Step(.3);
        Assert.Null(tracker.Update(.3, sim.Snapshot()));
        sim.Step(.1);
        Assert.Null(tracker.Update(.4, sim.Snapshot()));
    }
}
