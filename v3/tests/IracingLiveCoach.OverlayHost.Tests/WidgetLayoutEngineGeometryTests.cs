using IracingLiveCoach.OverlayHost.Layout;

namespace IracingLiveCoach.OverlayHost.Tests;

/// <summary>
/// Phase 2's mandated geometry acceptance test (see the plan): the three preset row counts
/// (1 GTP + 5 GT3 = 6 rows, 5 SF23 = 5 rows, Relative = 7 rows) at 100/125/150% DPI, asserted
/// against spec §17's physical ceilings (floor(0.25 x width), floor(0.35 x height)) for a fixed
/// 1920x1080 physical reference monitor.
///
/// The column set below is a representative synthetic shape, not Standings/Relative's final real
/// columns (that's Phase 3's job) -- its purpose is to exercise the layout engine's math, including
/// the exact DPI-scaling trap spec §17 calls out by name: "Em 150% de DPI, 480 DIPs NÃO equivalem
/// a 480 px físicos." Column widths below are DIP-authored (as a Control Center user would enter
/// them) and this test scales them to physical pixels the same way a real DPI change would.
/// </summary>
public class WidgetLayoutEngineGeometryTests
{
    private const float ReferenceMonitorWidthPx = 1920f;
    private const float ReferenceMonitorHeightPx = 1080f;
    private const float RowHeightDip = 24f;
    private const float HeaderHeightDip = 24f;
    private const float SeparatorWidthPx = 1f; // spec §19: grid thickness is physical-px-native, not DIP-scaled
    private const float BorderWidthPx = 1f;

    /// <summary>A representative 9-column set: class strip, position, car #, flag, brand, name
    /// (flexible), licence badge, iRating badge, gap (flexible). Widths/paddings are DIPs.</summary>
    private static List<ColumnDefinition> BuildColumnsAtDip() =>
    [
        new("classStrip", ColumnWidthMode.Fixed, 4, 4, ColumnAlignment.Left, 0, 0, true, 0),
        new("position", ColumnWidthMode.Fixed, 24, 24, ColumnAlignment.Center, 2, 2, true, 1),
        new("carNumber", ColumnWidthMode.Fixed, 28, 28, ColumnAlignment.Center, 2, 2, true, 2),
        new("flag", ColumnWidthMode.Fixed, 20, 20, ColumnAlignment.Center, 2, 2, true, 3),
        new("brand", ColumnWidthMode.Fixed, 20, 20, ColumnAlignment.Center, 2, 2, true, 4),
        new("name", ColumnWidthMode.Flexible, 140, 90, ColumnAlignment.Left, 2, 2, true, 5),
        new("licence", ColumnWidthMode.Fixed, 36, 36, ColumnAlignment.Center, 2, 2, true, 6),
        new("irating", ColumnWidthMode.Fixed, 56, 56, ColumnAlignment.Right, 2, 2, true, 7, DecimalPlaces: 0),
        new("gap", ColumnWidthMode.Flexible, 48, 36, ColumnAlignment.Right, 2, 2, true, 8, DecimalPlaces: 3),
    ];

    private static List<ColumnDefinition> ScaleToPhysical(IEnumerable<ColumnDefinition> dipColumns, float dpiScale) =>
        dipColumns.Select(c => c with
        {
            WidthPx = c.WidthPx * dpiScale,
            MinWidthPx = c.MinWidthPx * dpiScale,
            PaddingLeftPx = c.PaddingLeftPx * dpiScale,
            PaddingRightPx = c.PaddingRightPx * dpiScale
        }).ToList();

    [Theory]
    [InlineData(1.0f)]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    public void MulticlassPreset_1GTP_5GT3_SixRows_RespectsPhysicalCeilings(float dpiScale)
    {
        AssertPresetRespectsOrReportsCeiling(rowCount: 6, dpiScale);
    }

    [Theory]
    [InlineData(1.0f)]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    public void Sf23Preset_FiveRows_RespectsPhysicalCeilings(float dpiScale)
    {
        AssertPresetRespectsOrReportsCeiling(rowCount: 5, dpiScale);
    }

    [Theory]
    [InlineData(1.0f)]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    public void RelativePreset_SevenRows_RespectsPhysicalCeilings(float dpiScale)
    {
        AssertPresetRespectsOrReportsCeiling(rowCount: 7, dpiScale);
    }

    private static void AssertPresetRespectsOrReportsCeiling(int rowCount, float dpiScale)
    {
        var (maxWidthPx, maxHeightPx) = WidgetLayoutEngine.ComputePhysicalLimits(ReferenceMonitorWidthPx, ReferenceMonitorHeightPx);
        Assert.Equal(480f, maxWidthPx); // sanity check against spec §17's own worked numbers
        Assert.Equal(378f, maxHeightPx);

        var columns = ScaleToPhysical(BuildColumnsAtDip(), dpiScale);
        float rowHeightPx = RowHeightDip * dpiScale;
        float headerHeightPx = HeaderHeightDip * dpiScale;

        var result = WidgetLayoutEngine.LayoutTable(
            columns, rowCount, rowHeightPx, headerHeightPx, SeparatorWidthPx, BorderWidthPx, maxWidthPx, maxHeightPx);

        // The engine must never silently exceed the ceiling: either it fits (post-shrink), or it
        // honestly reports how many pixels are missing -- never a table wider/taller than the ceiling
        // while also claiming FitsWithinBudget.
        if (result.FitsWithinBudget)
        {
            Assert.True(result.TableWidthPx <= maxWidthPx + 0.01f, $"Reported fit but width {result.TableWidthPx} > {maxWidthPx}");
            Assert.True(result.TableHeightPx <= maxHeightPx + 0.01f, $"Reported fit but height {result.TableHeightPx} > {maxHeightPx}");
            Assert.Equal(0f, result.MissingPx);
        }
        else
        {
            Assert.True(result.MissingPx > 0);
        }

        // No column was ever removed or hidden by the engine -- exactly the input's visible count
        // survives into the placement list (spec §17: never silently drop a field).
        Assert.Equal(columns.Count(c => c.Visible), result.Columns.Count);

        // Fixed columns are never shrunk below their configured width, at any DPI or overflow.
        foreach (var placement in result.Columns.Where(p => p.Column.WidthMode == ColumnWidthMode.Fixed))
            Assert.Equal(placement.Column.WidthPx, placement.ResolvedWidthPx, precision: 3);

        // Flexible columns are never shrunk past their configured minimum.
        foreach (var placement in result.Columns.Where(p => p.Column.WidthMode == ColumnWidthMode.Flexible))
            Assert.True(placement.ResolvedWidthPx >= placement.Column.MinWidthPx - 0.01f);
    }

    [Fact]
    public void At100PercentDpi_SyntheticColumnSet_FitsComfortablyWithinWidthBudget()
    {
        var columns = BuildColumnsAtDip(); // 1.0 scale == DIP is physical px
        var (maxWidthPx, maxHeightPx) = WidgetLayoutEngine.ComputePhysicalLimits(ReferenceMonitorWidthPx, ReferenceMonitorHeightPx);

        var result = WidgetLayoutEngine.LayoutTable(columns, 7, RowHeightDip, HeaderHeightDip, SeparatorWidthPx, BorderWidthPx, maxWidthPx, maxHeightPx);

        Assert.True(result.FitsWithinBudget);
        // No shrink was needed at 100% DPI -- confirms the synthetic set is "comfortable at native scale".
        foreach (var p in result.Columns) Assert.Equal(p.Column.WidthPx, p.ResolvedWidthPx, precision: 3);
    }

    [Fact]
    public void At150PercentDpi_SameConfiguredWidths_NoLongerFit_DemonstratingTheDpiTrap()
    {
        // This is spec §17's exact warning, proven: the SAME DIP configuration that fits at 100%
        // physically cannot fit at 150% even after shrinking every flexible column to its minimum.
        var columns = ScaleToPhysical(BuildColumnsAtDip(), 1.5f);
        var (maxWidthPx, maxHeightPx) = WidgetLayoutEngine.ComputePhysicalLimits(ReferenceMonitorWidthPx, ReferenceMonitorHeightPx);

        var result = WidgetLayoutEngine.LayoutTable(columns, 7, RowHeightDip * 1.5f, HeaderHeightDip * 1.5f, SeparatorWidthPx, BorderWidthPx, maxWidthPx, maxHeightPx);

        Assert.False(result.FitsWithinBudget);
        Assert.True(result.MissingPx > 0);
    }
}
