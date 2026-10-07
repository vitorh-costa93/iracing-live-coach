using Ams2.Core.Calc;

namespace Ams2.Core.Tests;

public class Broadcast18BoardPresentationTests
{
    static readonly BoardDriver Player = new(1, 2, "Player", "Player", "PLA", "Team", "", true);
    static readonly BoardDriver Neighbor = new(2, 1, "Neighbor", "Neighbor", "NEI", "Team", "", false);
    static BoardState Frame(double value, bool split = false) => BoardState.Empty with
    {
        Mode = BoardMode.SectorGap,
        SectorGap = new(2, Player, Neighbor, true, value, split, BoardText.Gap(value), 10, 20)
            { WindowStartedT = 10 }
    };

    [Fact]
    public void Live_value_is_held_but_exact_split_is_immediate()
    {
        var view = new Broadcast18BoardPresentation();
        Assert.Equal(.4, view.Project(Frame(.4), 10, "session").SectorGap!.GapSeconds);
        Assert.Equal(.4, view.Project(Frame(.5), 10.99, "session").SectorGap!.GapSeconds);
        Assert.Equal(.6, view.Project(Frame(.6), 11, "session").SectorGap!.GapSeconds);
        var final = Frame(.65, true);
        Assert.Same(final, view.Project(final, 11.01, "session"));
    }

    [Fact]
    public void New_pair_window_or_session_does_not_reuse_previous_value()
    {
        var view = new Broadcast18BoardPresentation();
        view.Project(Frame(.4), 10, "session");
        var other = Frame(.7) with { SectorGap = Frame(.7).SectorGap! with { Neighbor = Neighbor with { CarIndex = 7 } } };
        Assert.Equal(.7, view.Project(other, 10.1, "session").SectorGap!.GapSeconds);
        Assert.Equal(.9, view.Project(Frame(.9), 10.2, "session2").SectorGap!.GapSeconds);
        var window = Frame(1.1) with { SectorGap = Frame(1.1).SectorGap! with { WindowStartedT = 10.3 } };
        Assert.Equal(1.1, view.Project(window, 10.3, "session2").SectorGap!.GapSeconds);
    }

    [Fact]
    public void Refined_opening_keeps_hold_and_clock_rollback_resets_it()
    {
        var view = new Broadcast18BoardPresentation();
        view.Project(Frame(.4), 10, "session");
        var refined = Frame(.5) with { SectorGap = Frame(.5).SectorGap! with { OpenT = 10.04 } };
        Assert.Equal(.4, view.Project(refined, 10.2, "session").SectorGap!.GapSeconds);
        Assert.Equal(.8, view.Project(Frame(.8), 9.9, "session").SectorGap!.GapSeconds);
    }

    [Fact]
    public void Rear_neighbor_retains_sign_and_line_tower_results_are_never_delayed()
    {
        var view = new Broadcast18BoardPresentation();
        var rear = Frame(-.4) with { SectorGap = Frame(-.4).SectorGap! with { NeighborAhead = false } };
        Assert.Equal(-.4, view.Project(rear, 10, "session").SectorGap!.GapSeconds);
        var changed = rear with { SectorGap = rear.SectorGap! with { GapSeconds = -.8 } };
        Assert.Equal(-.4, view.Project(changed, 10.1, "session").SectorGap!.GapSeconds);
        var tower = BoardState.Empty with { Mode = BoardMode.LineTower };
        Assert.Same(tower, view.Project(tower, 10.2, "session"));
    }
}
