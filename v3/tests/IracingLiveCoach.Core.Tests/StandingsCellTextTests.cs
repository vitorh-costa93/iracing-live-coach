using IracingLiveCoach.Core.Telemetry;
using Xunit;

namespace IracingLiveCoach.Core.Tests;

public class StandingsCellTextTests
{
    private static StandingsRow Row(int classPos, double? interval = null, int? laps = null, bool timed = false, double? last = 104.5, double? best = 103.074) =>
        new(classPos, "X", 3, last, null, false, "", "A 2.3", null, 3000, 1, "", null, null, null, "GT3", null, classPos, interval,
            null, null, null, false, "", IntervalLaps: laps, BestLapTime: best, TimedOrder: timed);

    [Fact]
    public void Kapps_race_interval_format()
    {
        Assert.Equal("INT", StandingsCellText.Interval(Row(1)));
        Assert.Equal("1.5", StandingsCellText.Interval(Row(2, 1.46)));
        Assert.Equal("0.0", StandingsCellText.Interval(Row(3, 0.02)));
        Assert.Equal("1L", StandingsCellText.Interval(Row(26, laps: 1)));
        Assert.Equal("—", StandingsCellText.Interval(Row(4)));
    }

    [Fact]
    public void Kapps_qualifying_interval_format_three_decimals_unsigned()
    {
        // Watkins Glen qualifying print: 1:43.235 - 1:43.074 = "0.161", 1:43.963 - 1:43.959 = "0.004".
        Assert.Equal("0.161", StandingsCellText.Interval(Row(2, 103.235 - 103.074, timed: true)));
        Assert.Equal("0.004", StandingsCellText.Interval(Row(6, 103.963 - 103.959, timed: true)));
        Assert.Equal("INT", StandingsCellText.Interval(Row(1, timed: true)));
    }

    [Fact]
    public void Position_change_marker()
    {
        Assert.Equal((PositionTrend.Down, "25"), StandingsCellText.PositionChange(-25));
        Assert.Equal((PositionTrend.Up, "2"), StandingsCellText.PositionChange(2));
        Assert.Equal((PositionTrend.None, ""), StandingsCellText.PositionChange(0));
        Assert.Equal((PositionTrend.None, ""), StandingsCellText.PositionChange(null));
    }

    [Fact]
    public void Lap_column_is_the_best_lap_when_ordered_by_best_lap()
    {
        Assert.Equal(103.074, StandingsCellText.LapColumn(Row(1, timed: true)));
        Assert.Equal(104.5, StandingsCellText.LapColumn(Row(1, timed: false)));
    }

    [Fact]
    public void Lap_text_is_truncated_like_kapps()
    {
        Assert.Equal("1:44.059", StandingsCellText.LapText(Row(7, timed: true, best: 104.0597)));
        Assert.Equal("1:43.074", StandingsCellText.LapText(Row(1, timed: true, best: 103.0742)));
        Assert.Equal("1:48.3", StandingsCellText.LapText(Row(2, last: 108.366)));
        Assert.Equal("1:50.2", StandingsCellText.LapText(Row(7, last: 110.281)));
        Assert.Equal("—", StandingsCellText.LapText(Row(7, last: null)));
    }
}
