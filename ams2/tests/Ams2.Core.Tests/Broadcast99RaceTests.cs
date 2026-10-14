using Ams2.Core.Calc;

namespace Ams2.Core.Tests;

public class Broadcast99RaceTests
{
    [Fact]
    public void Practice_keeps_caption_without_race_events()
    {
        var p = new Broadcast99RacePresentation(BoardOptions.Default);
        var s = Session(2) with { Kind = SessionKind.Practice };
        p.Update(0, s, new(), null, true);
        p.Update(.1, s with { Cars = s.Cars.Select(c => c with { CurrentLap = 3 }).ToArray() }, new(), null, true);
        Assert.Equal(Broadcast99RaceMode.Caption, p.State!.Mode);
        Assert.Empty(p.State.Rows);
        Assert.Null(p.State.Gap);
    }
    static SessionSnapshot Session(int lap, PitState pit = PitState.None, double speed = 50)
    {
        var s = new Sim(3000, (0, speed), (50, 50)) { PlayerIndex = 0 }.Snapshot();
        return s with { Cars = s.Cars.Select(c => c with { CurrentLap = lap, LapsCompleted = lap - 1, PitState = c.IsPlayer ? pit : PitState.None }).ToArray() };
    }
    static BoardSectorGap Gap(SessionSnapshot s, double now, double previous = 1.2)
        => new(3, BoardText.Driver(s.Cars[0], s.Cars), BoardText.Driver(s.Cars[1], s.Cars), true, .8, true, ".8", now, now + 2)
        { PreviousGapSeconds = previous, PlayerLapsCompleted = s.PlayerCar!.LapsCompleted };
    static void Advance(Broadcast99RacePresentation p, double from, double to, SessionSnapshot s, BoardSectorGap? gap = null)
    {
        for (double t = from + .1; t < to + .00001; t += .1) p.Update(t, s, new GapTracker(), gap, true);
    }

    [Theory]
    [InlineData(3, Broadcast99RaceMode.Gap)]
    [InlineData(6, Broadcast99RaceMode.Gap)]
    [InlineData(9, Broadcast99RaceMode.Gap)]
    [InlineData(5, Broadcast99RaceMode.Remaining)]
    [InlineData(10, Broadcast99RaceMode.Remaining)]
    public void Cadence_uses_current_player_lap_after_transient_table(int lap, Broadcast99RaceMode expected)
    {
        var p = new Broadcast99RacePresentation(BoardOptions.Default);
        p.Update(0, Session(lap - 1), new(), null, true);
        var s = Session(lap); var gap = Gap(s, .1);
        p.Update(.1, s, new(), gap, true);
        Assert.Equal(Broadcast99RaceMode.Table, p.State!.Mode);
        Advance(p, .1, 4.2, s, gap);
        Assert.Equal(expected, p.State!.Mode);
        Advance(p, 4.2, 10.5, s, gap);
        Assert.Equal(Broadcast99RaceMode.Caption, p.State!.Mode);
    }

    [Fact]
    public void Lap15_retains_both_gap_and_remaining_and_measured_previous_gap()
    {
        var p = new Broadcast99RacePresentation(BoardOptions.Default);
        p.Update(0, Session(14), new(), null, true);
        var s = Session(15); var gap = Gap(s, .1, 11.407);
        p.Update(.1, s, new(), gap, true);
        Advance(p, .1, 4.2, s, gap);
        Assert.Equal(Broadcast99RaceMode.Gap, p.State!.Mode);
        Assert.Equal(11.407, p.State.Gap!.PreviousGapSeconds);
        Advance(p, 4.2, 10.5, s, gap);
        Assert.Equal(Broadcast99RaceMode.Remaining, p.State!.Mode);
        Assert.Equal(6, p.State.LapsRemaining);
        Advance(p, 10.5, 17, s, gap);
        Assert.Equal(Broadcast99RaceMode.Caption, p.State!.Mode);
    }

    [Fact]
    public void Missing_previous_sector_or_counter_does_not_invent_comparison_or_block_remaining()
    {
        var p = new Broadcast99RacePresentation(BoardOptions.Default);
        p.Update(0, Session(14), new(), null, true);
        var s = Session(15); var gap = Gap(s, .1) with { IsSplit = false };
        p.Update(.1, s, new(), gap, true);
        Advance(p, .1, 4.2, s, gap);
        Assert.Equal(Broadcast99RaceMode.Remaining, p.State!.Mode);
        Assert.Null(p.State.Gap);
    }

    [Fact]
    public void Pit_has_no_lane_or_exit_hold_and_suspends_table_duration()
    {
        var p = new Broadcast99RacePresentation(BoardOptions.Default);
        p.Update(0, Session(1), new(), null, true);
        p.Update(.1, Session(2), new(), null, true);
        p.Update(.2, Session(2, PitState.InPit, 0), new(), null, true);
        Assert.Equal(Broadcast99RaceMode.Pit, p.State!.Mode);
        Advance(p, .2, 10, Session(2, PitState.InPit, 0));
        p.Update(10.1, Session(2, PitState.DrivingOutOfPits), new(), null, true);
        Assert.Equal(Broadcast99RaceMode.Table, p.State!.Mode);
        Assert.InRange(p.State.ShownSeconds, 0, .2);
        p.Update(10.2, Session(2, PitState.DrivingIntoPits), new(), null, true);
        Assert.Equal(Broadcast99RaceMode.Table, p.State!.Mode);
    }

    [Fact]
    public void Fastest_overrides_table_and_preserves_queued_time()
    {
        var p = new Broadcast99RacePresentation(BoardOptions.Default);
        var seed = Session(1) with { Cars = Session(1).Cars.Select(c => c with { BestLapTime = 100 }).ToArray() };
        p.Update(0, seed, new(), null, true);
        var next = Session(2) with { Cars = Session(2).Cars.Select(c => c with { BestLapTime = c.IsPlayer ? 99 : 100 }).ToArray() };
        p.Update(.1, next, new(), null, true);
        Assert.Equal(Broadcast99RaceMode.Fastest, p.State!.Mode);
        Advance(p, .1, 7.3, next);
        Assert.Equal(Broadcast99RaceMode.Table, p.State!.Mode);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(1)]
    public void Pause_replay_menu_seed_resume_without_replaying_crossing(uint gameState)
    {
        var p = new Broadcast99RacePresentation(BoardOptions.Default);
        p.Update(0, Session(2), new(), null, true);
        p.Update(.1, Session(3) with { GameState = gameState }, new(), null, true);
        Assert.Null(p.State);
        p.Update(.2, Session(3), new(), null, true);
        Assert.Equal(Broadcast99RaceMode.Caption, p.State!.Mode);
    }

    [Fact]
    public void Old_line_split_cannot_be_reused_on_next_scheduled_lap()
    {
        var p = new Broadcast99RacePresentation(BoardOptions.Default);
        p.Update(0, Session(2), new(), null, true);
        var stale = Gap(Session(2), 0);
        p.Update(.1, Session(3), new(), stale, true);
        Advance(p, .1, 4.2, Session(3), stale);
        Assert.Equal(Broadcast99RaceMode.Caption, p.State!.Mode);
    }

    [Fact]
    public void Queued_remaining_survives_next_lap_during_long_interruption()
    {
        var p = new Broadcast99RacePresentation(BoardOptions.Default);
        p.Update(0, Session(14), new(), null, true);
        p.Update(.1, Session(15, PitState.InPit, 0), new(), null, true);
        Advance(p, .1, 5, Session(15, PitState.InPit, 0));
        p.Update(5.1, Session(16), new(), null, true);
        Advance(p, 5.1, 9.3, Session(16));
        Assert.Equal(Broadcast99RaceMode.Remaining, p.State!.Mode);
    }

    [Fact]
    public void Identity_change_invalidates_queued_measured_gap_without_substitution()
    {
        var p = new Broadcast99RacePresentation(BoardOptions.Default);
        p.Update(0, Session(2), new(), null, true);
        var s = Session(3); var gap = Gap(s, .1);
        p.Update(.1, s, new(), gap, true);
        var replacement = gap with { Neighbor = gap.Neighbor with { Name = "Replacement" } };
        Advance(p, .1, 4.2, s, replacement);
        Assert.Equal(Broadcast99RaceMode.Caption, p.State!.Mode);
    }

    [Fact]
    public void Attach_reset_time_gap_and_missing_player_never_replay_old_events()
    {
        var p = new Broadcast99RacePresentation(BoardOptions.Default);
        p.Update(0, Session(15), new(), null, true);
        Assert.Equal(Broadcast99RaceMode.Caption, p.State!.Mode);
        p.Update(10, Session(16), new(), null, true);
        Assert.Equal(Broadcast99RaceMode.Caption, p.State!.Mode);
        p.Update(10.1, Session(16) with { Player = null }, new(), null, true);
        Assert.Null(p.State);
        p.Update(10.2, Session(15), new(), null, true);
        Assert.Equal(Broadcast99RaceMode.Caption, p.State!.Mode);
    }
}
