using System.Collections.Generic;
using IracingLiveCoach.Core;
using Xunit;

namespace IracingLiveCoach.Core.Tests;

public class LiveCoachEngineTests
{
    private static CornerBaseline Corner(int number, double start, double end) =>
        new(number, $"Turn {number}", start, end, null, null, null, null, null, null);

    [Fact]
    public void Fires_CornerCompleted_exactly_once_when_the_car_leaves_a_corners_window()
    {
        var corners = new List<CornerBaseline> { Corner(1, 10, 20), Corner(2, 40, 50) };
        var engine = new LiveCoachEngine(corners, gearModel: null, trackLengthMeters: null);
        var completed = new List<CornerFeedback>();
        engine.CornerCompleted += feedback => completed.Add(feedback);

        // Approach, enter, and exit corner 1; nothing between 20 and 40 is inside any corner.
        engine.Update(new TelemetrySample(LapDistPct: 5, null, null, null, null, null, null));
        engine.Update(new TelemetrySample(LapDistPct: 12, null, null, null, null, null, null));
        engine.Update(new TelemetrySample(LapDistPct: 18, null, null, null, null, null, null));
        engine.Update(new TelemetrySample(LapDistPct: 25, null, null, null, null, null, null)); // exits corner 1's window here

        Assert.Single(completed);
        Assert.Equal(1, completed[0].CornerNumber);
        Assert.Equal("Turn 1", completed[0].CornerName);
    }

    [Fact]
    public void Does_not_fire_while_still_inside_the_same_corner()
    {
        var corners = new List<CornerBaseline> { Corner(1, 10, 20) };
        var engine = new LiveCoachEngine(corners, gearModel: null, trackLengthMeters: null);
        var completedCount = 0;
        engine.CornerCompleted += _ => completedCount++;

        engine.Update(new TelemetrySample(12, null, null, null, null, null, null));
        engine.Update(new TelemetrySample(14, null, null, null, null, null, null));
        engine.Update(new TelemetrySample(16, null, null, null, null, null, null));

        Assert.Equal(0, completedCount);
    }

    [Fact]
    public void Tracks_two_separate_corners_across_a_lap()
    {
        var corners = new List<CornerBaseline> { Corner(1, 10, 20), Corner(2, 40, 50) };
        var engine = new LiveCoachEngine(corners, gearModel: null, trackLengthMeters: null);
        var completed = new List<CornerFeedback>();
        engine.CornerCompleted += feedback => completed.Add(feedback);

        foreach (var pct in new[] { 12.0, 18.0, 25.0, 45.0, 55.0 })
            engine.Update(new TelemetrySample(pct, null, null, null, null, null, null));

        Assert.Equal(2, completed.Count);
        Assert.Equal(1, completed[0].CornerNumber);
        Assert.Equal(2, completed[1].CornerNumber);
    }
}
