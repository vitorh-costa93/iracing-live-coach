using Ams2.Core.Calc;

namespace Ams2.Core.Tests;

public class Broadcast04LapComparisonTests
{
    [Theory]
    [InlineData(-.471, false, true)]
    [InlineData(.471, false, false)]
    [InlineData(0, false, false)]
    [InlineData(-.471, true, false)]
    public void Faster_colour_follows_the_selected_reference_and_never_invalid_results(double delta, bool invalid, bool expected)
    {
        var result = new QualiLapResult(2, 78, 7, delta, -delta, true, invalid, [], 10, true);
        Assert.Equal(expected, Broadcast04LapComparison.Resolve(QualiLapState.Empty, null, result, false, 1)!.IsFaster);
        Assert.Equal(!invalid && -delta < 0, Broadcast04LapComparison.Resolve(QualiLapState.Empty, null, result, true, 7)!.IsFaster);
        Assert.Equal(delta < 0, Broadcast04LapComparison.Resolve(QualiLapState.Empty, new QualiSplit(1, 24, -delta, delta, 10), null, false, 1)!.IsFaster);
    }

    [Fact]
    public void Finish_keeps_reference_rank_separate_from_result_rank()
    {
        var result = new QualiLapResult(2, 78.917, 7, .685, .2, true, false, [], 100, true);
        var view = Broadcast04LapComparison.Resolve(QualiLapState.Empty, null, result, false, 1)!;
        Assert.Equal(1, view.ReferencePosition);
        Assert.Equal(7, view.ResultPosition);
        Assert.Equal(.685, view.Delta);
        Assert.Null(view.ReferenceTime);
        Assert.Equal(.2, Broadcast04LapComparison.Resolve(QualiLapState.Empty, null, result, true, 9)!.Delta);
    }

    [Fact]
    public void Reference_appears_only_near_known_mark_and_not_after_crossing()
    {
        var q = QualiLapState.Empty with { Sector = 2, LeaderBestLap = 78.232, Elapsed = 73.232 };
        Assert.Equal(78.232, Broadcast04LapComparison.Resolve(q, null, null, false, 1)!.ReferenceTime);
        Assert.Null(Broadcast04LapComparison.Resolve(q with { Elapsed = 73 }, null, null, false, 1));
        Assert.Null(Broadcast04LapComparison.Resolve(q with { Elapsed = 78.232 }, null, null, false, 1));
        Assert.Null(Broadcast04LapComparison.Resolve(q with { LeaderBestLap = null }, null, null, false, 1));
    }

    [Fact]
    public void Partial_does_not_fabricate_projected_finishing_position_or_reference()
    {
        var q = QualiLapState.Empty with { Sector = 1, Elapsed = 40, LeaderBestLap = 78, OverallBestSectors = [24, 25, 28] };
        Assert.Null(Broadcast04LapComparison.Resolve(q, null, null, false, 1));
        var split = new QualiSplit(1, 24.2, null, .3, 10);
        var view = Broadcast04LapComparison.Resolve(q, split, null, false, 1)!;
        Assert.Equal(.3, view.Delta);
        Assert.Null(view.ResultPosition);
        Assert.Null(view.ReferenceTime);
    }
}
