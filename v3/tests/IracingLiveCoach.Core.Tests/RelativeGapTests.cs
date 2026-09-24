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

    [Fact]
    public void Kapps_gap_runs_on_the_other_cars_class_curve()
    {
        var curves = new EstTimeCurves();
        // Class 2 (slower) has a slow corner between 0.50 and 0.52: 2.4 s there; class 1 does it in 1.0 s.
        for (int i = 0; i < 1000; i++)
        {
            double pct = i / 1000.0;
            curves.Add(1, pct, pct * 66.0 + (pct > 0.5 ? 0.0 : 0));
            curves.Add(2, pct, pct * 68.5 + (pct > 0.5 ? Math.Min(pct - 0.5, 0.02) / 0.02 * 1.03 : 0));
        }
        // Class-2 car at 0.52 ahead of the player at 0.50: its own curve says ~2.4 s.
        var gap = RelativeGapKapps.Seconds(curves.At(2, 0.52), curves.At(2, 0.50), 68.5);
        Assert.InRange(gap!.Value, 2.3, 2.5);
        // The same distance measured on the player's (class 1) curve would be ~1.3 s: not what Kapps shows.
        Assert.InRange(RelativeGapKapps.Seconds(curves.At(1, 0.52), curves.At(1, 0.50), 66.0)!.Value, 1.2, 1.4);
    }

    [Fact]
    public void Kapps_gap_wraps_across_the_line_and_needs_a_curve()
    {
        Assert.Equal(0.6, RelativeGapKapps.Seconds(67.9, 0.0, 68.5)!.Value, 6);   // behind me, across the line
        Assert.Null(RelativeGapKapps.Seconds(null, 1.0, 68.5));
        Assert.Null(new EstTimeCurves().At(3, 0.4));
    }
}
