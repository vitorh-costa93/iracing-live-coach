using IracingLiveCoach.Core.Telemetry;

namespace IracingLiveCoach.Core.Tests;

public sealed class RaceLengthEstimatorTests
{
    private const int A = 4029, B = 4011;
    private static readonly Dictionary<int, int> Classes = new() { [39] = A, [0] = A, [16] = B };
    private static readonly Dictionary<int, double> Est = new() { [A] = 66.218, [B] = 75.8217 };
    // Road Atlanta 45-min race, 24/09/2026: Lone Qualify pole #39 70.4252 (QualifyResultsInfo, 0-based positions).
    private static readonly List<GridResult> Grid = [new(39, 0, 0, 70.4252), new(0, 4, 4, 70.7942), new(16, 13, 0, 75.0)];

    private static RaceLengthTick Tick(double remain, (int Car, int Lap, double Pct)[] cars, List<RaceResult> results, int state = 4, int? limit = null, int flags = 0) =>
        new(state, flags, remain, false, results, cars.Select(c => new CarLapTick(c.Car, Classes[c.Car], c.Lap, c.Pct)).ToList(),
            Classes, Est, Grid, 2700, limit);

    [Fact]
    public void Before_a_lap_time_the_grid_decides()
    {
        var e = new RaceLengthEstimator().Update(Tick(2700, [(39, 1, 0.0), (16, 1, 0.0)], [], state: 3));
        Assert.Equal(38.34, e[A].Laps, 2);                              // Kapps "R 0/≈38.34" = 2700 / 70.4252
        Assert.Equal(Math.Ceiling(2700 / 70.4252) * 70.4252 / 75.0, e[B].Laps, 6);
        Assert.False(e[A].Exact);
    }

    [Fact]
    public void Class_leader_laps_plus_remaining_time_on_the_class_lap_time()
    {
        var r = new RaceLengthEstimator();
        r.Update(Tick(2621, [(39, 1, 0.99)], []));
        r.Update(Tick(2620.2, [(39, 2, 0.0003)], []));                 // leader completes lap 1
        // Results after lap 1: LastTime still -1 -> no lap time -> the grid stays (Kapps "2/≈38.34").
        var e1 = r.Update(Tick(2617, [(39, 2, 0.04)], [new(39, 1, 0, 1, 0, -1)]));
        Assert.Equal(38.34, e1[A].Laps, 2);
        r.Update(Tick(2549.5, [(39, 2, 0.998)], [new(39, 1, 0, 1, 0, -1)]));
        r.Update(Tick(2549.31, [(39, 3, 0.0)], [new(39, 1, 0, 1, 0, -1)]));
        var e2 = r.Update(Tick(2545, [(39, 3, 0.06)], [new(39, 1, 0, 2, 0, 71.072)]));
        // 2 + 2549.31 / 71.072 = 37.87 -- Kapps' fuel "Laps in Race" 37.87 on that lap.
        Assert.Equal(37.87, e2[A].Laps, 2);
        // Stable between the leader's crossings.
        Assert.Equal(e2[A].Laps, r.Update(Tick(2500, [(39, 3, 0.7)], [new(39, 1, 0, 2, 0, 71.072)]))[A].Laps);
    }

    [Fact]
    public void Lap_time_is_the_mean_of_the_last_five_below_the_fastest_plus_two()
    {
        var r = new RaceLengthEstimator();
        double remain = 2000;
        int lap = 1;
        foreach (var t in new[] { 79.6, 71.07, 69.75, 69.59, 71.96, 69.43, 75.0 })
        {
            r.Update(Tick(remain, [(39, lap, 0.99)], [new(39, 1, 0, lap - 1, 0, t)]));
            lap++;
            r.Update(Tick(remain, [(39, lap, 0.0)], [new(39, 1, 0, lap - 1, 0, t)]));
            remain -= 70;
        }
        // last five: 69.75 69.59 71.96 69.43 75.0 -> below 71.43: 69.75 69.59 69.43
        Assert.Equal((69.75 + 69.59 + 69.43) / 3, r.AverageLapTime(A)!.Value, 6);
    }

    [Fact]
    public void Other_classes_end_when_the_overall_leader_takes_the_flag()
    {
        var r = new RaceLengthEstimator();
        // leader (A) and B leader cross; results update with both classes' laps.
        r.Update(Tick(1001, [(39, 10, 0.99), (16, 9, 0.99)], []));
        r.Update(Tick(1000, [(39, 11, 0.0), (16, 9, 0.99)], []));
        r.Update(Tick(990, [(39, 11, 0.14), (16, 10, 0.0)], []));
        var e = r.Update(Tick(988, [(39, 11, 0.17), (16, 10, 0.03)], [new(39, 1, 0, 10, 0, 70), new(16, 2, 0, 9, 90, 80)]));
        double lA = 10 + 1000 / 70.0;                                   // 24.29
        double remainB = 990 + (90 - 90);                               // B's own crossing
        double leaderRemain = Math.Ceiling(lA - 10) * 70 - (1000 - remainB);
        Assert.Equal(lA, e[A].Laps, 6);
        Assert.Equal(9 + leaderRemain / 80, e[B].Laps, 6);
    }

    [Fact]
    public void Without_a_matching_crossing_the_backup_plus_three_seconds_is_used()
    {
        var r = new RaceLengthEstimator();
        // No line records at all (the app started mid-race): remain = backup + 3.
        var e = r.Update(Tick(1500, [(39, 12, 0.1)], [new(39, 1, 0, 11, 0, 70)]));
        Assert.Equal(11 + 1503 / 70.0, e[A].Laps, 6);
    }

    [Fact]
    public void A_lap_limit_below_the_estimate_wins_exactly()
    {
        var e = new RaceLengthEstimator().Update(Tick(2700, [(39, 1, 0.0)], [], state: 3, limit: 20));
        Assert.True(e[A].Exact);
        Assert.Equal(20, e[A].Laps);
    }

    [Fact]
    public void Estimates_freeze_after_the_flag()
    {
        var r = new RaceLengthEstimator();
        var before = r.Update(Tick(1500, [(39, 12, 0.1)], [new(39, 1, 0, 11, 0, 70)]))[A].Laps;
        var after = r.Update(Tick(604000, [(39, 13, 0.1)], [new(39, 1, 0, 12, 0, 70)], state: 5))[A].Laps;
        Assert.Equal(before, after);
    }

    [Fact]
    public void Caution_laps_do_not_enter_the_lap_time()
    {
        var r = new RaceLengthEstimator();
        r.Update(Tick(1500, [(39, 12, 0.1)], [new(39, 1, 0, 11, 0, 70)]));
        r.Update(Tick(1400, [(39, 13, 0.1)], [new(39, 1, 0, 12, 0, 120)], flags: 0x4000));
        Assert.Equal(70, r.AverageLapTime(A));
    }
}
