using System.Globalization;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Theme;
using Ams2.Shared.Profiles;

namespace Ams2.OverlayHost.Widgets;

/// <summary>Clima: temperatura do ar e da pista e intensidade da chuva (o AMS2 não expõe umidade).</summary>
public sealed class WeatherWidget : IWidget
{
    public string Id => "weather";
    // O título do 1998 é alto (fonte grande): cabeçalho mais alto e linhas empurradas para baixo, sem colidir com AIR.
    bool _b98;
    public void UseTheme(Theme.Theme theme) => _b98 = theme.Style == ThemeStyle.Broadcast98;
    /// <summary>Linhas visiveis (ar, pista, chuva) sobem para ocupar o lugar das ocultas; o painel encolhe junto.</summary>
    int Rows => (_cfg.ColumnVisible("air") ? 1 : 0) + (_cfg.ColumnVisible("track") ? 1 : 0) + (_cfg.ColumnVisible("rain") ? 1 : 0);
    public (float Width, float Height) DesignSize => (295, Row0 + Math.Max(Rows, 1) * RowPitch + (_b98 ? 5 : 8));

    const float LabelX = 87, ValueRight = 275, RowPitch = 29;
    float Row0 => _b98 ? 52 : 33;

    WidgetSettings _cfg = new() { Id = "weather" };
    public void Configure(WidgetSettings s) => _cfg = s;

    public void Draw(ThemeCanvas c, OverlayModel m)
    {
        var t = c.Theme;
        var (w, h) = DesignSize;
        c.Panel(0, 0, w, h);
        DrawCloud(c, 18, 20);
        Chrome.Header(c, "WEATHER", LabelX, _b98 ? 4 : 3, 40, underline: false);

        var wx = m.Session?.Weather;
        if (!m.Connected || wx is null)
        {
            Chrome.Notice(c, m.Connected ? "NO DATA" : "WAITING", LabelX, Row0 + 10, 200);
            return;
        }

        var f = t.Label with { Size = 22 };
        var tu = _cfg.Fmt.TempOrDefault;
        string Temp(double celsius) => Math.Round(DisplayFormat.Temp(celsius, tu)).ToString("0", CultureInfo.InvariantCulture);
        int row = 0;
        if (_cfg.ColumnVisible("air")) Row(c, Row0, "AIR", Temp(wx.AmbientC), DisplayFormat.TempLabel(tu), row++, f);
        if (_cfg.ColumnVisible("track")) Row(c, Row0, "TRACK", Temp(wx.TrackC), DisplayFormat.TempLabel(tu), row++, f);
        if (_cfg.ColumnVisible("rain")) Row(c, Row0, "RAIN", Math.Round(Math.Clamp(wx.RainDensity, 0, 1) * 100).ToString("0", CultureInfo.InvariantCulture), "%", row++, f);
    }

    static void Row(ThemeCanvas c, float row0, string label, string value, string unit, int row, FontToken labelFont)
    {
        var t = c.Theme;
        float y = row0 + row * RowPitch;
        if (t.Style == ThemeStyle.Broadcast2000s)
        {
            Chrome.WhiteCell(c, LabelX, y, 92, 27, label, labelFont);
            Chrome.BlackCell(c, LabelX + 92, y, ValueRight + 8 - LabelX - 92, 27, value + " " + unit, t.Numbers with { Size = 22 });
            return;
        }
        c.Text(label, labelFont, LabelX, y, 120, 28, t.LabelColor, shadow: t.TextShadow);
        Chrome.ValueUnit(c, value, unit, ValueRight, y - 2, 30, t.ReadoutColor, true, t.Label with { Size = 20 });
    }

    /// <summary>Nuvem em tinta clara: três elipses e uma base, sem depender de glifo.</summary>
    static void DrawCloud(ThemeCanvas c, float x, float y)
    {
        var col = c.Theme.TitleColor;
        c.FillEllipse(x + 12, y + 24, 12, 11, col);
        c.FillEllipse(x + 28, y + 14, 14, 14, col);
        c.FillEllipse(x + 42, y + 25, 12, 11, col);
        c.FillRect(x + 12, y + 24, 30, 11, col);
    }
}
