using IracingLiveCoach.Core.Telemetry;
using Xunit;

namespace IracingLiveCoach.Core.Tests;

public class FuelLapsRemainTests
{
    [Theory]
    [InlineData(46.34, 3.26, 0.09, 14.31)]  // Kapps qualy print
    [InlineData(45.40, 3.26, 0.215, 14.14)] // Kapps race lap 2
    [InlineData(20.34, 3.26, 0.912, 7.16)]  // Kapps race lap 9
    public void Matches_kapps_laps_remain(double fuel, double perLap, double pct, double kapps)
        => Assert.Equal(kapps, FuelLapsRemain.Compute(fuel, perLap, pct)!.Value, 1);

    [Fact]
    public void Off_track_or_no_average()
    {
        Assert.Equal(10.0, FuelLapsRemain.Compute(30, 3, -1)!.Value, 6);
        Assert.Equal(10.0, FuelLapsRemain.Compute(30, 3, null)!.Value, 6);
        Assert.Null(FuelLapsRemain.Compute(30, null, 0.5));
        Assert.Null(FuelLapsRemain.Compute(30, 0, 0.5));
    }
}
