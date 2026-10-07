using System.Globalization;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Theme;
using Ams2.Shared.Profiles;

namespace Ams2.OverlayHost.Widgets;

/// <summary>Pneus: temperatura e desgaste das quatro rodas (FL FR / RL RR) e o composto.</summary>
public sealed class TyresWidget : IWidget
{
    public string Id => "tyres";
    // Cada roda = nome + (temperatura e/ou desgaste). Ocultar uma linha encurta as celulas e o painel.
    int Lines => (_cfg.ColumnVisible("temp") ? 1 : 0) + (_cfg.ColumnVisible("wear") ? 1 : 0);
    float RowPitch => Lines == 1 ? 60 : 22 + 26 * Lines;   // 74 com as duas linhas (mockup); 1 linha precisa de folga extra entre as rodas
    public (float Width, float Height) DesignSize => (290, RowTop0 + 2 * RowPitch + 2);

    static readonly string[] Names = ["FL", "FR", "RL", "RR"];
    const float ColCenter0 = 80, ColCenter1 = 200, RowTop0 = 42;

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
            Chrome.Notice(c, m.Connected ? "NO DATA" : "WAITING", 14, 60, 260);
            return;
        }

        string compound = _cfg.ColumnVisible("compound") ? p.Wheels[0].Compound.Trim().ToUpperInvariant() : "";
        bool b04 = t.Style == ThemeStyle.Broadcast2000s;
        if (compound.Length > 0 && b04) Chrome.Caption(c, 280 - c.Measure(compound, t.Label with { Size = 18 }) - 18, 13, compound, 24, t.Label with { Size = 18 }, Chrome.CellKind.Navy);
        else if (compound.Length > 0) c.Text(compound, t.Label with { Size = 20 }, 190, 11, 90, 30, t.LabelColor, HAlign.Right, t.TextShadow);

        for (int i = 0; i < 4; i++)
        {
            var wh = p.Wheels[i];
            float cx = i % 2 == 0 ? ColCenter0 : ColCenter1;
            float y = RowTop0 + (i < 2 ? 0 : RowPitch);
            if (b04) { Draw2000s(c, t, wh, i, cx, y); continue; }
            c.Text(Names[i], t.Label with { Size = 17, Tracking = 4 }, cx - 40, y, 80, 20, t.TitleColor, HAlign.Center, t.TextShadow);
            bool temp = _cfg.ColumnVisible("temp"), wear = _cfg.ColumnVisible("wear");
            if (temp) DrawCentered(c, Temp(wh.TempC), DisplayFormat.TempLabel(_cfg.Fmt.TempOrDefault), cx, y + 19, t.ValueColor);
            double wearPct = Math.Clamp(wh.Wear, 0, 1) * 100; // 0 = novo ... 1 = gasto (a confirmar em sessão real)
            if (wear) DrawCentered(c, wearPct.ToString("0", CultureInfo.InvariantCulture), "%", cx, y + (temp ? 45 : 19), wearPct >= 70 ? t.PlayerColor : t.ReadoutColor);
        }
    }

    /// <summary>2004–2008: nome da roda em célula branca sobre a célula preta com temperatura e/ou desgaste.</summary>
    void Draw2000s(ThemeCanvas c, Theme.Theme t, Ams2.Core.WheelSnapshot wh, int i, float cx, float y)
    {
        bool temp = _cfg.ColumnVisible("temp"), wear = _cfg.ColumnVisible("wear");
        Chrome.WhiteCell(c, cx - 50, y, 100, 22, Names[i], t.Label with { Size = 17 }, HAlign.Center);
        var f = t.Numbers with { Size = 21 };
        double wearPct = Math.Clamp(wh.Wear, 0, 1) * 100;
        float yy = y + 22;
        if (temp) { Chrome.BlackCell(c, cx - 50, yy, 100, 24, Temp(wh.TempC) + " " + DisplayFormat.TempLabel(_cfg.Fmt.TempOrDefault), f, HAlign.Center); yy += 24; }
        if (wear) Chrome.BlackCell(c, cx - 50, yy, 100, 24, wearPct.ToString("0", CultureInfo.InvariantCulture) + " %", f, HAlign.Center, wearPct >= 70 ? t.PlayerColor : null);
    }

    string Temp(double celsius) => DisplayFormat.Temp(celsius, _cfg.Fmt.TempOrDefault).ToString("0", CultureInfo.InvariantCulture);

    static void DrawCentered(ThemeCanvas c, string value, string unit, float cx, float y, Vortice.Win32.Numerics.Color4 color)
    {
        var t = c.Theme;
        var uf = t.Label with { Size = 18 };
        float vw = c.Measure(value, t.Numbers) + t.Numbers.Tracking * value.Length, uw = c.Measure(unit, uf);
        Chrome.ValueUnit(c, value, unit, cx - (vw + 4 + uw) / 2, y, 24, color, false, uf);
    }
}
