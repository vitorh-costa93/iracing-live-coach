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
        Assert.Equal(1.0, v.Seconds!.Value, 6);
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
        Assert.Equal(1.0, r[1].Seconds!.Value, 6);
    }

    [Fact]
    public void Small_local_est_time_inversion_does_not_fabricate_a_finish_line_wrap()
    {
        // Live start, 24/09/2026: Josh (0.429) 0.005 laps behind Jonas (0.434) but with the higher EstTime.
        // Both cars are on the same lap: the small EstTime inversion is not a crossing of the line.
        var v = ClassIntervals.RaceInterval(Car(1, 4, 4, 0.429, 28.025), Car(1, 3, 3, 0.434, 27.879), 67.4669);
        Assert.Equal(0.146, v.Seconds!.Value, 3);
    }

    [Fact]
    public void After_the_flag_same_lap_cars_use_the_final_time_difference()
    {
        // 24/09/2026 official results: winner Time 0, P2 Time 17.5409, both 35 laps -> Kapps "17.5".
        var v = ClassIntervals.RaceInterval(Car(1, 2, 2, 35, null, gap: 17.5409), Car(1, 1, 1, 35, null, gap: 0), 67.47);
        Assert.Equal(17.5409, v.Seconds!.Value, 3);
    }

    [Fact]
    public void Race_interval_uses_the_same_live_est_time_as_relative_instead_of_distance_or_own_lap_trace()
    {
        var v = ClassIntervals.RaceInterval(Car(1, 2, 2, 5.50, 30), Car(1, 1, 1, 5.52, 33.3), 67);
        Assert.Equal(RelativeGap.LiveSeconds(33.3, 30, 67, .02, 67), v.Seconds);
        Assert.Equal(3.3, v.Seconds!.Value, 6); // distance fallback would give 1.34 s
        var w = ClassIntervals.RaceInterval(Car(1, 2, 2, 5.99, 66), Car(1, 1, 1, 6.01, 1), 67);
        Assert.Equal(2, w.Seconds!.Value, 6);
    }

    [Fact]
    public void Preceding_classified_car_over_half_a_lap_ahead_keeps_the_forward_interval()
    {
        var v = ClassIntervals.RaceInterval(Car(1, 2, 2, 5.1, 10), Car(1, 1, 1, 5.9, 90), 100);
        Assert.Equal(80, v.Seconds);
        Assert.Equal(20, RelativeGap.LiveSeconds(90, 10, 100, -.2, 100));
        Assert.Equal(80, ClassIntervals.RaceInterval(Car(1, 2, 2, 5.9, 90), Car(1, 1, 1, 6.7, 70), 100).Seconds);
    }

    [Fact]
    public void Gap_to_leader_is_the_forward_est_time_gap_never_an_invented_zero()
    {
        // Same class, same lap: leader 3.3 s up the road (the old "max(0, mine - leader)" read 0).
        Assert.Equal(3.3, ClassIntervals.RaceGapToLeader(Car(1, 2, 2, 5.50, 30, gap: 4.0), Car(1, 1, 1, 5.52, 33.3, gap: 0), 67)!.Value, 6);
        // Across the line: leader just crossed, I have not.
        Assert.Equal(2, ClassIntervals.RaceGapToLeader(Car(1, 2, 2, 5.99, 66, gap: 1.8), Car(1, 1, 1, 6.01, 1, gap: 0), 67)!.Value, 6);
        // Another class's curve is another time scale: F2Time. Lapped: F2Time. Negative/missing: null.
        Assert.Equal(12.5, ClassIntervals.RaceGapToLeader(Car(2, 5, 1, 5.50, 30, gap: 12.5), Car(1, 1, 1, 5.60, 40, gap: 0), 67));
        Assert.Equal(70, ClassIntervals.RaceGapToLeader(Car(1, 9, 9, 4.50, 30, gap: 70), Car(1, 1, 1, 5.60, 40, gap: 0), 67));
        Assert.Null(ClassIntervals.RaceGapToLeader(Car(2, 5, 1, 5.50, 30, gap: -1), Car(1, 1, 1, 5.60, 40, gap: 0), 67));
    }

    [Fact]
    public void Missing_est_time_uses_forward_distance_times_class_lap()
    {
        var v = ClassIntervals.RaceInterval(Car(1, 2, 2, 5.1, null), Car(1, 1, 1, 5.9, 90), 100);
        Assert.Equal(80, v.Seconds!.Value, 6);
    }

    [Fact]
    public void Nonuniform_reference_curve_uses_forward_time_independent_of_the_spatial_half_lap()
    {
        var v = ClassIntervals.RaceInterval(Car(1, 2, 2, 5.1, 10), Car(1, 1, 1, 5.4, 90), 100);
        Assert.Equal(80, v.Seconds);
        Assert.Equal(0, ClassIntervals.RaceInterval(Car(1, 2, 2, 5.1, 10), Car(1, 1, 1, 5.4, 10), 100).Seconds);
    }
}
