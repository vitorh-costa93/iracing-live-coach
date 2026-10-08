using Ams2.Core.Calc;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Theme;
using Ams2.Shared.Profiles;
using Vortice.Win32.Numerics;

namespace Ams2.OverlayHost.Widgets;

/// <summary>
/// Radar de proximidade. Padrao: estilo painel (replica do Radar do V3). Coluna "native": indicador nativo do AMS2 (marcadores so com carro ao lado, cor e distancia lateral). Painel (replica do Radar do V3, ver ams2/reference/radar-notes.md): o jogador no centro de um painel translucido de 120 x 190 dip,
/// os carros em volta na posicao REAL (frente/lado em metros, vinda de WorldPosition + yaw), cor por proximidade (cinza / ambar ate 12 m / vermelho ao lado ate 7 m)
/// e barras vermelhas nas bordas quando ha carro ao lado. Le so <see cref="OverlayModel.Radar"/>. Alta frequencia: redesenha a cada vblank e
/// extrapola a posicao entre dois passos de 60 Hz do provider pela velocidade relativa. Sem alocacao por quadro.
/// Aparece so com carro dentro do alcance (0,6 s de permanencia + esmaecimento) ou sempre (coluna "always"); nunca fora de sessao ou sozinho na pista.
/// </summary>
public sealed class RadarWidget : IWidget
{
    public string Id => "radar";
    public bool HighFrequency => true;
    public (float Width, float Height) DesignSize => Panel ? (W, H) : (NW, NH);
    public void UseTheme(Theme.Theme theme) { }
    WidgetSettings _cfg = new() { Id = "radar" };
    public void Configure(WidgetSettings s) => _cfg = s;
    /// <summary>Padrao: painel estilo V3. A coluna "native" troca pelo indicador nativo do AMS2 (so com carro ao lado).</summary>
    bool Panel => !_cfg.ColumnVisible("native");

    const float W = 120, H = 190;
    /// <summary>Tamanho do estilo nativo (so os marcadores, sem painel).</summary>
    const float NW = 280, NH = 72;
    /// <summary>Escala lateral fixa: 8 dip por metro (carro de 2 m = 16 dip, como no V3; faixa do V3 a 30 dip = 3,75 m; meia-largura util 7,5 m).</summary>
    const float LateralDipPerMeter = 8f;
    const float EdgePad = 10f;
    const double HoldSeconds = 0.6, FadeSeconds = 0.3, MaxExtrapolateSeconds = 0.05;
    static readonly Color4 Warn = new(1f, 0.8f, 0f, 1f);

    double _lastNear = double.NegativeInfinity;
    int _labelRange = -1;
    string _label = "";

    /// <summary>Opcoes do tracker para as configuracoes do widget (alcance em metros, sensibilidade 1..5).</summary>
    public static RadarOptions OptionsFor(WidgetSettings s)
    {
        bool panel = !s.ColumnVisible("native");
        return new()
        {
            // Estilo nativo: so interessa o carro ao lado (sem faixa longitudinal ampla): alcance minimo e janela lateral de 9,5 m.
            RangeMeters = panel ? s.EffectiveRadarRange : RadarOptions.MinRange,
            LateralMeters = panel ? 7.5 : RadarOptions.AlongsideLateralMeters + 0.5,
            Sensitivity = WidgetCatalog.RadarSensitivityFactor(s.EffectiveRadarSensitivity),
        };
    }

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
    public bool IsIdle(OverlayModel m) => Panel ? Alpha(m, commit: false) <= 0f : NativeAlpha(m, commit: false) is (<= 0f, <= 0f);

    // ---- Estilo nativo ("Indicador de proximidade" do AMS2) ----
    // Observado no jogo: dois pequenos triangulos vermelhos (chevron) na parte baixa-central, do lado do carro vizinho, so com carro realmente
    // ao lado (|frente| menor que ~4 m). Cor verde (longe) -> amarelo -> vermelho (perto) e a distancia lateral em metros sao INFERIDAS (a descricao do jogo diz
    // "verde = longe, vermelho = perto"; a distancia numerica segue o pedido do usuario).
    const double NativeFade = 0.3;
    const float NativeInner = 12f, NativeSpread = 26f;
    const double GapGreen = 6.0, GapYellow = 3.5, GapRed = 1.5;
    double _seenL = double.NegativeInfinity, _seenR = double.NegativeInfinity, _gapL, _gapR;

    /// <summary>Cor por distancia lateral (m, borda a borda): verde &gt;= 6, amarelo 3,5, vermelho &lt;= 1,5; linear entre os pontos.</summary>
    public static Color4 GapColor(double gap)
    {
        var green = new Color4(0.18f, 0.85f, 0.35f, 1f); var yellow = new Color4(1f, 0.85f, 0f, 1f); var red = new Color4(1f, 0.16f, 0.12f, 1f);
        if (gap >= GapGreen) return green;
        if (gap <= GapRed) return red;
        if (gap >= GapYellow) return Lerp(yellow, green, (float)((gap - GapYellow) / (GapGreen - GapYellow)));
        return Lerp(red, yellow, (float)((gap - GapRed) / (GapYellow - GapRed)));
    }
    static Color4 Lerp(Color4 a, Color4 b, float t) => new(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t, 1f);

    /// <summary>Opacidade de um lado: 1 com carro ao lado, esmaece em 0,3 s depois que ele sai.</summary>
    static float SideAlpha(double now, double seen) => seen == double.NegativeInfinity ? 0f : (float)Math.Clamp(1 - (now - seen) / NativeFade, 0, 1);

    /// <summary>Atualiza (se <paramref name="commit"/>) e devolve as opacidades esquerda/direita do estilo nativo.</summary>
    (float L, float R) NativeAlpha(OverlayModel m, bool commit)
    {
        if (!m.Connected || m.Radar is not { Valid: true } f)
        {
            if (commit) _seenL = _seenR = double.NegativeInfinity;
            return (0, 0);
        }
        double now = Clock(m), sl = _seenL, sr = _seenR;
        if (f.AlongLeft) { sl = now; if (commit) { _seenL = now; _gapL = f.RenderGap(left: true, now); } }
        if (f.AlongRight) { sr = now; if (commit) { _seenR = now; _gapR = f.RenderGap(left: false, now); } }
        return (SideAlpha(now, sl), SideAlpha(now, sr));
    }

    /// <summary>Triangulo cheio por faixas horizontais de 0,5 dip (o canvas so tem retangulos); sem alocacao.</summary>
    static void FillTri(ThemeCanvas c, float x0, float y0, float x1, float y1, float x2, float y2, Color4 color)
    {
        float top = Math.Min(y0, Math.Min(y1, y2)), bottom = Math.Max(y0, Math.Max(y1, y2));
        for (float y = top; y < bottom; y += 0.5f)
        {
            float h = Math.Min(0.5f, bottom - y), ym = y + h / 2, lo = float.MaxValue, hi = float.MinValue;
            Edge(x0, y0, x1, y1); Edge(x1, y1, x2, y2); Edge(x2, y2, x0, y0);
            if (hi > lo) c.FillRect(lo, y, hi - lo, h + (h >= 0.5f ? 0.2f : 0f), color);
            void Edge(float ax, float ay, float bx, float by)
            {
                if ((ay <= ym && by > ym) || (by <= ym && ay > ym)) { float x = ax + (ym - ay) / (by - ay) * (bx - ax); lo = Math.Min(lo, x); hi = Math.Max(hi, x); }
            }
        }
    }

    /// <summary>Um marcador (par de triangulos em chevron) com a borda interna em <paramref name="inner"/>; <paramref name="dir"/> = +1 direita, -1 esquerda.</summary>
    static void Marker(ThemeCanvas c, Theme.Theme t, float inner, float cy, int dir, double gap, float alpha)
    {
        float closeness = (float)(1 - Math.Clamp(gap / GapGreen, 0, 1));
        float s = 1f + 0.4f * closeness;
        var color = GapColor(gap);
        float prev = c.Opacity; c.Opacity = prev * alpha;
        // Dois triangulos largos e baixos; o segundo deslocado para fora e para baixo (como o par do jogo).
        float bw = 26f * s, bh = 8f * s, dx = 16f * s, dy = 6f * s;
        float ax = inner, ay = cy - 1f * s;
        Tri(ax, ay, bw, bh);
        Tri(ax + dir * dx, ay + dy, bw, bh);
        float outer = inner + dir * (dx + bw);
        string text = gap.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " m";
        var font = t.Label with { Size = 14f, Tracking = 0f };
        const float tw = 52f;
        c.Text(text, font, dir > 0 ? outer + 5f : outer - 5f - tw, cy - 9f, tw, 18f, new Color4(1f, 1f, 1f, 0.95f), dir > 0 ? HAlign.Left : HAlign.Right, t.TextShadow);
        c.Opacity = prev;

        void Tri(float x, float y, float w, float h)
        {
            // base horizontal em y, apice em cima, deslocado para o lado de fora (inclinado como no jogo)
            float apexX = x + dir * w * 0.78f;
            FillTri(c, x, y, x + dir * w, y, apexX, y - h, color);
        }
    }

    void DrawNative(ThemeCanvas c, OverlayModel m)
    {
        var (al, ar) = NativeAlpha(m, commit: true);
        if (al <= 0f && ar <= 0f) return;
        float cx = NW / 2, cy = NH / 2;
        if (al > 0f) Marker(c, c.Theme, cx - NativeInner - NativeSpread * Closeness(_gapL), cy, -1, _gapL, al);
        if (ar > 0f) Marker(c, c.Theme, cx + NativeInner + NativeSpread * Closeness(_gapR), cy, +1, _gapR, ar);
    }

    /// <summary>0 = colado, 1 = longe (afasta o marcador do centro conforme a distancia lateral).</summary>
    static float Closeness(double gap) => (float)Math.Clamp(gap / GapGreen, 0, 1);

    readonly record struct Look(Color4 Fill, Color4 Border, float BorderWidth, float Radius, float CarRadius, Color4 Guide, Color4 Player, Color4 Other, Color4 Alert, Color4 Accent, bool TopAccent);

    static Look LookFor(Theme.Theme t) => t.Style switch
    {
        ThemeStyle.Broadcast2000s => new Look(new(0f, 0f, 0f, 0.42f), new(1f, 1f, 1f, 0.35f), 1f, 0f, 1.5f, new(1f, 1f, 1f, 0.22f),
            Player: new(1f, 1f, 1f, 1f), Other: new(0.62f, 0.62f, 0.68f, 1f), Alert: t.BrakeColor, Accent: t.AccentBar, TopAccent: true),
        // 2018: painel preto translúcido de cantos retos com o filete vermelho F1 no topo; jogador branco (as zonas de aviso usam amarelo/vermelho), demais cinza-claro.
        ThemeStyle.Modern2018 => new Look(t.PanelFill, new(0f, 0f, 0f, 0f), 0f, 0f, 2.5f, new(1f, 1f, 1f, 0.14f),
            Player: t.AccentFill, Other: new(200 / 255f, 202 / 255f, 208 / 255f, 1f), Alert: t.BrakeColor, Accent: t.AccentBar, TopAccent: true),
        _ => new Look(new(28 / 255f, 35 / 255f, 38 / 255f, 0.62f), new(1f, 1f, 1f, 0.12f), 1f, 0f, 2.5f, new(176 / 255f, 178 / 255f, 170 / 255f, 0.30f),
            Player: t.LabelColor, Other: new(190 / 255f, 196 / 255f, 196 / 255f, 1f), Alert: t.BrakeColor, Accent: t.NumberColor, TopAccent: false),
    };

    public void Draw(ThemeCanvas c, OverlayModel m)
    {
        if (Panel) DrawPanel(c, m); else DrawNative(c, m);
    }

    void DrawPanel(ThemeCanvas c, OverlayModel m)
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
