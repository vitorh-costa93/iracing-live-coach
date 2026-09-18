using System.Collections.Generic;
using IracingLiveCoach.Core.Telemetry;
using Xunit;

namespace IracingLiveCoach.Core.Tests;

public class ClassIntervalsTests
{
    private static readonly Dictionary<int, double> NoEstimates = new();

    [Fact]
    public void Class_leader_has_no_interval_and_followers_measure_to_the_car_ahead_in_class()
    {
        // overall order: P1 GTP, P2 GT3, P3 GT3, P4 GT3 -- the GT3 cars must not measure to the GTP car
        var drivers = new[]
        {
            new IntervalInput(2, 1, 1, 12, 0),
            new IntervalInput(1, 2, 1, 12, 5.0),
            new IntervalInput(1, 3, 2, 12, 8.8),
            new IntervalInput(1, 4, 3, 12, 10.0),
        };
        var result = ClassIntervals.Compute(drivers, NoEstimates);
        Assert.Null(result[0]);
        Assert.Null(result[1]);
        Assert.Equal(3.8, result[2]!.Value, 3);
        Assert.Equal(1.2, result[3]!.Value, 3);
    }

    [Fact]
    public void Same_lap_prefers_estimated_times_over_gaps()
    {
        var drivers = new[] { new IntervalInput(1, 1, 1, 5, 0), new IntervalInput(1, 2, 2, 5, 99) };
        var est = new Dictionary<int, double> { [1] = 10.0, [2] = 12.5 };
        Assert.Equal(2.5, ClassIntervals.Compute(drivers, est)[1]!.Value, 3);
    }

    [Fact]
    public void Inconsistent_data_yields_null_never_a_negative_number()
    {
        var drivers = new[] { new IntervalInput(1, 1, 1, 5, 50), new IntervalInput(1, 2, 2, 4, 10) };
        Assert.Null(ClassIntervals.Compute(drivers, NoEstimates)[1]);
    }
}
