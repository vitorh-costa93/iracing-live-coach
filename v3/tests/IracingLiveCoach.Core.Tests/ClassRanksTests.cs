using System.Linq;
using IracingLiveCoach.Core.Telemetry;
using Xunit;

namespace IracingLiveCoach.Core.Tests;

public class ClassRanksTests
{
    [Fact]
    public void Faster_relative_speed_ranks_first()
    {
        var ranks = ClassRanks.Compute([new(11, 40, 130), new(22, 100, 95), new(33, 70, 110), new(44, 10, 150)]);
        Assert.Equal(1, ranks[22]);
        Assert.Equal(2, ranks[33]);
        Assert.Equal(3, ranks[11]);
        Assert.Equal(4, ranks[44]);
    }

    [Fact]
    public void Lap_time_breaks_a_speed_tie_and_duplicates_collapse()
    {
        var ranks = ClassRanks.Compute([new(1, 50, 120), new(2, 50, 110), new(2, 50, 110)]);
        Assert.Equal(2, ranks.Count);
        Assert.Equal(1, ranks[2]);
        Assert.Equal(2, ranks[1]);
    }

    [Fact]
    public void Single_class_and_invalid_ids()
    {
        Assert.Equal(1, ClassRanks.Compute([new(7, 0, 0)])[7]);
        Assert.Empty(ClassRanks.Compute([new(0, 10, 100)]));
    }
}
