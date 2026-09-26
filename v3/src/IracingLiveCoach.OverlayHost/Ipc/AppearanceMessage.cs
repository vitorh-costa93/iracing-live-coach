namespace IracingLiveCoach.OverlayHost.Ipc;

/// <summary>Sixth typed/versioned IPC message: one widget's font-scale/row-height/row-spacing
/// override (spec §12). Keyed by WidgetKey like the column-config channel, since appearance is
/// per-widget, not global.</summary>
public sealed record AppearanceMessage(int SchemaVersion, string WidgetKey, float FontScale, float RowHeightDip, float RowSpacingDip, int FontWeight = 0, float PaddingHDip = -1f, string? FontFamily = null, string? BackgroundColor = null, float BackgroundOpacity = -1f)
{
    public const int CurrentSchemaVersion = 2; // v2: FontFamily, BackgroundColor, BackgroundOpacity (all optional)
}
