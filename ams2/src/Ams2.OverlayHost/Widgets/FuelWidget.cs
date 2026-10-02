using System.Globalization;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Theme;
using Ams2.Shared.Profiles;

namespace Ams2.OverlayHost.Widgets;

/// <summary>Combustível: litros no tanque, voltas restantes com o combustível e consumo médio por volta.</summary>
public sealed class FuelWidget : IWidget
{
    public string Id => "fuel";
    // Linhas da coluna da direita (LAPS/USE/ADD): as visiveis sobem para ocupar o lugar das ocultas.
    int RightRows => (_cfg.ColumnVisible("laps") ? 1 : 0) + (_cfg.ColumnVisible("use") ? 1 : 0) + (_cfg.ColumnVisible("add") ? 1 : 0);
    public (float Width, float Height) DesignSize => RightRows == 0 ? (250, 96) : (470, Math.Max(96, 46 + RightRows * RowPitch + 8));
    const float RowPitch = 34;

    WidgetSettings _cfg = new() { Id = "fuel" };
    public void Configure(WidgetSettings s) => _cfg = s;

    public void Draw(ThemeCanvas c, OverlayModel m)
    {
        var t = c.Theme;
        var (w, h) = DesignSize;
        c.Panel(0, 0, w, h);
        Chrome.Header(c, "FUEL", 96, 11, 160, maxRight: w - 20);
        DrawPump(c, t, 22, 22);

        var f = m.Fuel;
        if (!m.Connected || f is null || m.Session?.Player is null)
        {
            c.Text(m.Connected ? "NO DATA" : "WAITING FOR AMS2", t.Label, 96, 56, 360, 30, t.LabelColor, shadow: t.TextShadow);
            return;
        }

        Chrome.ValueUnit(c, f.LitersLeft.ToString("0.0", CultureInfo.InvariantCulture), "L", 96, 46, 34, t.ReadoutColor, false);
        float y = 46;
        if (_cfg.ColumnVisible("laps"))
        {
            c.Text("LAPS", t.Label, 235, y, 90, 34, t.LabelColor, shadow: t.TextShadow);
            string laps = f.LapsRemainingOnFuel is { } l ? Math.Floor(l).ToString("0", CultureInfo.InvariantCulture) : "--";
            c.Text(laps, t.Numbers, 330, y, 100, 34, t.ReadoutColor, shadow: t.ValueShadow);
            y += RowPitch;
        }
        if (_cfg.ColumnVisible("use"))
        {
            c.Text("USE", t.Label, 235, y, 90, 34, t.LabelColor, shadow: t.TextShadow);
            string use = f.PerLapAverage is { } a ? a.ToString("0.00", CultureInfo.InvariantCulture) : "-.--";
            Chrome.ValueUnit(c, use, "L/LAP", 448, y, 34, t.ReadoutColor, true, t.Label with { Size = 20 });
            y += RowPitch;
        }
        if (_cfg.ColumnVisible("add"))
        {
            // Sem necessidade de reabastecer a linha continua (mostra "--"), para o painel nao mudar de tamanho com os dados.
            c.Text("ADD", t.Label, 235, y, 90, 34, t.LabelColor, shadow: t.TextShadow);
            string add = f.LitersToAdd is { } v && v > 0.05 ? v.ToString("0.0", CultureInfo.InvariantCulture) : "--";
            Chrome.ValueUnit(c, add, "L", 448, y, 34, t.ValueColor, true);
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
