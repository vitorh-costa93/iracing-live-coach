using System.Globalization;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Theme;
using Ams2.Shared.Profiles;

namespace Ams2.OverlayHost.Widgets;

/// <summary>
/// Contador de voltas "N/56" (volta do líder / total) no topo-centro. 2004–2008: caixa branca com texto escuro, como na
/// transmissão; nos outros temas, painel do tema com o número. Corrida por tempo (LapsInEvent = 0): "Lap N".
/// </summary>
public sealed class LapCounterWidget : IWidget
{
    public string Id => "lapcounter";
    public (float Width, float Height) DesignSize => (132, 40);
    public void Configure(WidgetSettings s) { }

    public void Draw(ThemeCanvas c, OverlayModel m)
    {
        var t = c.Theme;
        var (w, h) = DesignSize;
        string text = Format(m);
        if (t.Style == ThemeStyle.Broadcast2000s)
        {
            Chrome.WhiteCell(c, 4, 3, w - 8, h - 8, text, t.Numbers, HAlign.Center);
            return;
        }
        c.Panel(0, 0, w, h);
        c.Text(text, t.Numbers, 0, 2, w, h - 4, t.NumberColor, HAlign.Center, t.ValueShadow);
    }

    public static string Format(OverlayModel m)
    {
        if (!m.Connected || m.Session is not { } s) return "-- /--";
        var leader = s.Cars.Where(x => x.Position > 0).OrderBy(x => x.Position).FirstOrDefault() ?? s.PlayerCar;
        if (leader is null) return "-- /--";
        int lap = Math.Max(leader.CurrentLap, 1);
        if (s.LapsInEvent > 0) return Math.Min(lap, s.LapsInEvent).ToString(CultureInfo.InvariantCulture) + "/" + s.LapsInEvent.ToString(CultureInfo.InvariantCulture);
        return "Lap " + lap.ToString(CultureInfo.InvariantCulture);
    }
}
