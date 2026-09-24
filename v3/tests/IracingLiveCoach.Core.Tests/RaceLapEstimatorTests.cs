using IracingLiveCoach.Core.Telemetry;

namespace IracingLiveCoach.Core.Tests;

public class RaceLapEstimatorTests
{
    [Fact]
    public void Grid_position_behind_the_line_is_zero_progress()
    {
        Assert.Equal(0, RaceLapEstimator.Progress(-1, 0.995)!.Value, 6);
        Assert.Equal(3.25, RaceLapEstimator.Progress(3, 0.25)!.Value, 6);
        Assert.Null(RaceLapEstimator.Progress(3, -1));
    }
}
