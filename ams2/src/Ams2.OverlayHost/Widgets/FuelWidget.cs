using System.Globalization;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Theme;

namespace Ams2.OverlayHost.Widgets;

/// <summary>Combustível: litros no tanque, voltas restantes com o combustível e consumo médio por volta.</summary>
public sealed class FuelWidget : IWidget
{
    public string Id => "fuel";
    public (float Width, float Height) DesignSize => (470, 156);

    public void Draw(ThemeCanvas c, OverlayModel m)
    {
        var t = c.Theme;
        var (w, h) = DesignSize;
        c.Panel(0, 0, w, h);
        Chrome.Header(c, "FUEL", 96, 11, 160);
        DrawPump(c, t, 22, 22);

        var f = m.Fuel;
        if (!m.Connected || f is null || m.Session?.Player is null)
        {
            c.Text(m.Connected ? "NO DATA" : "WAITING FOR AMS2", t.Label, 96, 56, 360, 30, t.LabelColor, shadow: t.TextShadow);
            return;
        }

        Chrome.ValueUnit(c, f.LitersLeft.ToString("0.0", CultureInfo.InvariantCulture), "L", 96, 46, 34, t.LabelColor, false);
        c.Text("LAPS", t.Label, 235, 46, 90, 34, t.LabelColor, shadow: t.TextShadow);
        string laps = f.LapsRemainingOnFuel is { } l ? Math.Floor(l).ToString("0", CultureInfo.InvariantCulture) : "--";
        c.Text(laps, t.Numbers, 330, 46, 100, 34, t.LabelColor, shadow: t.ValueShadow);
        c.Text("USE", t.Label, 235, 80, 90, 34, t.LabelColor, shadow: t.TextShadow);
        string use = f.PerLapAverage is { } a ? a.ToString("0.00", CultureInfo.InvariantCulture) : "-.--";
        Chrome.ValueUnit(c, use, "L/LAP", 448, 80, 34, t.LabelColor, true, t.Label with { Size = 20 });
        if (f.LitersToAdd is { } add && add > 0.05)
        {
            c.Text("ADD", t.Label, 235, 114, 90, 34, t.LabelColor, shadow: t.TextShadow);
            Chrome.ValueUnit(c, add.ToString("0.0", CultureInfo.InvariantCulture), "L", 448, 114, 34, t.ValueColor, true);
        }
    }

    /// <summary>Bomba de combustível em tinta de destaque (corpo, visor, base e mangueira), sem depender de glifo.</summary>
    static void DrawPump(ThemeCanvas c, Theme.Theme t, float x, float y)
    {
        var fill = t.AccentFill;
        c.FillRect(x + 4, y + 2, 30, 46, fill);                 // corpo
        c.FillRect(x + 9, y + 7, 20, 14, t.PanelFill);          // visor
        c.FillRect(x, y + 48, 38, 6, fill);                     // base
        c.FillRect(x + 36, y + 10, 7, 4, fill);                 // mangueira: sai do corpo...
        c.FillRect(x + 40, y + 10, 4, 28, fill);                // ...desce...
        c.FillRect(x + 36, y + 36, 8, 4, fill);                 // ...e termina no bico
    }
}
