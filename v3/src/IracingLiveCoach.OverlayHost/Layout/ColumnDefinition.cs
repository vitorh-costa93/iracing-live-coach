namespace IracingLiveCoach.OverlayHost.Layout;

public enum ColumnWidthMode
{
    /// <summary>Width is fixed at <see cref="ColumnDefinition.WidthPx"/>; never stretched or shrunk
    /// to fill available space (spec §12: "não estica fonte ou todas as outras colunas").</summary>
    Fixed,

    /// <summary>Width may be reduced toward <see cref="ColumnDefinition.MinWidthPx"/> when the table
    /// doesn't fit its budget (spec §17: "ajuste apenas colunas explicitamente flexíveis até seus mínimos").</summary>
    Flexible
}

public enum ColumnAlignment { Left, Center, Right }

/// <summary>
/// One column's configuration, shared verbatim between the Control Center's preview and the live
/// overlay render (spec §12: "Preview e overlay real devem compartilhar o mesmo motor de layout").
/// This is pure configuration data — no drawing, no telemetry — so it can be unit-tested and
/// serialized without touching GPU resources.
/// </summary>
/// <param name="Key">Stable identifier (e.g. "position", "irating", "gap") — never the display order index.</param>
/// <param name="WidthMode">Fixed or Flexible (spec §12).</param>
/// <param name="WidthPx">Current configured width in physical pixels.</param>
/// <param name="MinWidthPx">Minimum width a Flexible column may shrink to; ignored for Fixed columns.</param>
/// <param name="Alignment">Text alignment within the column.</param>
/// <param name="PaddingLeftPx">Left padding, physical pixels.</param>
/// <param name="PaddingRightPx">Right padding, physical pixels.</param>
/// <param name="Visible">Hidden columns are excluded from width sums entirely, not drawn empty.</param>
/// <param name="Order">Display order among visible columns; reorderable independent of <see cref="Key"/>.</param>
/// <param name="DecimalPlaces">Null for non-numeric columns; spec §12's per-field configurable precision.</param>
public sealed record ColumnDefinition(
    string Key,
    ColumnWidthMode WidthMode,
    float WidthPx,
    float MinWidthPx,
    ColumnAlignment Alignment,
    float PaddingLeftPx,
    float PaddingRightPx,
    bool Visible,
    int Order,
    int? DecimalPlaces = null)
{
    /// <summary>Total footprint of this column including its own padding — what actually gets summed
    /// for the table's auto-width (spec §12: "largura automática de tabela = soma das colunas visíveis + paddings...").</summary>
    public float FootprintPx => WidthPx + PaddingLeftPx + PaddingRightPx;
}
