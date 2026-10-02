using System.Globalization;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Theme;
using Ams2.Shared.Profiles;

namespace Ams2.OverlayHost.Widgets;

/// <summary>Pneus: temperatura e desgaste das quatro rodas (FL FR / RL RR) e o composto.</summary>
public sealed class TyresWidget : IWidget
{
    public string Id => "tyres";
    public (float Width, float Height) DesignSize => (290, 192);

    static readonly string[] Names = ["FL", "FR", "RL", "RR"];
    const float ColCenter0 = 80, ColCenter1 = 200, RowTop0 = 42, RowTop1 = 116;

    WidgetSettings _cfg = new() { Id = "tyres" };
    public void Configure(WidgetSettings s) => _cfg = s;

    public void Draw(ThemeCanvas c, OverlayModel m)
    {
        var t = c.Theme;
        var (w, h) = DesignSize;
        c.Panel(0, 0, w, h);
        Chrome.Header(c, "TYRES", 14, 11, 70);

        var p = m.Session?.Player;
        if (!m.Connected || p is null || p.Wheels.Count < 4)
        {
            c.Text(m.Connected ? "NO DATA" : "WAITING", t.Label, 14, 60, 260, 30, t.LabelColor, shadow: t.TextShadow);
            return;
        }

        string compound = p.Wheels[0].Compound.Trim().ToUpperInvariant();
        if (compound.Length > 0) c.Text(compound, t.Label with { Size = 20 }, 190, 11, 90, 30, t.LabelColor, HAlign.Right, t.TextShadow);

        for (int i = 0; i < 4; i++)
        {
            var wh = p.Wheels[i];
            float cx = i % 2 == 0 ? ColCenter0 : ColCenter1;
            float y = i < 2 ? RowTop0 : RowTop1;
            c.Text(Names[i], t.Label with { Size = 17, Tracking = 4 }, cx - 40, y, 80, 20, t.TitleColor, HAlign.Center, t.TextShadow);
            bool temp = _cfg.ColumnVisible("temp"), wear = _cfg.ColumnVisible("wear");
            if (temp) DrawCentered(c, wh.TempC.ToString("0", CultureInfo.InvariantCulture), "°C", cx, y + 19, t.ValueColor);
            double wearPct = Math.Clamp(wh.Wear, 0, 1) * 100; // 0 = novo ... 1 = gasto (a confirmar em sessão real)
            if (wear) DrawCentered(c, wearPct.ToString("0", CultureInfo.InvariantCulture), "%", cx, y + (temp ? 45 : 19), wearPct >= 70 ? t.PlayerColor : t.LabelColor);
        }
    }

    static void DrawCentered(ThemeCanvas c, string value, string unit, float cx, float y, Vortice.Win32.Numerics.Color4 color)
    {
        var t = c.Theme;
        var uf = t.Label with { Size = 18 };
        float vw = c.Measure(value, t.Numbers) + t.Numbers.Tracking * value.Length, uw = c.Measure(unit, uf);
        Chrome.ValueUnit(c, value, unit, cx - (vw + 4 + uw) / 2, y, 24, color, false, uf);
    }
}
