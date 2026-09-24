namespace IracingLiveCoach.OverlayHost.Layout;

/// <summary>Keeps saved column lists in step with columns added to a widget later.</summary>
public static class ColumnDefaults
{
    /// <summary>A saved profile predates columns added later (e.g. Standings' posChange): every default
    /// column missing from <paramref name="columns"/> is inserted right after the column that precedes it in
    /// <paramref name="defaults"/> (first if none does) and the orders are renumbered 0..n-1, so an old
    /// profile gains the new column instead of never showing it. The user's own columns keep their settings
    /// and relative order; nothing missing = the same list back.</summary>
    public static List<ColumnDefinition> MergeMissing(List<ColumnDefinition> columns, IReadOnlyList<ColumnDefinition> defaults)
    {
        var ordered = columns.OrderBy(c => c.Order).ToList();
        bool changed = false;
        for (int d = 0; d < defaults.Count; d++)
        {
            if (ordered.Any(c => c.Key == defaults[d].Key)) continue;
            int at = 0;
            for (int p = d - 1; p >= 0; p--)
            {
                int found = ordered.FindIndex(c => c.Key == defaults[p].Key);
                if (found >= 0) { at = found + 1; break; }
            }
            ordered.Insert(at, defaults[d]);
            changed = true;
        }
        return changed ? ordered.Select((c, i) => c with { Order = i }).ToList() : columns;
    }
}
