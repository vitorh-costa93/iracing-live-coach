using IracingLiveCoach.Core.Telemetry;
using Xunit;

namespace IracingLiveCoach.Core.Tests;

public class FinalResultsTests
{
    // Watkins Glen 24/09/2026 cool-down, first five of ResultsPositions.
    private static readonly FinalResultEntry[] Glen =
    {
        new(1, 1, 0, 0.0, 102.8345, 12),
        new(3, 2, 1, 1.4959, 103.0107, 12),
        new(0, 3, 2, 3.4225, 103.4981, 12),
        new(8, 4, 3, 7.8478, 103.8116, 12),
        new(5, 5, 4, 8.5129, 103.7343, 12),
    };

    [Theory]
    [InlineData(true, 5, true)]
    [InlineData(true, 6, true)]
    [InlineData(true, 4, false)]
    [InlineData(false, 6, false)]
    public void Applies_only_to_a_race_from_the_chequered_flag_on(bool isRace, int state, bool expected) =>
        Assert.Equal(expected, FinalResults.Applies(isRace, state));

    [Fact]
    public void Positions_come_from_the_classification_with_one_based_class_positions()
    {
        var (overall, byClass) = FinalResults.Positions(Glen);
        Assert.Equal(5, overall[5]);
        Assert.Equal(5, byClass[5]);
        Assert.Equal(1, byClass[1]);
        Assert.Equal(5, overall.Count);
    }

    [Fact]
    public void Intervals_from_result_times_match_kapps()
    {
        var inputs = Glen.Select(r => new IntervalInput(0, r.Position, r.ClassPosition + 1, r.LapsComplete, null, r.Time)).ToList();
        var intervals = ClassIntervals.Compute(inputs, raceMode: true);
        Assert.Equal(4.4, Math.Truncate(intervals[3].Seconds!.Value * 10) / 10, 3);
        Assert.Equal(0.6, Math.Truncate(intervals[4].Seconds!.Value * 10) / 10, 3);
    }

    [Fact]
    public void Total_laps_is_the_winners_completed_laps() =>
        Assert.Equal(12, FinalResults.TotalLaps(Glen));
}
