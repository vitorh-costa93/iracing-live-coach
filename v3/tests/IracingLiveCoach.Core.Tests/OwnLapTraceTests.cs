using IracingLiveCoach.Core.Telemetry;

namespace IracingLiveCoach.Core.Tests;

public sealed class OwnLapTraceTests
{
    /// <summary>Drives laps sampled at 20 Hz: slow first half (40 s), fast second half (20 s) = 60 s/lap,
    /// scaled by <paramref name="factor"/> per lap.</summary>
    private static double Drive(OwnLapTrace t, double start, int fromLap, double[] factors, bool pitOnFirst = false)
    {
        double time = start;
        for (int l = 0; l < factors.Length; l++)
        {
            double lapT = 60 * factors[l];
            for (double e = 0; e < lapT; e += 0.05)
            {
                double f = e / lapT * 60; // normalised time 0..60
                double pct = f < 40 ? f / 40 * 0.5 : 0.5 + (f - 40) / 20 * 0.5;
                t.Update(fromLap + l, pct, time + e, pitOnFirst && l == 0);
            }
            time += lapT;
        }
        t.Update(fromLap + factors.Length, 0.0005, time + 0.03, false);
        return time;
    }

    [Fact]
    public void Gap_is_the_players_best_lap_time_between_the_two_spots()
    {
        var t = new OwnLapTrace();
        Drive(t, 0, 0, new[] { 1.0, 1.2, 1.0, 1.1 }); // lap 0 joins mid-way? no: starts at the line but unknown start
        Assert.NotNull(t.BestLapSeconds);
        Assert.Equal(60, t.BestLapSeconds!.Value, 0);
        // Slow half: 0.10 laps take 8 s; fast half: 0.10 laps take 4 s.
        Assert.Equal(8.0, t.Seconds(0.20, 0.30)!.Value, 1);
        Assert.Equal(4.0, t.Seconds(0.70, 0.80)!.Value, 1);
        // Across the line: from 0.95 to 0.05 = 2 s + 4 s.
        Assert.Equal(6.0, t.Seconds(0.95, 0.05)!.Value, 1);
    }

    [Fact]
    public void Braking_does_not_change_the_gap_it_only_depends_on_positions()
    {
        var t = new OwnLapTrace();
        Drive(t, 0, 0, new[] { 1.0, 1.0, 1.0 });
        double a = t.Seconds(0.40, 0.45)!.Value;
        double b = t.Seconds(0.40, 0.45)!.Value;
        Assert.Equal(a, b);
        Assert.Equal(4.0, a, 1);
    }

    [Fact]
    public void Nothing_until_a_clean_complete_lap_and_pit_laps_do_not_count()
    {
        var t = new OwnLapTrace();
        Assert.Null(t.Seconds(0.1, 0.2));
        Drive(t, 0, 0, new[] { 1.0 });           // the first lap's start is unknown: not usable
        Assert.Null(t.BestLapSeconds);
        var p = new OwnLapTrace();
        Drive(p, 0, 0, new[] { 1.0, 0.9, 1.0 }, pitOnFirst: false);
        Assert.Equal(54, p.BestLapSeconds!.Value, 0);
    }
}
