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
    public void A_lapped_player_still_plans_on_the_race_total_like_Kapps()
    {
        // Leader at 10.2, player a lap down at 9.4 in a 20-lap race: Kapps plans 20 - 9.4 = 10.6.
        var e = RaceLapEstimator.Estimate(20, null, 10.2, 9.4, null)!;
        Assert.Equal(20, e.TotalLaps);
        Assert.Equal(10.6, e.PlayerLapsRemaining, 3);
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
}
