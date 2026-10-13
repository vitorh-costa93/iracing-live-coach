using System.Globalization;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Theme;
using Ams2.Shared.Profiles;

namespace Ams2.OverlayHost.Widgets;

/// <summary>
/// Contador de voltas "N/56" no topo-centro. 2004–2008: voltas restantes do líder / total, caixa branca com texto escuro, como na
/// transmissão; nos outros temas, painel do tema com o número. Corrida por tempo (LapsInEvent = 0): "Lap N".
/// </summary>
public sealed class LapCounterWidget : IWidget
{
    public string Id => "lapcounter";
    public (float Width, float Height) DesignSize => _b18 ? (168, 82) : _b12 ? (170, 40) : (132, 40);
    bool _b18, _b12;
    public void UseTheme(Theme.Theme theme) { _b18 = theme.Style == ThemeStyle.Modern2018; _b12 = theme.Style == ThemeStyle.Broadcast2000s && theme.CellSlant > 0; }
    public void Configure(WidgetSettings s) { }

    public void Draw(ThemeCanvas c, OverlayModel m)
    {
        var t = c.Theme;
        var (w, h) = DesignSize;
        string text = Format(m, countdown: t.Style == ThemeStyle.Broadcast2000s && !_b12);
        if (t.Style == ThemeStyle.Modern2018)
        {
            // Cabeçalho da torre 2018: topo arredondado, "LAP" largo com tracking, filete fino, "n / N" regular; filete vermelho embaixo.
            c.FillRoundRect(0, 0, w, h - 4, 7, t.PanelFill);
            c.FillRect(0, h - 12, w, 8, t.PanelFill);
            c.Text("LAP", t.Title with { Size = 26, Tracking = 4 }, 0, 4, w + 4, 34, t.TitleColor, HAlign.Center);
            c.FillRect(34, 40, w - 68, 1.2f, new Vortice.Win32.Numerics.Color4(1f, 1f, 1f, 0.45f));
            string n = text.StartsWith("--") ? "- / -" : text.StartsWith("Lap ") ? text[4..] : text.Replace("/", " / ");
            c.Text(n, t.Numbers with { Size = 26 }, 0, 42, w, 32, t.ValueColor, HAlign.Center);
            c.FillRect(0, h - 4, w, 4, t.AccentBar);
            return;
        }
        if (_b12)
        {
            // 2012 ("Lap  41 / 56"): pilula escura inclinada com "Lap" em cinza e "atual / total" em branco; sob bandeira amarela o numero fica em amarelo.
            string n12 = Format(m, countdown: false);
            n12 = n12.StartsWith("Lap ") ? n12[4..] : n12.Replace("/", " / ");
            bool yellow = m.Connected && m.Session is { } ys && ys.FlagColour is 6 or 7;
            var label = t.Label with { Weight = 500 };
            Chrome.Box(c, 4, 3, 66, h - 8, "Lap", label, Chrome.CellKind.Black, HAlign.Center, 0, new Vortice.Win32.Numerics.Color4(0.78f, 0.80f, 0.86f, 1f));
            Chrome.Box(c, 62, 3, w - 66, h - 8, n12, t.Numbers, yellow ? Chrome.CellKind.Yellow : Chrome.CellKind.Black, HAlign.Center, 0);
            return;
        }
        if (t.Style == ThemeStyle.Broadcast2000s)
        {
            Chrome.WhiteCell(c, 4, 3, w - 8, h - 8, text, t.Numbers, HAlign.Center);
            return;
        }
        c.Panel(0, 0, w, h);
        c.Text(text, t.Numbers, 0, 2, w, h - 4, t.NumberColor, HAlign.Center, t.ValueShadow);
    }

    public static string Format(OverlayModel m, bool countdown = false)
    {
        if (!m.Connected || m.Session is not { } s) return "-- /--";
        var leader = s.Cars.Where(x => x.Position > 0).OrderBy(x => x.Position).FirstOrDefault() ?? s.PlayerCar;
        if (leader is null) return "-- /--";
        int lap = Math.Max(leader.CurrentLap, 1);
        if (s.LapsInEvent > 0)
        {
            int shown = countdown ? Math.Clamp(s.LapsInEvent - leader.LapsCompleted, 0, s.LapsInEvent) : Math.Min(lap, s.LapsInEvent);
            return shown.ToString(CultureInfo.InvariantCulture) + "/" + s.LapsInEvent.ToString(CultureInfo.InvariantCulture);
        }
        return "Lap " + lap.ToString(CultureInfo.InvariantCulture);
    }
}
