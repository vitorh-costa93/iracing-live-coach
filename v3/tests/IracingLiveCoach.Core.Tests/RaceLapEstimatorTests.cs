using IracingLiveCoach.Core.Telemetry;

namespace IracingLiveCoach.Core.Tests;

public class RaceLapEstimatorTests
{
    [Fact]
    public void Lap_limited_race_uses_the_limit_and_counts_the_lap_in_progress()
    {
        // Kapps at the start of a 30-lap race, sitting at 92% of lap 0: 29.08 laps left.
        var e = RaceLapEstimator.Estimate(30, 86400, 0.92, 0.92, 75)!;
        Assert.Equal(30, e.TotalLaps);
        Assert.False(e.IsEstimate);
        Assert.Equal(29.08, e.PlayerLapsRemaining, 3);
    }

    [Fact]
    public void Time_limited_race_projects_the_total_from_lap_time()
    {
        // 40 min left, 80 s laps, leader at 5.5 laps: 5.5 + 30 = 35.5 -> finishes lap 36.
        var e = RaceLapEstimator.Estimate(RaceLapEstimator.UnlimitedLaps, 2400, 5.5, 5.5, 80)!;
        Assert.Equal(36, e.TotalLaps);
        Assert.True(e.IsEstimate);
        Assert.Equal(30.5, e.PlayerLapsRemaining, 3);
    }

    [Fact]
    public void A_player_behind_on_track_finishes_at_the_first_crossing_after_the_leader()
    {
        // Leader at 10.2, player at 9.4 in a 20-lap race: player is at 19.2 when the leader finishes -> flag at 20.
        var e = RaceLapEstimator.Estimate(20, null, 10.2, 9.4, null)!;
        Assert.Equal(20, e.TotalLaps);
        Assert.Equal(10.6, e.PlayerLapsRemaining, 3);
    }

    [Fact]
    public void Kapps_live_case_ten_laps_down()
    {
        // Live SF23 session: player completed lap 4 (4.0) with the leader at ~11.2 of 30.
        // Player is at 22.8 when the leader finishes -> flag at 23 -> 19 laps; Kapps: 46.66 - 13.98 = 32.68 L = 19 x 1.72.
        var e = RaceLapEstimator.Estimate(30, 86000, 11.2, 4.0, 76)!;
        Assert.Equal(19, e.PlayerLapsRemaining, 3);
    }

    [Fact]
    public void Kapps_example_two_laps_in_a_thirty_lap_race()
    {
        // User's session: 2 laps done, back at the line -> 28 laps to go; 28 x 1.72 = 48.16 L.
        var e = RaceLapEstimator.Estimate(30, 86000, 2.0, 2.0, 76)!;
        Assert.Equal(28, e.PlayerLapsRemaining, 3);
    }

    [Fact]
    public void Both_limits_end_at_whichever_comes_first()
    {
        var byTime = RaceLapEstimator.Estimate(50, 600, 10, 10, 60)!; // 10 min at 60 s = 10 more laps -> 20 < 50
        Assert.Equal(20, byTime.TotalLaps);
        Assert.True(byTime.IsEstimate);
        var byLaps = RaceLapEstimator.Estimate(15, 6000, 10, 10, 60)!;
        Assert.Equal(15, byLaps.TotalLaps);
        Assert.False(byLaps.IsEstimate);
    }

    [Fact]
    public void Time_race_without_a_lap_time_cannot_be_estimated()
        => Assert.Null(RaceLapEstimator.Estimate(RaceLapEstimator.UnlimitedLaps, 2400, 1, 1, null));

    [Fact]
    public void Clock_at_zero_mid_lap_still_finishes_the_current_lap()
    {
        var e = RaceLapEstimator.Estimate(RaceLapEstimator.UnlimitedLaps, 0, 25.4, 25.4, 80)!;
        Assert.Equal(26, e.TotalLaps);
        Assert.Equal(0.6, e.PlayerLapsRemaining, 3);
    }

    [Fact]
    public void Multiclass_faster_leader_ends_the_timed_race_and_the_slower_player_covers_fewer_laps()
    {
        // 10 min left, leader laps in 60 s -> finishes at 20.0 (10 more laps). The player (90 s laps) covers
        // 10 x 60/90 = 6.67 laps meanwhile: 10.0 -> 16.67 -> flag at 17 -> 7 laps to go.
        var e = RaceLapEstimator.Estimate(RaceLapEstimator.UnlimitedLaps, 600, 10.0, 10.0, 90, 60)!;
        Assert.Equal(20, e.TotalLaps);
        Assert.Equal(7, e.PlayerLapsRemaining, 3);
    }

    [Fact]
    public void Leader_lap_time_null_keeps_the_single_class_behaviour()
    {
        var a = RaceLapEstimator.Estimate(RaceLapEstimator.UnlimitedLaps, 600, 10.0, 9.5, 60)!;
        var b = RaceLapEstimator.Estimate(RaceLapEstimator.UnlimitedLaps, 600, 10.0, 9.5, 60, null)!;
        Assert.Equal(a, b);
    }
}
