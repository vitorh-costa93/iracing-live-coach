using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Theme;
using Ams2.Shared.Profiles;

namespace Ams2.OverlayHost.Widgets;

/// <summary>Cronometro de box do jogador: [nome em celula branca][tempo parado em celula preta, "3.0"]. Aparece enquanto parado (PitState InPit) e ~4 s depois (coluna "always" = fixo).</summary>
public sealed class PitTimerWidget : IWidget
{
    public string Id => "pittimer";
    public (float Width, float Height) DesignSize => (X0 * 2 + (_b98 ? NameW98 : NameW) + TimeW, Y0 * 2 + RowH + 2);
    bool _b98;
    public void UseTheme(Theme.Theme theme) => _b98 = theme.Style == ThemeStyle.Broadcast98;
    WidgetSettings _cfg = new() { Id = "pittimer" };
    public void Configure(WidgetSettings s) => _cfg = s;

    const float X0 = 4, Y0 = 4, NameW = 190, NameW98 = 256, TimeW = 96, RowH = 32;

    public void Draw(ThemeCanvas c, OverlayModel m)
    {
        if (!m.Connected || m.Session is not { PlayerCar: { } car } s) return;
        var b = BroadcastUi.State(m);
        float alpha = _cfg.ColumnVisible("always") || b.PlayerStopped ? 1f : BroadcastUi.Fade(m.Now - b.PlayerStopEndT, BroadcastUi.PitTimerHold);
        string name = BroadcastUi.ShortName(car, s.Cars), time = BroadcastUi.StopTime(b.PlayerStopNow(m.Now));
        var t = c.Theme;
        BroadcastUi.WithAlpha(c, alpha, () =>
        {
            if (t.Style == ThemeStyle.Broadcast2000s)
            {
                Chrome.WhiteCell(c, X0, Y0, NameW, RowH, name, t.Text);
                Chrome.BlackCell(c, X0 + NameW, Y0, TimeW, RowH, time, t.Numbers, HAlign.Right);
                return;
            }
            var (w, h) = DesignSize;
            c.Panel(0, 0, w, h);
            if (t.Style == ThemeStyle.Broadcast98)
            {
                // Faixa translucida: bolha ciana com o numero do carro, nome em caixa-alta e tempo parado em amarelo.
                Chrome.Bubble(c, 14, Y0 + 3, 50, RowH - 6, CaptionPlate.CarNumber(car), t.Numbers with { Size = 24, Tracking = 1f });
                string nm = name.ToUpperInvariant();
                c.Text(nm, BroadcastUi.Fit(c, nm, t.Text, NameW98 - 84), 76, Y0 - 1, NameW98 - 80, RowH, t.TextColor, shadow: t.TextShadow);
                c.Text(time, t.Numbers, X0 + NameW98, Y0, TimeW - 12, RowH, t.ValueColor, HAlign.Right, t.ValueShadow);
                return;
            }
            c.Text(name, t.Text, 16, Y0, NameW - 12, RowH, t.TextColor, shadow: t.TextShadow);
            c.Text(time, t.Numbers, X0 + NameW, Y0, TimeW - 12, RowH, t.ValueColor, HAlign.Right, t.ValueShadow);
        });
    }
}
