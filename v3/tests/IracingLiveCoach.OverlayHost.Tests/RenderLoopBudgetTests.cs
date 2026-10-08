using IracingLiveCoach.OverlayHost.Layout;

namespace IracingLiveCoach.OverlayHost.Tests;

public class RenderLoopBudgetTests
{
    [Theory]
    [InlineData(0, false, 50)]
    [InlineData(20, false, 30)]
    [InlineData(100, false, 0)]
    [InlineData(0, true, 5)]
    [InlineData(10, true, 0)]
    public void Hidden_windows_have_an_explicit_idle_wait_without_delaying_slow_frames(double elapsed, bool visible, int expected)
        => Assert.Equal(expected, RenderLoopBudget.DelayMilliseconds(elapsed, visible));
}
