using IracingLiveCoach.Core.Telemetry;

namespace IracingLiveCoach.Core.Tests;

public sealed class RelativeGapTests
{
    [Fact]
    public void Gap_is_the_physical_distance_at_the_players_reference_lap()
    {
        // Live start 24/09/2026 (GTP reference lap 67.4669 s): Brian Simpson 0.012 laps behind me.
        Assert.Equal(0.012 * 67.4669, RelativeGap.Seconds(0.407, 0.419, 67.4669), 3);
        Assert.True(RelativeGap.WrappedDelta(0.407, 0.419) < 0);
    }

    [Fact]
    public void Wraps_across_the_start_finish_line()
    {
        Assert.Equal(0.02, RelativeGap.WrappedDelta(0.01, 0.99), 6);   // just past the line = ahead
        Assert.Equal(-0.02, RelativeGap.WrappedDelta(0.99, 0.01), 6);  // just short of it = behind
        Assert.Equal(0.02 * 80, RelativeGap.Seconds(0.01, 0.99, 80), 6);
    }

    [Fact]
    public void Multiclass_uses_only_the_players_curve_so_the_gap_is_the_same_for_any_class()
    {
        // A GTP 0.01 laps ahead and a GT3 0.01 laps ahead are the same distance from me: same gap,
        // whatever their own class reference laps / EstTime curves are.
        double gtp = RelativeGap.Seconds(0.51, 0.50, 77.0);
        double gt3 = RelativeGap.Seconds(0.51, 0.50, 77.0);
        Assert.Equal(gtp, gt3, 9);
        Assert.Equal(0.77, gtp, 6);
    }

    [Fact]
    public void Unknown_reference_lap_gives_zero_not_garbage() =>
        Assert.Equal(0, RelativeGap.Seconds(0.3, 0.2, 0));
}
