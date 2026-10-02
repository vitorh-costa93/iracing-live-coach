using System.Globalization;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Theme;
using Ams2.Shared.Profiles;

namespace Ams2.OverlayHost.Widgets;

/// <summary>Clima: temperatura do ar e da pista e intensidade da chuva (o AMS2 não expõe umidade).</summary>
public sealed class WeatherWidget : IWidget
{
    public string Id => "weather";
    public (float Width, float Height) DesignSize => (295, 128);

    const float LabelX = 87, ValueRight = 275, Row0 = 33, RowPitch = 29;

    public void Configure(WidgetSettings s) { }

    public void Draw(ThemeCanvas c, OverlayModel m)
    {
        var t = c.Theme;
        var (w, h) = DesignSize;
        c.Panel(0, 0, w, h);
        DrawCloud(c, 18, 20);
        Chrome.Header(c, "WEATHER", LabelX, t.Style == ThemeStyle.Broadcast98 ? 11 : 3, 40, underline: false);

        var wx = m.Session?.Weather;
        if (!m.Connected || wx is null)
        {
            c.Text(m.Connected ? "NO DATA" : "WAITING", t.Label, LabelX, Row0 + 10, 200, 30, t.LabelColor, shadow: t.TextShadow);
            return;
        }

        var f = t.Label with { Size = 22 };
        Row(c, "AIR", Math.Round(wx.AmbientC).ToString("0", CultureInfo.InvariantCulture), "°C", 0, f);
        Row(c, "TRACK", Math.Round(wx.TrackC).ToString("0", CultureInfo.InvariantCulture), "°C", 1, f);
        Row(c, "RAIN", Math.Round(Math.Clamp(wx.RainDensity, 0, 1) * 100).ToString("0", CultureInfo.InvariantCulture), "%", 2, f);
    }

    static void Row(ThemeCanvas c, string label, string value, string unit, int row, FontToken labelFont)
    {
        var t = c.Theme;
        float y = Row0 + row * RowPitch;
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
