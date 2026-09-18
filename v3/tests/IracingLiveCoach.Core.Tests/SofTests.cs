using IracingLiveCoach.Core.Telemetry;
using Xunit;

namespace IracingLiveCoach.Core.Tests;

public class SofTests
{
    [Fact]
    public void Compute_of_identical_ratings_is_that_rating()
    {
        Assert.Equal(3000, Sof.Compute([3000, 3000, 3000])!.Value, 3);
    }

    [Fact]
    public void Compute_weights_towards_the_stronger_drivers()
    {
        double sof = Sof.Compute([1000, 5000])!.Value;
        Assert.InRange(sof, 1000, 3000); // below the arithmetic mean: iRacing's SOF is dominated by the weaker field
    }

    [Fact]
    public void Compute_returns_null_when_no_positive_ratings()
    {
        Assert.Null(Sof.Compute([]));
        Assert.Null(Sof.Compute([0, -1]));
    }
}
