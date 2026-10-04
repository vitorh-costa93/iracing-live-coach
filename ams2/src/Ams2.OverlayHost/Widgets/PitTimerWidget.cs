using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Theme;
using Ams2.Shared.Profiles;

namespace Ams2.OverlayHost.Widgets;

/// <summary>Cronometro de box do jogador: [nome em celula branca][tempo parado em celula preta, "3.0"]. Aparece enquanto parado (PitState InPit) e ~4 s depois (coluna "always" = fixo).</summary>
public sealed class PitTimerWidget : IWidget
{
    public string Id => "pittimer";
    public (float Width, float Height) DesignSize => _b18 ? (NameW + 110, Head18 + Gap18 + Strip18 + Body18) : (X0 * 2 + (_b98 ? NameW98 : NameW) + TimeW, Y0 * 2 + RowH + 2);
    bool _b98, _b18;
    public void UseTheme(Theme.Theme theme) { _b98 = theme.Style == ThemeStyle.Broadcast98; _b18 = theme.Style == ThemeStyle.Modern2018; }
    const float Head18 = 38, Gap18 = 4, Strip18 = 44, Body18 = 96;
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
            if (t.Style == ThemeStyle.Modern2018)
            {
                string? lane = ShowPitTime18 && b.PlayerInPitLane ? BroadcastUi.StopTime(b.PlayerPitLaneNow(m.Now)) : null;
                Draw18(c, t, car, s.Cars, name, time, lane, w);
                return;
            }
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

    // Opcoes do tema 2018 (WidgetCatalog.OptionsFor("f1-2018", "pittimer")); padrao = ligado (nao gravado).
    bool Opt18(string id) => !string.Equals(_cfg.OptionOr(id, "true"), "false", StringComparison.OrdinalIgnoreCase);
    bool ShowPitTime18 => Opt18("showPitTime");
    bool ShowPosition18 => Opt18("showPosition");
    bool ShowTick18 => Opt18("showTick");

    /// <summary>
    /// Gráfico "PIT LANE" 2018 (ref. f1-2018-pitlane-stoptime.jpg): cabeçalho preto, faixa preta com caixa de posição branca + tique +
    /// SOBRENOME em negrito, corpo cinza-azulado (canto inferior direito arredondado) com "STOP TIME" ciano e o tempo grande em ciano
    /// entre colchetes de canto. Na pit lane (opção showPitTime) o rótulo vira "PIT" + tempo na pit lane em branco.
    /// O AMS2 não dá a cor da equipe: o tique usa a cor da classe.
    /// </summary>
    void Draw18(ThemeCanvas c, Theme.Theme t, Ams2.Core.CarSnapshot car, IReadOnlyList<Ams2.Core.CarSnapshot> field, string name, string time, string? pitLane, float w)
    {
        var black = new Vortice.Win32.Numerics.Color4(0f, 0f, 0f, 0.92f);
        c.FillRect(0, 0, w, Head18, black);
        c.Text("PIT LANE", t.Title with { Weight = 400, Size = 22, Tracking = 1f }, 0, 1, w, Head18, t.TitleColor, HAlign.Center);

        float sy = Head18 + Gap18, x = 6;
        c.FillRect(0, sy, w, Strip18, black);
        if (ShowPosition18)
        {
            Chrome.PosBox(c, x, sy + 5, 36, Strip18 - 10, car.Position.ToString(System.Globalization.CultureInfo.InvariantCulture),
                t.Numbers with { Size = 22 });
            x += 36 + 10;
        }
        if (ShowTick18)
        {
            int ci = Math.Max(0, field.Select(f => f.ClassName).Distinct().ToList().IndexOf(car.ClassName));
            Chrome.Tick(c, x, sy + 10, Strip18 - 20, Chrome.ClassTick(ci), 5);
            x += 5 + 10;
        }
        else if (!ShowPosition18) x = 12;
        string up = name.ToUpperInvariant();
        var nf = t.Text with { Weight = 700, Size = 22 };
        c.Text(up, BroadcastUi.Fit(c, up, nf, w - x - 10), x, sy, w - x - 8, Strip18, t.TextColor);

        float by = sy + Strip18;
        // Corpo num poligono so (sem sobrepor a tinta translucida): so o canto inferior direito arredondado, como na TV.
        const float r = 12;
        Span<System.Numerics.Vector2> pts = stackalloc System.Numerics.Vector2[3 + 9 + 1];
        pts[0] = new(0, by); pts[1] = new(w, by); pts[2] = new(w, by + Body18 - r);
        for (int i = 1; i <= 9; i++)
        {
            float a = MathF.PI / 2 * i / 9;
            pts[2 + i] = new(w - r + r * MathF.Cos(a), by + Body18 - r + r * MathF.Sin(a));
        }
        pts[12] = new(0, by + Body18);
        c.FillPolygon(pts, t.SubPanelFill);
        float lw = MathF.Round(w * 0.46f);
        if (pitLane is not null)
        {
            var white = new Vortice.Win32.Numerics.Color4(1f, 1f, 1f, 1f);
            c.Text("PIT", t.Label with { Weight = 400, Size = 21 }, 4, by + 10, lw, 26, white, HAlign.Center);
            c.Text(pitLane, BroadcastUi.Fit(c, pitLane, t.Numbers with { Weight = 400, Size = 34 }, lw - 12), 4, by + 34, lw, 46, white, HAlign.Center);
        }
        else
        {
            var lf = t.Label with { Weight = 700, Size = 23 };
            c.Text("STOP", lf, 4, by + 16, lw, 30, t.PitTimeColor, HAlign.Center);
            c.Text("TIME", lf, 4, by + 46, lw, 30, t.PitTimeColor, HAlign.Center);
        }
        float bx = lw + 8, bw = w - bx - 16, bt = by + 12, bh = Body18 - 24;
        Chrome.CornerBrackets(c, bx, bt, bw, bh, t.PitTimeColor, 11, 3);
        c.Text(time, BroadcastUi.Fit(c, time, t.Numbers with { Weight = 400, Size = 46 }, bw - 16), bx, bt, bw, bh, t.PitTimeColor, HAlign.Center);
    }
}
