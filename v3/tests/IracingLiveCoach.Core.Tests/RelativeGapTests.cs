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

    [Fact]
    public void Shared_live_gap_keeps_the_relatives_same_class_curve_and_nearest_distance_contract()
    {
        Assert.Equal(3.3, RelativeGap.LiveSeconds(33.3, 30, 67, .02, 67)!.Value, 6);
        Assert.Equal(2, RelativeGap.LiveSeconds(1, 66, 67, .02, 67));
        Assert.Equal(20, RelativeGap.LiveSeconds(90, 10, 100, -.2, 100));
    }

    [Fact]
    public void Shared_live_gap_uses_multiclass_curve_then_distance_fallback()
    {
        Assert.Equal(RelativeGapKapps.Seconds(37.4, 35, 70), RelativeGap.LiveSeconds(37.4, 35, 70, .02, 67));
        Assert.Equal(.02 * 67, RelativeGap.LiveSeconds(37.4, null, 70, .02, 67));
        Assert.Null(RelativeGap.LiveSeconds(null, null, 70, .02, null));
    }

    private static EstTimeCurves TwoClassCurves()
    {
        // Class 1 (player's, 100 s) is uniform; class 2 (120 s) loses 1.5 s in a slow corner at 0.50-0.52.
        var curves = new EstTimeCurves();
        for (int i = 0; i < 1000; i++)
        {
            double pct = i / 1000.0;
            curves.Add(1, pct, pct * 100);
            curves.Add(2, pct, pct * 120 + (pct > 0.5 ? Math.Min(pct - 0.5, 0.02) / 0.02 * 1.5 : 0));
        }
        return curves;
    }

    private static readonly Dictionary<int, double> TwoClassLaps = new() { [1] = 100, [2] = 120 };

    [Fact]
    public void Multiclass_car_two_ahead_never_reads_closer_than_the_car_ahead()
    {
        // Multiclass race 28/10/2026: a slow-class car just ahead (its own curve, through the slow corner)
        // read 3.9 s, a same-class car physically beyond it 3.0 s -- the per-car formula alone inverts.
        var curves = TwoClassCurves();
        var player = new RelativeCar(1, 0.50, 50);
        var slow = new RelativeCar(2, 0.52, 0.52 * 120 + 1.5);
        var fast = new RelativeCar(1, 0.53, 53);
        double slowAlone = RelativeGap.LiveSeconds(slow.EstTime, curves.At(2, 0.50), 120, .02, 100)!.Value;
        double fastAlone = RelativeGap.LiveSeconds(fast.EstTime, player.EstTime, 100, .03, 100)!.Value;
        Assert.True(fastAlone < slowAlone); // the reported bug, reproduced

        var gaps = RelativeGap.SideGaps(player, [slow, fast], curves, TwoClassLaps, 100);
        Assert.Equal(3.9, gaps[0], 6);
        Assert.Equal(3.9, gaps[1], 6); // floored at the car ahead of it, never closer
    }

    [Fact]
    public void Coherent_gaps_keep_the_exact_est_time_values_on_both_sides()
    {
        var curves = TwoClassCurves();
        var player = new RelativeCar(1, 0.30, 30);
        var ahead = RelativeGap.SideGaps(player, [new(1, 0.31, 31), new(2, 0.33, 0.33 * 120), new(1, 0.45, 45)], curves, TwoClassLaps, 100);
        Assert.Equal(new[] { 1.0, 3.6, 15.0 }, ahead.Select(g => Math.Round(g, 6)));
        var behind = RelativeGap.SideGaps(player, [new(1, 0.29, 29), new(2, 0.20, 24), new(1, 0.99, 99)], curves, TwoClassLaps, 100);
        Assert.Equal(new[] { 1.0, 12.0, 31.0 }, behind.Select(g => Math.Round(g, 6)));
    }

    [Fact]
    public void Behind_side_is_monotonic_and_unknown_gap_does_not_drop_to_zero()
    {
        var curves = TwoClassCurves();
        var player = new RelativeCar(1, 0.53, 53);
        // Slow class just behind, through its slow corner (3.9 s); fast car beyond it 3.0 s; then a class with
        // no curve and no lap time at all (direct gap unknown = 0) must not read closer either.
        var gaps = RelativeGap.SideGaps(player, [new(2, 0.505, curves.At(2, 0.505)!.Value), new(1, 0.50, 50), new(7, 0.49, null)],
            curves, TwoClassLaps, 0);
        Assert.True(gaps[0] > 3.0);
        Assert.Equal(gaps[0], gaps[1], 6);
        Assert.Equal(gaps[1], gaps[2], 6);
    }

    [Fact]
    public void Directed_gap_wraps_reference_time_while_relative_always_takes_the_nearest_time()
    {
        Assert.Equal(80, RelativeGap.LiveSeconds(90, 10, 100, .3, 100, forward: true));
        Assert.Equal(20, RelativeGap.LiveSeconds(90, 10, 100, .3, 100));
        Assert.Equal(2, RelativeGap.LiveSeconds(1, 99, 100, .02, 100, forward: true, crossedLine: true));
        Assert.Equal(0, RelativeGap.LiveSeconds(10, 10, 100, .8, 100, forward: true));
        Assert.Equal(0, RelativeGap.LiveSeconds(0, 0, 100, .8, 100, forward: true));
        Assert.Equal(0, RelativeGap.LiveSeconds(0, 0, 100, .02, 100, forward: true, crossedLine: true));
        Assert.Equal(.146, RelativeGap.LiveSeconds(27.879, 28.025, 67.4669, .005, 67.4669, forward: true)!.Value, 6);
    }
}
