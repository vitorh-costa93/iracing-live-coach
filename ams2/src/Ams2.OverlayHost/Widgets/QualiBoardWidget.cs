using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Theme;
using Ams2.Shared.Profiles;

namespace Ams2.OverlayHost.Widgets;

/// <summary>Qualification lower board selected by broadcast style.</summary>
public sealed class QualiBoardWidget : IWidget
{
    public string Id => "qualiboard";
    public (float Width, float Height) DesignSize => (1920, 300);
    readonly Broadcast93QualiBoard _board93 = new();
    readonly Broadcast99QualiBoard _board99 = new();
    ThemeStyle _style = ThemeStyle.Broadcast98;
    WidgetSettings _cfg = new() { Id = "qualiboard" };
    public void UseTheme(Theme.Theme theme)
    {
        _style = theme.Style;
    }
    public void Configure(WidgetSettings settings) => _cfg = settings;
    public void Draw(ThemeCanvas c, OverlayModel m)
    {
        if (_style == ThemeStyle.Broadcast93) { _board93.Draw(c, m, _cfg); return; }
        if (_style == ThemeStyle.Broadcast98) _board99.Draw(c, m, _cfg);
    }
}
