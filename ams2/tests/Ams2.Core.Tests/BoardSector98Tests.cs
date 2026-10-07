using Ams2.Core.Calc;

namespace Ams2.Core.Tests;

public class BoardSector98Tests
{
    static BoardRig Rig(params (double Distance, double Speed)[] cars) =>
        new(new Sim(3000, cars) { PlayerIndex = 0, AutoPositions = true }) { Dt = .05 };

    [Fact]
    public void Real_second_crossing_keeps_2004_animation_identity_when_mark_is_refined()
    {
        var rig = Rig((900, 100), (850, 100), (1200, 100));
        var live = rig.RunTo(11.1);
        Assert.False(live.SectorGap98!.IsSplit);
        Assert.NotNull(live.SectorGap98.WindowStartedT);
        var motion = new Broadcast04BoardMotion();
        motion.Advance(Broadcast04BoardPresentation.Project(live with { Mode = BoardMode.SectorGap, Tower = null }, live.Now), live.Now);
        var frozen = rig.RunTo(11.7);
        Assert.True(frozen.SectorGap98!.IsSplit);
        Assert.Equal(live.SectorGap98.WindowStartedT, frozen.SectorGap98.WindowStartedT);
        var frame = motion.Advance(Broadcast04BoardPresentation.Project(frozen with { Mode = BoardMode.SectorGap, Tower = null }, frozen.Now), frozen.Now);
        Assert.Equal(1, frame.Entry);
        Assert.Null(frame.Outgoing);
    }

    [Fact]
    public void Bootstrap_waits_for_next_mark_and_third_car_cannot_open_window()
    {
        var rig = Rig((900, 100), (950, 100), (1200, 100));
        Assert.Null(rig.RunTo(1.1).SectorGap98); // player S1 only chooses the pair
        Assert.Null(rig.RunTo(8.1).SectorGap98); // leader S2 is irrelevant
        Assert.Null(rig.RunTo(10.4).SectorGap98);
        var gap = rig.RunTo(10.7).SectorGap98!;
        Assert.Equal(2, gap.Sector);
        Assert.Equal(1, gap.Neighbor.CarIndex);
        Assert.True(gap.NeighborAhead);
        Assert.False(gap.IsSplit);
        Assert.Equal(10.5, gap.OpenT, 2);
        Assert.Equal(.2, gap.GapSeconds, 2);
        gap = rig.RunTo(11.1).SectorGap98!;
        Assert.True(gap.IsSplit);
        Assert.Equal(.5, gap.GapSeconds, 3);
        Assert.Equal(13, gap.CloseT, 2);
        Assert.Equal(gap.GapSeconds, rig.RunTo(12.5).SectorGap98!.GapSeconds);
        Assert.Null(rig.RunTo(13.1).SectorGap98);
    }

    [Fact]
    public void Rear_pair_starts_at_player_and_next_selection_does_not_replace_it()
    {
        var rig = Rig((900, 100), (850, 100), (1200, 100), (600, 100));
        rig.RunTo(1.1);
        rig.Sim.Speeds[3] = 127; // becomes closer by S2, but cannot replace the open S2 window
        var gap = rig.RunTo(11.1).SectorGap98!;
        Assert.Equal(1, gap.Neighbor.CarIndex);
        Assert.False(gap.NeighborAhead);
        Assert.False(gap.IsSplit);
        Assert.InRange(gap.OpenT, 10.97, 11.03); // learned mark is still bounded by the first crossing sample
        Assert.Equal(-(11.1 - gap.OpenT), gap.GapSeconds, 6);
        gap = rig.RunTo(11.7).SectorGap98!;
        Assert.True(gap.IsSplit);
        Assert.Equal(-.5, gap.GapSeconds, 3);
        Assert.Equal(13.5, gap.CloseT, 2);
    }

    [Fact]
    public void Selection_uses_physical_distance_instead_of_time_or_official_position()
    {
        var rig = Rig((900, 100), (950, 100), (940, 20));
        rig.RunTo(1.05); // at S1: ahead is 50m / .5s, rear is 40m / 2s
        rig.Sim.Speeds[2] = 100;
        var gap = rig.RunTo(11.1).SectorGap98!;
        Assert.Equal(2, gap.Neighbor.CarIndex);
        Assert.False(gap.NeighborAhead);
    }

    [Fact]
    public void Pair_is_locked_at_previous_player_mark_even_if_another_car_becomes_closer()
    {
        var rig = Rig((900, 100), (950, 100), (600, 100));
        rig.RunTo(1.1);
        rig.Sim.Speeds[2] = 128;
        var gap = rig.RunTo(10.7).SectorGap98!;
        Assert.Equal(1, gap.Neighbor.CarIndex);
        Assert.Equal(1, rig.RunTo(11.1).SectorGap98!.Neighbor.CarIndex);
    }

    [Fact]
    public void Lapped_but_physically_nearest_car_is_selected_and_cycle_wraps_to_sector_one()
    {
        var rig = Rig((3900, 100), (950, 100), (4100, 100));
        rig.RunTo(11.7);
        var line = rig.RunTo(20.7).SectorGap98!;
        Assert.Equal(3, line.Sector);
        Assert.Equal(1, line.Neighbor.CarIndex);
        Assert.Equal(.5, rig.RunTo(21.1).SectorGap98!.GapSeconds, 3);
        var first = rig.RunTo(30.7).SectorGap98!;
        Assert.Equal(1, first.Sector);
        Assert.Equal(1, first.Neighbor.CarIndex);
    }

    [Theory]
    [InlineData(PitState.InPit)]
    [InlineData(PitState.InGarage)]
    [InlineData(PitState.DrivingOutOfGarage)]
    public void Ineligible_neighbor_invalidates_reference_without_substitution(PitState pit)
    {
        var rig = Rig((900, 100), (950, 100), (1000, 100));
        rig.RunTo(1.1);
        rig.Sim.Pit[1] = pit;
        Assert.Null(rig.RunTo(11.1).SectorGap98);
    }

    [Fact]
    public void Reused_car_slot_and_player_switch_require_new_bootstrap()
    {
        var rig = Rig((900, 100), (950, 100), (1100, 100));
        rig.RunTo(1.1);
        rig.Sim.Names = ["C0", "Replacement", "C2"];
        Assert.Null(rig.RunTo(11.1).SectorGap98);
        rig.Sim.PlayerIndex = 2;
        Assert.Null(rig.RunTo(20.7).SectorGap98);
    }

    [Fact]
    public void Overtaking_before_target_mark_discards_old_reference()
    {
        var rig = Rig((900, 100), (950, 100));
        rig.RunTo(1.1);
        rig.Sim.Speeds[1] = 90;
        Assert.Null(rig.RunTo(11.1).SectorGap98);
        Assert.Null(rig.RunTo(12.0).SectorGap98);
    }

    [Fact]
    public void Disconnect_and_session_change_clear_pending_pair()
    {
        var rig = Rig((900, 100), (950, 100));
        rig.RunTo(1.1);
        rig.Board.Update(rig.Sim.Now, rig.Sim.Snapshot() with { InSession = false }, rig.Gaps);
        Assert.Null(rig.RunTo(11.1).SectorGap98);
        rig.Sim.Track = "Other";
        Assert.Null(rig.RunTo(21.1).SectorGap98);
    }

    [Fact]
    public void Legacy_contract_still_opens_on_leader_before_pair_crosses()
    {
        var rig = Rig((900, 100), (950, 100), (1200, 100));
        rig.RunTo(1.1);
        var state = rig.RunTo(8.1);
        Assert.Equal(BoardMode.SectorGap, state.Mode);
        Assert.NotNull(state.SectorGap);
        Assert.False(state.SectorGap!.IsSplit);
        Assert.Null(state.SectorGap98);
    }

    [Fact]
    public void Open_window_is_removed_if_neighbor_disconnects_or_enters_pit()
    {
        var rig = Rig((900, 100), (950, 100));
        Assert.NotNull(rig.RunTo(10.7).SectorGap98);
        var snapshot = rig.Sim.Snapshot();
        rig.Sim.Step(.1);
        var state = rig.Board.Update(rig.Sim.Now, snapshot with { Cars = snapshot.Cars.Where(c => c.Index != 1).ToArray() }, rig.Gaps);
        Assert.Null(state.SectorGap98);
    }

    [Fact]
    public void Stopped_second_car_cannot_extend_window_past_safety_limit()
    {
        var rig = Rig((900, 100), (950, 100));
        var gap = rig.RunTo(10.7).SectorGap98!;
        rig.Sim.Speeds[0] = 0;
        rig.Sim.Speeds[1] = 0;
        Assert.False(rig.RunTo(50.4).SectorGap98!.IsSplit);
        Assert.Null(rig.RunTo(gap.OpenT + 40.1).SectorGap98);
    }

    [Fact]
    public void Newer_sector_starts_even_when_previous_window_is_waiting_for_rear_car()
    {
        var rig = Rig((3900, 100), (2700, 100));
        Assert.Null(rig.RunTo(1.1).SectorGap98);
        var second = rig.RunTo(11.1).SectorGap98!;
        Assert.Equal(2, second.Sector);
        Assert.False(second.IsSplit);

        var line = rig.RunTo(21.1).SectorGap98!;
        Assert.Equal(3, line.Sector);
        Assert.InRange(line.OpenT, 20.97, 21.03);
        Assert.False(line.IsSplit);
        Assert.Equal(1, line.Neighbor.CarIndex);

        // The rear car reaching the older S2 mark cannot replace or complete the newer line window.
        line = rig.RunTo(23.1).SectorGap98!;
        Assert.Equal(3, line.Sector);
        Assert.False(line.IsSplit);
        Assert.Equal(-(rig.Sim.Now - line.OpenT), line.GapSeconds, 6);
    }

    [Fact]
    public void Repeated_timestamp_cannot_create_fake_crossing()
    {
        var rig = Rig((900, 100), (950, 100));
        rig.RunTo(1.1);
        var sample = rig.Sim.Snapshot();
        var jumped = sample with { Cars = sample.Cars.Select(c => c with { Sector = 2, LapDistance = c.LapDistance + 1000 }).ToArray() };
        Assert.Null(rig.Board.Update(rig.Sim.Now, jumped, rig.Gaps).SectorGap98);
        Assert.Null(rig.Tick().SectorGap98);
    }
}
