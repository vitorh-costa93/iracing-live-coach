using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Theme;
using Ams2.Shared.Profiles;

namespace Ams2.OverlayHost.Widgets;

/// <summary>Cronometro de box do jogador: [nome em celula branca][tempo parado em celula preta, "3.0"]. Aparece enquanto parado (PitState InPit) e ~4 s depois (coluna "always" = fixo).</summary>
public sealed class PitTimerWidget : IWidget
{
    public string Id => "pittimer";
    public (float Width, float Height) DesignSize => _b18 ? (NameW + 110, Head18 + Strip18 + Body18) : (X0 * 2 + (_b98 ? NameW98 : NameW) + TimeW, Y0 * 2 + RowH + 2);
    bool _b98, _b18;
    public void UseTheme(Theme.Theme theme) { _b98 = theme.Style == ThemeStyle.Broadcast98; _b18 = theme.Style == ThemeStyle.Modern2018; }
    const float Head18 = 36, Strip18 = 42, Body18 = 80;
    WidgetSettings _cfg = new() { Id = "pittimer" };
    public void Configure(WidgetSettings s) => _cfg = s;

    const float X0 = 4, Y0 = 4, TimeW = 96, RowH = 32;
    float NameW => MathF.Round(_cfg.Width("name", 190));
    float NameW98 => MathF.Round(_cfg.Width("name", 256));

    public void Draw(ThemeCanvas c, OverlayModel m)
    {
        if (!m.Connected || m.Session is not { PlayerCar: { } car } s) return;
        var b = BroadcastUi.State(m);
        float alpha = _cfg.ColumnVisible("always") || b.PlayerStopped ? 1f : BroadcastUi.Fade(m.Now - b.PlayerStopEndT, BroadcastUi.PitTimerHold);
        string name = _cfg.Name(car, BroadcastUi.ShortName(car, s.Cars)), time = BroadcastUi.StopTime(b.PlayerStopNow(m.Now));
        var t = c.Theme;
        BroadcastUi.WithAlpha(c, alpha, () =>
        {
            if (t.Style == ThemeStyle.Broadcast2000s)
            {
                Chrome.WhiteCell(c, X0, Y0, NameW, RowH, name, BroadcastUi.Fit(c, name, t.Text, NameW - 16));
                Chrome.BlackCell(c, X0 + NameW, Y0, TimeW, RowH, time, t.Numbers, HAlign.Right);
                return;
            }
            var (w, h) = DesignSize;
            if (t.Style == ThemeStyle.Modern2018) { Draw18(c, t, car, name, time, w, h); return; }
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
            c.Text(name, BroadcastUi.Fit(c, name, t.Text, NameW - 12), 16, Y0, NameW - 12, RowH, t.TextColor, shadow: t.TextShadow);
            c.Text(time, t.Numbers, X0 + NameW, Y0, TimeW - 12, RowH, t.ValueColor, HAlign.Right, t.ValueShadow);
        });
    }

    /// <summary>
    /// Gráfico "PIT LANE" 2018 (ref. f1-2018-pitlane-stoptime.jpg): cabeçalho preto, faixa preta com caixa de posição branca + tique +
    /// SOBRENOME em negrito, corpo cinza-azulado com "STOP TIME" ciano e o tempo grande em ciano entre colchetes de canto.
    /// O AMS2 não dá a cor da equipe: o tique usa o vermelho do tema.
    /// </summary>
    static void Draw18(ThemeCanvas c, Theme.Theme t, Ams2.Core.CarSnapshot car, string name, string time, float w, float h)
    {
        c.FillRoundRect(0, 0, w, Head18 + 6, 6, t.PanelFill);
        c.Text("PIT LANE", t.Title with { Weight = 400, Size = 21, Tracking = 1f }, 0, 1, w, Head18, t.TitleColor, HAlign.Center);
        float sy = Head18;
        c.FillRect(0, sy, w, Strip18, new Vortice.Win32.Numerics.Color4(0f, 0f, 0f, 0.92f));
        Chrome.PosBox(c, 8, sy + 5, 32, Strip18 - 10, car.Position.ToString(System.Globalization.CultureInfo.InvariantCulture), t.Numbers with { Size = 20 });
        Chrome.Tick(c, 50, sy + 9, Strip18 - 18, t.AccentBar);
        string up = name.ToUpperInvariant();
        c.Text(up, BroadcastUi.Fit(c, up, t.Text, w - 74), 62, sy, w - 66, Strip18, t.TextColor);
        float by = sy + Strip18;
        c.FillRoundRect(0, by, w, Body18, 8, t.SubPanelFill);
        c.FillRect(0, by, w, 10, t.SubPanelFill);
        var lf = t.Label with { Weight = 700, Size = 19 };
        c.Text("STOP", lf, 8, by + 12, 96, 26, t.PitTimeColor, HAlign.Center);
        c.Text("TIME", lf, 8, by + 38, 96, 26, t.PitTimeColor, HAlign.Center);
        float bx = 112, bw = w - bx - 12;
        Chrome.CornerBrackets(c, bx, by + 10, bw, Body18 - 20, t.PitTimeColor);
        c.Text(time, BroadcastUi.Fit(c, time, t.Numbers with { Size = 40 }, bw - 16), bx, by + 8, bw, Body18 - 16, t.PitTimeColor, HAlign.Center);
    }}
