namespace IracingLiveCoach.OverlayHost.Layout;

/// <summary>Keep the message loop bounded even when hidden swap chains do not wait for vblank.</summary>
public static class RenderLoopBudget
{
    public static int DelayMilliseconds(double elapsedMilliseconds, bool anyVisible)
    {
        double interval = anyVisible ? 1000.0 / 240 : 50;
        return (int)Math.Ceiling(Math.Max(0, interval - elapsedMilliseconds));
    }
}
