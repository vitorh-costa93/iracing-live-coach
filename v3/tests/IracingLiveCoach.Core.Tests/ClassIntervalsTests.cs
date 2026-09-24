using System.Collections.Generic;
using IracingLiveCoach.Core.Telemetry;
using Xunit;

namespace IracingLiveCoach.Core.Tests;

public class ClassIntervalsTests
{
    private static IntervalInput Car(int cls, int pos, int classPos, double? progress, double? est, double? gap = null, double? best = null)
        => new(cls, pos, classPos, progress, est, gap, best);

    [Fact]
    public void Class_leader_has_no_interval_and_followers_measure_to_the_car_ahead_in_class()
    {
        // overall order: P1 GTP, P2 GT3, P3 GT3, P4 GT3 -- the GT3 cars must not measure to the GTP car
        var drivers = new[]
        {
            Car(2, 1, 1, 12.9, 90),
            Car(1, 2, 1, 12.5, 50.0),
            Car(1, 3, 2, 12.45, 46.2),
            Car(1, 4, 3, 12.44, 45.0),
        };
        var result = ClassIntervals.Compute(drivers, raceMode: true);
        Assert.Equal(IntervalValue.None, result[0]);
        Assert.Equal(IntervalValue.None, result[1]);
        Assert.Equal(3.8, result[2].Seconds!.Value, 3);
        Assert.Equal(1.2, result[3].Seconds!.Value, 3);
    }

    [Fact]
    public void Cars_either_side_of_the_line_use_est_time_plus_one_class_lap_not_a_dash()
    {
        // The regression behind the "—" seen live: the car ahead crossed the line (lap 6, est 0.3 s),
        // mine has not (lap 5 at 99.4 %, est 91.8 s on a 92.5 s reference lap) -> 0.3 + 92.5 - 91.8 = 1.0.
        // The stale F2Time difference (negative right after the line) must not be used.
        var ahead = Car(1, 1, 1, 6.003, 0.3, gap: 0);
        var me = Car(1, 2, 2, 5.994, 91.8, gap: -0.5);
        var v = ClassIntervals.RaceInterval(me, ahead, 92.5);
        Assert.Null(v.Laps);
        Assert.Equal(0.009 * 92.5, v.Seconds!.Value, 2); // distance x class lap, continuous across the line
    }

    [Fact]
    public void A_full_lap_or_more_up_the_road_is_reported_in_laps()
    {
        var v = ClassIntervals.RaceInterval(Car(1, 3, 3, 4.2, 20), Car(1, 2, 2, 5.7, 60), 90);
        Assert.Equal(1, v.Laps);
        Assert.Null(v.Seconds);
        Assert.Equal(2, ClassIntervals.RaceInterval(Car(1, 3, 3, 4.2, 20), Car(1, 2, 2, 6.3, 25), 90).Laps);
    }

    [Fact]
    public void Car_not_in_the_world_falls_back_to_f2_time_difference()
    {
        var v = ClassIntervals.RaceInterval(Car(1, 2, 2, null, null, gap: 12.5), Car(1, 1, 1, 3.5, 40, gap: 10.0), 90);
        Assert.Equal(2.5, v.Seconds!.Value, 3);
    }

    [Fact]
    public void Inconsistent_data_yields_none_never_a_negative_number()
    {
        var v = ClassIntervals.RaceInterval(Car(1, 2, 2, null, null, gap: 10), Car(1, 1, 1, null, null, gap: 50), 90);
        Assert.Equal(IntervalValue.None, v);
        // no class lap time known and the car ahead already past the line: no fabricated value
        Assert.Equal(IntervalValue.None, ClassIntervals.RaceInterval(Car(1, 2, 2, 5.99, 91), Car(1, 1, 1, 6.01, 1), null));
    }

    [Fact]
    public void Practice_and_qualifying_compare_best_laps_inside_the_class()
    {
        var drivers = new[]
        {
            Car(1, 1, 1, null, null, best: 100.100),
            Car(1, 2, 2, null, null, best: 100.500),
            Car(1, 3, 3, null, null, best: null), // no time yet
        };
        var r = ClassIntervals.Compute(drivers, raceMode: false);
        Assert.Equal(IntervalValue.None, r[0]);
        Assert.Equal(0.4, r[1].Seconds!.Value, 3);
        Assert.Equal(IntervalValue.None, r[2]);
    }

    [Fact]
    public void Class_lap_time_is_looked_up_by_the_class_id()
    {
        var drivers = new[] { Car(7, 1, 1, 6.003, 0.3), Car(7, 2, 2, 5.994, 91.8) };
        var r = ClassIntervals.Compute(drivers, raceMode: true, new Dictionary<int, double> { [7] = 92.5 });
        Assert.Equal(0.009 * 92.5, r[1].Seconds!.Value, 2);
    }

    [Fact]
    public void Same_lap_with_non_monotonic_est_time_uses_distance_not_a_whole_lap()
    {
        // Live start, 24/09/2026: Josh (0.429) 0.005 laps behind Jonas (0.434) but with the higher EstTime.
        // Kapps showed 0.3; adding a class lap gave 66.9.
        var v = ClassIntervals.RaceInterval(Car(1, 4, 4, 0.429, 28.025), Car(1, 3, 3, 0.434, 27.879), 67.4669);
        Assert.Equal(0.005 * 67.4669, v.Seconds!.Value, 3);
    }

    [Fact]
    public void After_the_flag_same_lap_cars_use_the_final_time_difference()
    {
        // 24/09/2026 official results: winner Time 0, P2 Time 17.5409, both 35 laps -> Kapps "17.5".
        var v = ClassIntervals.RaceInterval(Car(1, 2, 2, 35, null, gap: 17.5409), Car(1, 1, 1, 35, null, gap: 0), 67.47);
        Assert.Equal(17.5409, v.Seconds!.Value, 3);
    }

    [Fact]
    public void Race_interval_uses_the_track_time_function_when_available()
    {
        // 0.02 laps apart; the trace says that stretch takes 2.4 s (slow corner) -> 2.4, not 0.02 x 67.
        var v = ClassIntervals.RaceInterval(Car(1, 2, 2, 5.50, 30), Car(1, 1, 1, 5.52, 31), 67, (rear, front) => rear == 0.5 && Math.Abs(front - 0.52) < 1e-9 ? 2.4 : null);
        Assert.Equal(2.4, v.Seconds!.Value, 6);
        // Across the line the fractions wrap (0.99 -> 0.01).
        var w = ClassIntervals.RaceInterval(Car(1, 2, 2, 5.99, 30), Car(1, 1, 1, 6.01, 1), 67, (rear, front) => rear > 0.98 && front < 0.02 ? 1.3 : null);
        Assert.Equal(1.3, w.Seconds!.Value, 6);
    }
}
