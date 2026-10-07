using Ams2.Core.Calc;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Theme;
using Ams2.Shared.Profiles;

namespace Ams2.OverlayHost.Widgets;

/// <summary>Single lower board: one classification pass on out lap, caption, then uninterrupted flying lap.</summary>
public sealed class QualiBoardWidget : IWidget
{
    public string Id => "qualiboard";
    public (float Width, float Height) DesignSize => (1920, 300);
    readonly QualiTowerWidget _tower = new();
    readonly QualiLapWidget _lap = new();
    ThemeStyle _style = ThemeStyle.Broadcast98;
    readonly QualiOutLapPresentation _presentation = new();
    WidgetSettings _cfg = new() { Id = "qualiboard" };
    public void UseTheme(Theme.Theme theme)
    {
        _style = theme.Style;
        _tower.UseTheme(theme); _lap.UseTheme(theme);
    }
    public void Configure(WidgetSettings settings) { _cfg = settings; _tower.Configure(settings); _lap.Configure(settings); }
    public void Draw(ThemeCanvas c, OverlayModel m)
    {
        if (_style != ThemeStyle.Broadcast98 || !m.Connected) { _presentation.Reset(); return; }
        if (m.Session is { InSession: false }) return;
        var stage = _presentation.Update(m.QualiLap, m.Now, m.Quali?.Rows.Count ?? 0, track: m.Session?.Track);
        if (stage == QualiBoardStage.Tower)
        {
            _tower.DrawBoard98(c, m, _presentation.Age);
        }
        else if (stage == QualiBoardStage.Caption) QualiCaption.Draw(c, m, _cfg, DesignSize.Width);
        else if (stage == QualiBoardStage.Lap) _lap.Draw(c, m);
    }
}
