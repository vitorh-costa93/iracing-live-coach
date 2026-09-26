namespace IracingLiveCoach.OverlayHost.Layout;

/// <summary>Per-widget text/row sizing (spec §12: "aumentar fonte... altura das linhas e
/// espaçamento... por widget" -- opacity already lived on <see cref="WidgetPlacement"/>, this is
/// the rest of that group). Pure data so it round-trips through persistence and IPC identically to
/// every other override in <see cref="WidgetPlacementStore"/>.</summary>
/// <param name="FontScale">Multiplier applied to every text format a widget owns (its own base
/// sizes are the widget's tuned defaults at 1.0).</param>
/// <param name="RowHeightDip">Table-row height override for row-based widgets (Standings/Relative).
/// 0 means "use the widget's own default" -- never persisted as a guessed absolute for widgets that
/// have no rows.</param>
/// <param name="FontWeight">0 = the widget's own weights; 400 = Regular; 600 = SemiBold (the two bundled cuts).</param>
/// <param name="FontFamily">Key of <see cref="FontCatalog"/> (barlow, chakra, plex, inter, sfpro); missing in old profiles = barlow.</param>
/// <param name="PaddingHDip">Right padding of every padded table column; negative = each column's own default.</param>
/// <param name="RowSpacingDip">Extra vertical gap inserted between consecutive rows, on top of
/// <paramref name="RowHeightDip"/>. Ignored by widgets with no row list.</param>
public sealed record WidgetAppearance(float FontScale, float RowHeightDip, float RowSpacingDip, int FontWeight = 0, float PaddingHDip = -1f, string FontFamily = "barlow")
{
    public static WidgetAppearance Default { get; } = new(FontScale: 1f, RowHeightDip: 0f, RowSpacingDip: 0f);
}
