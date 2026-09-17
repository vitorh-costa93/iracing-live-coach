namespace IracingLiveCoach.OverlayHost.Layout;

/// <summary>Physical placement of one column within a computed table layout.</summary>
/// <param name="Column">The source definition.</param>
/// <param name="OffsetXPx">Left edge, physical pixels, relative to the table's own left edge.</param>
/// <param name="ResolvedWidthPx">Final width after flexible-column shrinking, if any.</param>
public sealed record ColumnPlacement(ColumnDefinition Column, float OffsetXPx, float ResolvedWidthPx);

/// <summary>
/// Result of laying out one table (Standings or Relative). Carries enough for both the geometry
/// acceptance test (spec §17) and the actual draw call (Phase 3) to consume identically.
/// </summary>
/// <param name="Columns">Resolved left-to-right column placements, visible columns only, in display order.</param>
/// <param name="TableWidthPx">Full table width: columns + separators + borders (spec §12's auto-width formula).</param>
/// <param name="TableHeightPx">Full table height: header + rows + separators + borders.</param>
/// <param name="FitsWithinBudget">False if the table exceeds the physical limit even after shrinking flexible columns to their minimums.</param>
/// <param name="MissingPx">0 if it fits; otherwise how many pixels over budget, for the Control Center to report verbatim (spec §17: "mostre quantos pixels faltam").</param>
public sealed record TableLayoutResult(
    IReadOnlyList<ColumnPlacement> Columns,
    float TableWidthPx,
    float TableHeightPx,
    bool FitsWithinBudget,
    float MissingPx);

/// <summary>
/// Pure geometry/layout math — no GPU resources, no telemetry, no I/O. Deliberately free of any
/// DirectWrite/Direct2D dependency so it is trivially unit-testable and so the same functions can
/// back both the live overlay and the Control Center's preview (spec §12's shared-engine requirement).
/// Text measurement itself lives in <see cref="TextMeasurer"/> (a thin DirectWrite wrapper) and is
/// injected here as a plain <c>Func&lt;string, float&gt;</c> so this class never touches a live device.
/// </summary>
public static class WidgetLayoutEngine
{
    /// <summary>Spec §17: physical width/height ceilings for a target area (e.g. one monitor).
    /// These are TETOS (ceilings), not target dimensions — never pad a table up to them.</summary>
    public static (float MaxWidthPx, float MaxHeightForSevenRowsPx) ComputePhysicalLimits(
        float targetAreaWidthPx, float targetAreaHeightPx) =>
        (MathF.Floor(0.25f * targetAreaWidthPx), MathF.Floor(0.35f * targetAreaHeightPx));

    /// <summary>Sum of every visible column's footprint (width + own padding) — the column-only part
    /// of spec §12's auto-width formula, before separators/borders are added.</summary>
    public static float SumVisibleColumnFootprints(IEnumerable<ColumnDefinition> columns) =>
        columns.Where(c => c.Visible).Sum(c => c.FootprintPx);

    /// <summary>
    /// Lays out one table: resolves left-to-right column offsets, computes total width/height,
    /// and — if the result exceeds <paramref name="maxWidthPx"/> — shrinks Flexible columns toward
    /// their minimums (never Fixed ones, never by deforming typography) before reporting whether it
    /// still doesn't fit. Never removes a column or truncates a value on its own (spec §17: "não
    /// resolva excedentes... removendo informações obrigatórias silenciosamente").
    /// </summary>
    public static TableLayoutResult LayoutTable(
        IReadOnlyList<ColumnDefinition> columns,
        int rowCount,
        float rowHeightPx,
        float headerHeightPx,
        float separatorWidthPx,
        float borderWidthPx,
        float maxWidthPx,
        float maxHeightPx)
    {
        var visible = columns.Where(c => c.Visible).OrderBy(c => c.Order).ToList();
        if (visible.Count == 0)
            return new TableLayoutResult([], 0, 0, true, 0);

        var separatorsTotal = separatorWidthPx * Math.Max(0, visible.Count - 1);
        var bordersTotal = borderWidthPx * 2;

        float naturalWidth = SumVisibleColumnFootprints(visible) + separatorsTotal + bordersTotal;
        float overflow = naturalWidth - maxWidthPx;

        var resolvedWidths = visible.ToDictionary(c => c.Key, c => c.WidthPx);

        if (overflow > 0)
        {
            // Shrink only Flexible columns, proportionally to how much each can still give up,
            // down to their configured minimums -- never Fixed columns, never below the minimum.
            var flexible = visible.Where(c => c.WidthMode == ColumnWidthMode.Flexible).ToList();
            float shrinkable = flexible.Sum(c => Math.Max(0, c.WidthPx - c.MinWidthPx));

            if (shrinkable > 0)
            {
                float toRemove = Math.Min(overflow, shrinkable);
                foreach (var col in flexible)
                {
                    float colShrinkable = Math.Max(0, col.WidthPx - col.MinWidthPx);
                    if (colShrinkable <= 0) continue;
                    float share = colShrinkable / shrinkable * toRemove;
                    resolvedWidths[col.Key] = col.WidthPx - share;
                }
                overflow -= toRemove;
            }
        }

        float x = borderWidthPx;
        var placements = new List<ColumnPlacement>(visible.Count);
        foreach (var col in visible)
        {
            float w = resolvedWidths[col.Key] + col.PaddingLeftPx + col.PaddingRightPx;
            placements.Add(new ColumnPlacement(col, x, resolvedWidths[col.Key]));
            x += w + separatorWidthPx;
        }

        float finalWidth = placements.Sum(p => p.ResolvedWidthPx + p.Column.PaddingLeftPx + p.Column.PaddingRightPx)
                            + separatorsTotal + bordersTotal;

        float tableHeight = headerHeightPx + rowHeightPx * rowCount + borderWidthPx * 2;

        bool fits = finalWidth <= maxWidthPx && tableHeight <= maxHeightPx;
        float missingPx = fits ? 0 : Math.Max(finalWidth - maxWidthPx, tableHeight - maxHeightPx);

        return new TableLayoutResult(placements, finalWidth, tableHeight, fits, missingPx);
    }
}
