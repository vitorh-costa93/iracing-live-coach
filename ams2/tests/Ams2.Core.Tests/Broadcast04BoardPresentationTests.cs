using Ams2.Core.Calc;

namespace Ams2.Core.Tests;

public class Broadcast04BoardPresentationTests
{
    static readonly BoardDriver Player = new(3, 2, "Player", "Player", "PLA", "Team", "", true);
    static readonly BoardDriver Neighbor = new(1, 1, "Neighbor", "Neighbor", "NEI", "Team", "", false);
    static BoardSectorGap Gap => new(2, Player, Neighbor, true, .3, false, "+0.300", 10, 20);
    static BoardState Plate => BoardState.Empty with { Mode = BoardMode.DriverPlate, Plate = Player };
    static BoardLapComparison Laps => new(3, Player, Neighbor, true, []);

    [Fact]
    public void Physical_sector_overrides_laps_and_plate_but_not_tower()
    {
        var state = Plate with { SectorGap98 = Gap, LapComparison = Laps };
        var view = Broadcast04BoardPresentation.Project(state, 12);
        Assert.Equal(BoardMode.SectorGap, view.Mode);
        Assert.Same(Gap.Player, view.SectorGap!.Player);
        Assert.Equal(8, view.RemainingSeconds);
        var tower = new BoardTower(4, 1, 2, 8, [], 8, 16, true, 10, 20);
        var towerState = state with { Mode = BoardMode.LineTower, Tower = tower };
        Assert.Same(towerState, Broadcast04BoardPresentation.Project(towerState, 12));
    }

    [Fact]
    public void Missing_or_expired_physical_sector_never_uses_legacy_estimate()
    {
        var state = Plate with { Mode = BoardMode.SectorGap, SectorGap = Gap, LapComparison = Laps };
        var view = Broadcast04BoardPresentation.Project(state, 12);
        Assert.Equal(BoardMode.LapComparison, view.Mode);
        Assert.Null(view.SectorGap);
        Assert.Equal(BoardMode.DriverPlate, Broadcast04BoardPresentation.Project(state with { LapComparison = null }, 12).Mode);
        Assert.Equal(BoardMode.None, Broadcast04BoardPresentation.Project(state with { LapComparison = null, Plate = null }, 12).Mode);
        Assert.Equal(BoardMode.DriverPlate, Broadcast04BoardPresentation.Project(Plate with { SectorGap98 = Gap }, 20).Mode);
        Assert.Equal(BoardMode.DriverPlate, Broadcast04BoardPresentation.Project(Plate with { SectorGap98 = Gap }, 9).Mode);
    }

    [Fact]
    public void Sector_key_includes_pair_mark_and_exact_window_but_not_counter_split()
    {
        var live = Broadcast04BoardPresentation.Project(Plate with { SectorGap98 = Gap }, 12);
        var key = Broadcast04BoardPresentation.Key(live);
        Assert.Equal(key, Broadcast04BoardPresentation.Key(live with { SectorGap = Gap with { IsSplit = true, GapSeconds = .6 } }));
        Assert.NotEqual(key, Broadcast04BoardPresentation.Key(live with { SectorGap = Gap with { Sector = 1 } }));
        Assert.NotEqual(key, Broadcast04BoardPresentation.Key(live with { SectorGap = Gap with { Neighbor = Neighbor with { CarIndex = 7 } } }));
        Assert.NotEqual(key, Broadcast04BoardPresentation.Key(live with { SectorGap = Gap with { OpenT = 10.001 } }));
    }

    [Fact]
    public void Transition_removes_old_before_revealing_new_with_bounded_envelopes()
    {
        var motion = new Broadcast04BoardMotion();
        Assert.Equal(1, motion.Advance(Plate, 10).Entry);
        var next = Plate with { Plate = Neighbor };
        Assert.Equal(0, motion.Advance(next, 11).Entry);
        var exit = motion.Advance(next, 11.06);
        Assert.InRange(exit.Exit, .49f, .51f);
        Assert.Equal(0, exit.Entry);
        var entry = motion.Advance(next, 11.20);
        Assert.Null(entry.Outgoing);
        Assert.InRange(entry.Entry, .49f, .51f);
        Assert.Equal(1, motion.Advance(next, 20).Entry);
    }

    [Fact]
    public void Counter_to_split_updates_immediately_without_restarting_window()
    {
        var motion = new Broadcast04BoardMotion();
        var stableGap = Gap with { WindowStartedT = Gap.OpenT };
        var live = Broadcast04BoardPresentation.Project(Plate with { SectorGap98 = stableGap }, 12);
        motion.Advance(live, 12);
        var split = live with { SectorGap = stableGap with { IsSplit = true, GapSeconds = .5, OpenT = Gap.OpenT + .04 } };
        var frame = motion.Advance(split, 12.1);
        Assert.Same(split, frame.Current);
        Assert.Equal(1, frame.Entry);
        Assert.Null(frame.Outgoing);
    }

    [Fact]
    public void Rewind_and_reset_discard_previous_snapshot()
    {
        var motion = new Broadcast04BoardMotion();
        motion.Advance(Plate, 10);
        var next = Plate with { Plate = Neighbor };
        motion.Advance(next, 11);
        var rewind = motion.Advance(next, 3);
        Assert.Null(rewind.Outgoing);
        Assert.Equal(1, rewind.Entry);
        motion.Advance(Plate, 4);
        motion.Reset();
        Assert.Null(motion.Advance(next, 5).Outgoing);
    }

    [Fact]
    public void New_tower_row_does_not_restart_whole_board()
    {
        var tower = new BoardTower(4, 1, 2, 8, [], 8, 16, true, 10, 20);
        var state = Plate with { Mode = BoardMode.LineTower, Tower = tower };
        var motion = new Broadcast04BoardMotion();
        motion.Advance(state, 12);
        var changed = state with { Revision = 100, Tower = tower with { CrossedCount = 9 } };
        Assert.Equal(1, motion.Advance(changed, 12.1).Entry);
    }

    [Fact]
    public void Hidden_board_does_not_restore_stale_plate_on_reentry()
    {
        var motion = new Broadcast04BoardMotion();
        motion.Advance(Plate, 10);
        motion.Advance(BoardState.Empty, 11);
        motion.Advance(BoardState.Empty, 12);
        var frame = motion.Advance(Plate with { Plate = Neighbor }, 13);
        Assert.Null(frame.Outgoing);
        Assert.Equal(0, frame.Entry);
    }

    [Fact]
    public void Interrupted_entry_preserves_opacity_instead_of_flashing_full_plate()
    {
        var motion = new Broadcast04BoardMotion();
        motion.Advance(Plate, 10);
        var second = Plate with { Plate = Neighbor };
        motion.Advance(second, 11);
        var entering = motion.Advance(second, 11.20);
        var third = Plate with { Plate = Player with { CarIndex = 8 } };
        var interrupted = motion.Advance(third, 11.21);
        Assert.Equal(entering.Entry, interrupted.Exit);
        Assert.Equal(0, interrupted.Entry);
        Assert.Same(second, interrupted.Outgoing);
    }
}
