using IracingLiveCoach.Core.Telemetry;

namespace IracingLiveCoach.Core.Tests;

public sealed class LapHistoryTests
{
    private static void Complete(LapHistory h, int car, int lap, double time, bool pit = false, bool race = false)
        => h.Update(car, lap, time, pit, race);

    [Fact]
    public void Records_each_completed_lap_after_the_first_sighting()
    {
        var h = new LapHistory();
        h.Update(1, 3, 90.0, false, false);   // first sighting: nothing recorded
        Complete(h, 1, 4, 91.0);
        Complete(h, 1, 5, 92.0);
        Assert.Equal(new[] { 91.0, 92.0 }, h.Laps(1));
    }

    [Fact]
    public void Skips_pit_laps_and_missed_laps()
    {
        var h = new LapHistory();
        h.Update(1, 0, 0, false, false);
        Complete(h, 1, 1, 90);
        h.Update(1, 1, 90, true, false);      // enters pits during lap 2
        Complete(h, 1, 2, 130);               // in-lap: dropped
        Complete(h, 1, 3, 100);               // out-lap (flag carried from pit road at lap boundary? car left pits after the line)
        Complete(h, 1, 6, 91);                // counter jumped by 3: dropped
        Complete(h, 1, 7, 91.5);
        Assert.Equal(new[] { 90.0, 100.0, 91.5 }, h.Laps(1));
    }

    [Fact]
    public void Race_ignores_the_standing_start_lap()
    {
        var h = new LapHistory();
        h.Update(1, 0, 0, false, true);
        Complete(h, 1, 1, 110, race: true);
        Complete(h, 1, 2, 90, race: true);
        Assert.Equal(new[] { 90.0 }, h.Laps(1));
    }

    [Fact]
    public void Average_uses_all_laps_until_the_window_fills_then_the_best_ones()
    {
        Assert.Equal(91.0, LapHistory.AverageBest(new[] { 90.0, 92.0 }, 5));
        // 6 laps, window 5: the slowest (100) is dropped.
        Assert.Equal(91.0, LapHistory.AverageBest(new[] { 90.0, 100.0, 91.0, 92.0, 90.5, 91.5 }, 5));
        Assert.Null(LapHistory.AverageBest(new[] { 90.0 }, 5));
    }

    [Fact]
    public void Gap_is_theirs_minus_mine_and_positive_when_the_player_is_faster()
    {
        Assert.Equal(0.5, LapHistory.AverageGap(new[] { 90.5, 90.5 }, new[] { 90.0, 90.0 }, 5)!.Value, 6);
        Assert.Null(LapHistory.AverageGap(new[] { 90.5 }, new[] { 90.0, 90.0 }, 5));
    }

    [Fact]
    public void Reset_forgets_everything()
    {
        var h = new LapHistory();
        h.Update(1, 0, 0, false, false);
        Complete(h, 1, 1, 90);
        h.Reset();
        Assert.Empty(h.Laps(1));
    }
}
