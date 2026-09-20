namespace IracingLiveCoach.OverlayHost.Layout;

/// <summary>
/// Pure width math behind the Control Center's "Limite de largura" warning and "Ajustar larguras"
/// button: how wide a column set renders, and how to trim it toward a limit without wrecking it.
/// </summary>
public static class ColumnFitter
{
    /// <summary>Left margin every table has before its first column (class strip + gap).</summary>
    public const float LeftMargin = 8f;

    /// <summary>A data column is never trimmed below this share of its default width.</summary>
    public const float FitFloor = 0.8f;

    /// <summary>The flexible name column is never narrowed below this.</summary>
    public const float MinNameWidth = 90f;

    private static readonly string[] KeepSize = ["position", "flag", "brand", "name"];

    /// <summary>Width of one column including its paddings; <paramref name="paddingH"/> &gt;= 0 replaces
    /// the right padding of every padded column (the appearance's "Padding (H)").</summary>
    public static float Footprint(ColumnDefinition column, float paddingH) =>
        column.WidthPx + column.PaddingLeftPx + (paddingH >= 0 && column.PaddingRightPx > 0 ? paddingH : column.PaddingRightPx);

    /// <summary>Total table width: left margin plus every visible column.</summary>
    public static float TotalWidth(IEnumerable<ColumnDefinition> columns, float paddingH) =>
        LeftMargin + columns.Where(c => c.Visible).Sum(c => Footprint(c, paddingH));

    /// <summary>Trims <paramref name="columns"/> toward <paramref name="limit"/>: first the name column
    /// (down to <see cref="MinNameWidth"/>), then the other data columns proportionally (down to
    /// <see cref="FitFloor"/> of their <paramref name="defaults"/> width); icons keep their size. Returns
    /// the new list; <paramref name="remaining"/> is how many px still exceed the limit (0 when it fits).</summary>
    public static List<ColumnDefinition> Fit(IReadOnlyList<ColumnDefinition> columns, IReadOnlyList<ColumnDefinition>? defaults, float limit, float paddingH, out float remaining)
    {
        var result = columns.ToList();
        float over = TotalWidth(result, paddingH) - limit;
        if (over <= 0) { remaining = 0f; return result; }

        float DefaultWidth(ColumnDefinition c) => defaults?.FirstOrDefault(d => d.Key == c.Key)?.WidthPx ?? c.WidthPx;

        int nameIndex = result.FindIndex(c => c.Key == "name" && c.Visible);
        if (nameIndex >= 0)
        {
            var name = result[nameIndex];
            float newWidth = Math.Max(MinNameWidth, Math.Min(name.WidthPx, name.WidthPx - over));
            over -= name.WidthPx - newWidth;
            result[nameIndex] = name with { WidthPx = newWidth, MinWidthPx = Math.Min(name.MinWidthPx, newWidth) };
        }

        for (int pass = 0; pass < 6 && over > 0.5f; pass++)
        {
            var candidates = result.Select((c, i) => (c, i)).Where(t => t.c.Visible && !KeepSize.Contains(t.c.Key)).ToList();
            float RoomOf(ColumnDefinition c) => Math.Max(0f, c.WidthPx - FitFloor * DefaultWidth(c));
            float roomTotal = candidates.Sum(t => RoomOf(t.c));
            if (roomTotal < 1f) break;
            foreach (var (column, index) in candidates)
            {
                float room = RoomOf(column);
                float cut = Math.Min(room, over * room / roomTotal);
                float width = MathF.Floor(column.WidthPx - cut);
                result[index] = column with { WidthPx = width, MinWidthPx = Math.Min(column.MinWidthPx, width) };
            }
            over = TotalWidth(result, paddingH) - limit;
        }

        remaining = Math.Max(0f, TotalWidth(result, paddingH) - limit);
        return result;
    }
}
