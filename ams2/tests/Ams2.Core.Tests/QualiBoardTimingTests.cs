using Ams2.Core.Calc;

namespace Ams2.Core.Tests;

public class QualiBoardTimingTests
{
    [Fact]
    public void Finish_reference_remains_visible_until_crossing_even_when_player_is_slower()
    {
        Assert.False(QualiBoardTiming.ReferenceVisible(72, 66.99, 3));
        Assert.True(QualiBoardTiming.ReferenceVisible(72, 67, 3));
        Assert.True(QualiBoardTiming.ReferenceVisible(72, 72, 3));
        Assert.True(QualiBoardTiming.ReferenceVisible(72, 85, 3));
        Assert.False(QualiBoardTiming.ReferenceVisible(22, 23, 1));
        Assert.False(QualiBoardTiming.ReferenceVisible(null, 85, 3));
        Assert.False(QualiBoardTiming.ReferenceVisible(72, double.NaN, 3));
    }
    static QualiLapState Lap(double? elapsed) => QualiLapState.Empty with { CarIndex = 0, Elapsed = elapsed };

    [Theory]
    [InlineData(0, QualiBoardStage.Lap)]
    [InlineData(4.999, QualiBoardStage.Lap)]
    [InlineData(5, QualiBoardStage.Lap)]
    [InlineData(11.999, QualiBoardStage.Lap)]
    [InlineData(12, QualiBoardStage.Lap)]
    [InlineData(80, QualiBoardStage.Lap)]
    public void Flying_lap_boundaries(double elapsed, QualiBoardStage expected)
        => Assert.Equal(expected, QualiBoardTiming.Stage(Lap(elapsed), 100));

    [Fact]
    public void Unknown_start_is_not_a_flying_lap_and_pit_is_not_an_out_lap()
    {
        Assert.Equal(QualiBoardStage.Hidden, QualiBoardTiming.Stage(Lap(null), 100));
        Assert.Equal(QualiBoardStage.Tower, QualiBoardTiming.Stage(Lap(null) with { OutLap = true }, 100));
        Assert.Equal(QualiBoardStage.Hidden, QualiBoardTiming.Stage(Lap(6) with { InPit = true, OutLap = true }, 100));
        Assert.Equal(QualiBoardStage.Hidden, QualiBoardTiming.Stage(Lap(double.NaN), 100));
    }

    [Fact]
    public void Previous_result_never_overrides_the_new_flying_lap_stage()
    {
        var q = Lap(5) with { LastResult = new(1, 70, 1, 0, 0, true, false, [], 98, true) };
        Assert.Equal(QualiBoardStage.Lap, QualiBoardTiming.Stage(q, 100));
        Assert.Equal(QualiBoardStage.Lap, QualiBoardTiming.Stage(q with { Elapsed = null }, 100));
        Assert.Equal(QualiBoardStage.Hidden, QualiBoardTiming.Stage(q with { Elapsed = null }, 101));
    }

    [Fact]
    public void Out_lap_classification_pages_once_then_caption_until_line_and_rearms_after_pit()
    {
        var flow = new QualiOutLapPresentation();
        var q = Lap(null) with { OutLap = true, Lap = 1 };
        Assert.Equal(QualiBoardStage.Tower, flow.Update(q, 100, 16));
        Assert.Equal(QualiBoardStage.Tower, flow.Update(q, 107, 16));
        Assert.Equal(7, flow.Age);
        Assert.Equal(QualiBoardStage.Caption, flow.Update(q, 114, 16));
        Assert.Equal(QualiBoardStage.Caption, flow.Update(q, 140, 16));
        foreach (double elapsed in new[] { 0, 5, 8, 12, 40 })
            Assert.Equal(QualiBoardStage.Lap, flow.Update(q with { OutLap = false, Lap = 2, Elapsed = elapsed }, 141 + elapsed, 16));
        Assert.Equal(QualiBoardStage.Hidden, flow.Update(q with { InPit = true }, 190, 16));
        Assert.Equal(QualiBoardStage.Tower, flow.Update(q, 191, 16));
        Assert.Equal(QualiBoardStage.Tower, flow.Update(q, 10, 16)); // clock/session restart
        Assert.Equal(QualiBoardStage.Tower, flow.Update(q with { CarIndex = 1 }, 30, 16));
        Assert.Equal(QualiBoardStage.Caption, flow.Update(q with { CarIndex = 1 }, 50, 16));
        Assert.Equal(QualiBoardStage.Tower, flow.Update(q with { CarIndex = 1, SessionGeneration = 1 }, 51, 16));
    }

    [Fact]
    public void Reference_rejects_theoretical_sector_sum_and_uses_observed_split_delta()
    {
        var q = Lap(21) with { PersonalBestLap = 72, PersonalBestSectors = [20, 22, 29] };
        Assert.Null(QualiBoardTiming.ReferenceTime(q, 1, true));
        Assert.Equal(20.5, QualiBoardTiming.ReferenceTime(q, 1, true, new(1, 21, .5, null, 100)));
        Assert.Equal(72, QualiBoardTiming.ReferenceTime(q, 3, true));
        Assert.Equal(42, QualiBoardTiming.ReferenceTime(q with { PersonalBestSectors = [20, 22, 30] }, 2, true));
        Assert.Null(QualiBoardTiming.ReferenceTime(q, 2, false));
    }

    [Fact]
    public void Rows_reveal_sequentially_after_the_title()
    {
        Assert.Equal(0, QualiBoardTiming.RowAlpha(.59, 0));
        Assert.Equal(1, QualiBoardTiming.RowAlpha(.8, 0));
        Assert.Equal(0, QualiBoardTiming.RowAlpha(.8, 1));
        Assert.Equal(1, QualiBoardTiming.RowAlpha(4, 7));
    }
}
