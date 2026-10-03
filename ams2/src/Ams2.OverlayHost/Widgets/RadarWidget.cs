using Ams2.Core.Calc;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Theme;
using Ams2.Shared.Profiles;
using Vortice.Win32.Numerics;

namespace Ams2.OverlayHost.Widgets;

/// <summary>
/// Radar de proximidade (replica do Radar do V3, ver ams2/reference/radar-notes.md): o jogador no centro de um painel translucido de 120 x 190 dip,
/// os carros em volta na posicao REAL (frente/lado em metros, vinda de WorldPosition + yaw), cor por proximidade (cinza / ambar ate 12 m / vermelho ao lado ate 7 m)
/// e barras vermelhas nas bordas quando ha carro ao lado. Le so <see cref="OverlayModel.Radar"/>. Alta frequencia: redesenha a cada vblank e
/// extrapola a posicao entre dois passos de 60 Hz do provider pela velocidade relativa. Sem alocacao por quadro.
/// Aparece so com carro dentro do alcance (0,6 s de permanencia + esmaecimento) ou sempre (coluna "always"); nunca fora de sessao ou sozinho na pista.
/// </summary>
public sealed class RadarWidget : IWidget
{
    public string Id => "radar";
    public bool HighFrequency => true;
    public (float Width, float Height) DesignSize => (W, H);
    public void UseTheme(Theme.Theme theme) { }
    WidgetSettings _cfg = new() { Id = "radar" };
    public void Configure(WidgetSettings s) => _cfg = s;

    const float W = 120, H = 190;
    /// <summary>Escala lateral fixa: 8 dip por metro (carro de 2 m = 16 dip, como no V3; faixa do V3 a 30 dip = 3,75 m; meia-largura util 7,5 m).</summary>
    const float LateralDipPerMeter = 8f;
    const float EdgePad = 10f;
    const double HoldSeconds = 0.6, FadeSeconds = 0.3, MaxExtrapolateSeconds = 0.05;
    static readonly Color4 Warn = new(1f, 0.8f, 0f, 1f);

    double _lastNear = double.NegativeInfinity;
    int _labelRange = -1;
    string _label = "";

    /// <summary>Opcoes do tracker para as configuracoes do widget (alcance em metros, sensibilidade 1..5).</summary>
    public static RadarOptions OptionsFor(WidgetSettings s) => new()
    {
        RangeMeters = s.EffectiveRadarRange,
        Sensitivity = WidgetCatalog.RadarSensitivityFactor(s.EffectiveRadarSensitivity),
    };

    static double Clock(OverlayModel m) => m.Inputs?.Now ?? m.Now;

    /// <summary>Visibilidade e opacidade (0 = nada a desenhar). Estado (instante do ultimo carro proximo) so e gravado quando <paramref name="commit"/>.</summary>
    float Alpha(OverlayModel m, bool commit)
    {
        if (!m.Connected || m.Radar is not { Valid: true } f) { if (commit) _lastNear = double.NegativeInfinity; return 0; }
        if (_cfg.ColumnVisible("always")) return 1;
        double now = Clock(m);
        double last = _lastNear;
        if (f.AnyNear) { last = now; if (commit) _lastNear = now; }
        double since = now - last;
        if (since <= HoldSeconds) return 1;
        return since >= HoldSeconds + FadeSeconds ? 0 : (float)(1 - (since - HoldSeconds) / FadeSeconds);
    }

    /// <summary>Sem nada para desenhar: o host nao precisa redesenhar no ritmo do monitor.</summary>
    public bool IsIdle(OverlayModel m) => Alpha(m, commit: false) <= 0f;

    readonly record struct Look(Color4 Fill, Color4 Border, float BorderWidth, float Radius, float CarRadius, Color4 Guide, Color4 Player, Color4 Other, Color4 Alert, Color4 Accent, bool TopAccent);

    static Look LookFor(Theme.Theme t) => t.Style switch
    {
        ThemeStyle.Broadcast2000s => new Look(new(0f, 0f, 0f, 0.42f), new(1f, 1f, 1f, 0.35f), 1f, 0f, 1.5f, new(1f, 1f, 1f, 0.22f),
            Player: new(1f, 1f, 1f, 1f), Other: new(0.62f, 0.62f, 0.68f, 1f), Alert: t.BrakeColor, Accent: t.AccentBar, TopAccent: true),
        ThemeStyle.Modern2010s => new Look(new(12 / 255f, 20 / 255f, 35 / 255f, 0.80f), t.PanelBorder, t.BorderWidth, t.CornerRadius, 3.5f, new(1f, 1f, 1f, 0.14f),
            Player: new(0f, 201 / 255f, 232 / 255f, 1f), Other: new(166 / 255f, 176 / 255f, 187 / 255f, 1f), Alert: t.BrakeColor, Accent: t.AccentBar, TopAccent: false),
        _ => new Look(new(28 / 255f, 35 / 255f, 38 / 255f, 0.62f), new(1f, 1f, 1f, 0.12f), 1f, 0f, 2.5f, new(176 / 255f, 178 / 255f, 170 / 255f, 0.30f),
            Player: t.LabelColor, Other: new(190 / 255f, 196 / 255f, 196 / 255f, 1f), Alert: t.BrakeColor, Accent: t.NumberColor, TopAccent: false),
    };

    public void Draw(ThemeCanvas c, OverlayModel m)
    {
        float alpha = Alpha(m, commit: true);
        if (alpha <= 0f || m.Radar is not { } f) return;
        var t = c.Theme;
        var look = LookFor(t);
        float prevOpacity = c.Opacity;
        c.Opacity = prevOpacity * alpha;

        double range = f.RangeMeters;
        float cx = W / 2, cy = H / 2;
        float pxPerM = (float)((H / 2 - EdgePad) / range);
        float carH = (float)(f.CarLengthMeters * pxPerM), carW = (float)(f.CarWidthMeters * LateralDipPerMeter);
        double dt = Math.Clamp(Clock(m) - f.Time, 0, MaxExtrapolateSeconds);

        // Painel.
        if (look.BorderWidth > 0 && look.Radius > 0.5f) StrokeRounded(c, look);
        else
        {
            c.FillRect(0, 0, W, H, look.Fill);
            if (look.BorderWidth > 0) c.StrokeRect(0, 0, W, H, look.Border, look.BorderWidth);
        }
        if (look.TopAccent) c.FillRect(0, 0, W, t.TitleBarHeight, look.Accent);

        // Guias a cada 5 m (dentro do alcance), como no V3.
        for (int mtr = 5; mtr < range; mtr += 5)
        {
            float d = mtr * pxPerM;
            c.FillRect(EdgePad, cy - d, W - 2 * EdgePad, 1f, look.Guide);
            c.FillRect(EdgePad, cy + d, W - 2 * EdgePad, 1f, look.Guide);
        }

        // Barras laterais: carro Alert daquele lado.
        if (f.AlertLeft) SideBar(c, 3f, look.Alert);
        if (f.AlertRight) SideBar(c, W - 7f, look.Alert);

        // O jogador.
        Car(c, cx, cy, carW, carH, look.Player, look.CarRadius, filled: true);

        // Os outros, do mais longe ao mais perto (os mais proximos ficam por cima).
        var cars = f.Cars;
        for (int i = cars.Length - 1; i >= 0; i--)
        {
            ref readonly var car = ref cars[i];
            float fwd = (float)(car.Forward + car.VForward * dt), right = (float)(car.Right + car.VRight * dt);
            float x = Math.Clamp(cx + right * LateralDipPerMeter, 4 + carW / 2, W - 4 - carW / 2);
            float y = Math.Clamp(cy - fwd * pxPerM, 4 + carH / 2, H - 4 - carH / 2);
            var color = car.Zone switch { RadarZone.Alert => look.Alert, RadarZone.Warn => Warn, _ => look.Other };
            // Como no V3: so contorno (aqui, cor esmaecida) para o carro na mesma linha e longe.
            bool faint = car.Zone == RadarZone.Far && car.Side == RadarSide.Center;
            Car(c, x, y, carW, carH, color, look.CarRadius, filled: !faint);
        }

        // Alcance, na tipografia do tema.
        int r = (int)Math.Round(range);
        if (r != _labelRange) { _labelRange = r; _label = r + " m"; }
        c.Text(_label, t.Label with { Size = 11f, Tracking = 0f }, W - 56, H - 19, 50, 14, new Color4(t.LabelColor.R, t.LabelColor.G, t.LabelColor.B, 0.85f), HAlign.Right, t.TextShadow);

        c.Opacity = prevOpacity;
    }

    static void StrokeRounded(ThemeCanvas c, Look look)
    {
        // Contorno de painel arredondado: dois retangulos arredondados (borda + miolo) sem canal de traco no canvas.
        c.FillRoundRect(0, 0, W, H, look.Radius, look.Border);
        c.FillRoundRect(look.BorderWidth, look.BorderWidth, W - 2 * look.BorderWidth, H - 2 * look.BorderWidth, Math.Max(0, look.Radius - look.BorderWidth), look.Fill);
    }

    static void SideBar(ThemeCanvas c, float x, Color4 color) => c.FillRoundRect(x, 8, 4, H - 16, 2, color);

    static void Car(ThemeCanvas c, float cx, float cy, float w, float h, Color4 color, float radius, bool filled)
    {
        if (!filled) color = new Color4(color.R, color.G, color.B, color.A * 0.5f);
        c.FillRoundRect(cx - w / 2, cy - h / 2, w, h, radius, color);
        // Marca de frente: faixa escura a 22% da ponta (mostra o sentido do carro sem desenhar nada mais).
        c.FillRect(cx - w / 2 + 2, cy - h / 2 + h * 0.22f, w - 4, 1.5f, new Color4(0f, 0f, 0f, filled ? 0.35f : 0.2f));
    }
}
