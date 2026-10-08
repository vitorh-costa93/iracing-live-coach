using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Widgets;

namespace Ams2.Integration.Tests;

public class LapCounterFormatTests
{
    [Theory]
    [InlineData(0, 1, "58/58", "1/58")]
    [InlineData(22, 23, "36/58", "23/58")]
    [InlineData(58, 59, "0/58", "58/58")]
    public void Remaining_laps_follow_leader_and_other_themes_keep_current_lap(int completed, int current, string remaining, string normal)
    {
        var model = LayoutPreview.ForWidget("lapcounter");
        var session = model.Session!;
        var leader = session.Cars.OrderBy(c => c.Position).First() with { Position = 1, LapsCompleted = completed, CurrentLap = current };
        model = model with { Session = session with { LapsInEvent = 58, Cars = new[] { leader }.Concat(session.Cars.Where(c => c.Index != leader.Index).Select(c => c with { Position = Math.Max(2, c.Position) })).ToArray() } };
        Assert.Equal(remaining, LapCounterWidget.Format(model, countdown: true));
        Assert.Equal(normal, LapCounterWidget.Format(model));
    }

    [Fact]
    public void Timed_and_disconnected_sessions_do_not_invent_a_countdown()
    {
        var model = LayoutPreview.ForWidget("lapcounter");
        model = model with { Session = model.Session! with { LapsInEvent = 0 } };
        Assert.StartsWith("Lap ", LapCounterWidget.Format(model, countdown: true));
        Assert.Equal("-- /--", LapCounterWidget.Format(OverlayModel.Empty, countdown: true));
    }
}
