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
    public (float Width, float Height) DesignSize => RightRows == 0 ? (250, 96) : (470 + Extra, Math.Max(96, 46 + RightRows * RowPitch + 8));
    const float RowPitch = 34;
    /// <summary>Largura extra da coluna de valores (perfil: % de 135, a celula do 2004).</summary>
    float Extra => MathF.Round(_cfg.Width("value", 135)) - 135;
    FuelUnit Unit => _cfg.Fmt.FuelOrDefault;
    string U => DisplayFormat.FuelLabel(Unit);
    string Vol(double liters, string format) => DisplayFormat.Fuel(liters, Unit).ToString(format, CultureInfo.InvariantCulture);

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
            Chrome.Notice(c, m.Connected ? "NO DATA" : "WAITING FOR AMS2", 96, 56);
            return;
        }

        if (t.Style == ThemeStyle.Broadcast2000s) { Draw2000s(c, t, f); return; }
        Chrome.ValueUnit(c, Vol(f.LitersLeft, "0.0"), U, 96, 46, 34, t.ReadoutColor, false);
        float vr = 448 + Extra;
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
            string use = f.PerLapAverage is { } a ? Vol(a, "0.00") : "-.--";
            Chrome.ValueUnit(c, use, U + "/LAP", vr, y, 34, t.ReadoutColor, true, t.Label with { Size = 20 });
            y += RowPitch;
        }
        if (_cfg.ColumnVisible("add"))
        {
            // Sem necessidade de reabastecer a linha continua (mostra "--"), para o painel nao mudar de tamanho com os dados.
            c.Text("ADD", t.Label, 235, y, 90, 34, t.LabelColor, shadow: t.TextShadow);
            string add = f.LitersToAdd is { } v && v > 0.05 ? Vol(v, "0.0") : "--";
            Chrome.ValueUnit(c, add, U, vr, y, 34, t.ValueColor, true);
        }
    }

    /// <summary>2004–2008: litros numa célula preta; LAPS/USE/ADD como rótulo branco + valor preto.</summary>
    void Draw2000s(ThemeCanvas c, Theme.Theme t, Ams2.Core.Calc.FuelEstimate f)
    {
        Chrome.BlackCell(c, 96, 46, 124, 34, Vol(f.LitersLeft, "0.0") + " " + U, t.Numbers, HAlign.Center);
        var small = t.Numbers with { Size = 22 };
        float vw = 135 + Extra;
        float y = 46;
        if (_cfg.ColumnVisible("laps"))
        {
            Chrome.WhiteCell(c, 235, y, 86, 32, "LAPS", t.Label);
            Chrome.BlackCell(c, 321, y, vw, 32, f.LapsRemainingOnFuel is { } l ? Math.Floor(l).ToString("0", CultureInfo.InvariantCulture) : "--", t.Numbers);
            y += RowPitch;
        }
        if (_cfg.ColumnVisible("use"))
        {
            Chrome.WhiteCell(c, 235, y, 86, 32, "USE", t.Label);
            Chrome.BlackCell(c, 321, y, vw, 32, (f.PerLapAverage is { } a ? Vol(a, "0.00") : "-.--") + " " + U + "/LAP", small);
            y += RowPitch;
        }
        if (_cfg.ColumnVisible("add"))
        {
            Chrome.WhiteCell(c, 235, y, 86, 32, "ADD", t.Label);
            Chrome.BlackCell(c, 321, y, vw, 32, (f.LitersToAdd is { } v && v > 0.05 ? Vol(v, "0.0") : "--") + " " + U, t.Numbers);
        }
    }

    /// <summary>Bomba de combustível em tinta de destaque (corpo, visor, base e mangueira), sem depender de glifo.</summary>
    static void DrawPump(ThemeCanvas c, Theme.Theme t, float x, float y)
    {
        var fill = t.AccentFill;
        c.FillRect(x + 4, y + 2, 30, 46, fill);                 // corpo
        c.FillRect(x + 9, y + 7, 20, 14, t.ValueCellFill.A > 0f ? t.ValueCellFill : t.PanelFill);          // visor
        c.FillRect(x, y + 48, 38, 6, fill);                     // base
        c.FillRect(x + 36, y + 10, 7, 4, fill);                 // mangueira: sai do corpo...
        c.FillRect(x + 40, y + 10, 4, 28, fill);                // ...desce...
        c.FillRect(x + 36, y + 36, 8, 4, fill);                 // ...e termina no bico
    }
}
