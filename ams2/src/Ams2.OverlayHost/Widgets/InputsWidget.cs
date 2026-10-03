using System.Globalization;
using Ams2.Core.Calc;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Theme;
using Ams2.Shared.Profiles;
using Vortice.Win32.Numerics;

namespace Ams2.OverlayHost.Widgets;

/// <summary>
/// Pedais: gráfico dos últimos 10 s (acelerador e freio preenchidos, volante em linha), legenda, barras
/// verticais THR/BRK, marcha e velocidade. Só lê <see cref="OverlayModel.InputHistory"/> e o jogador.
/// </summary>
public sealed class InputsWidget : IWidget
{
    public string Id => "inputs";
    /// <summary>O gráfico rola por tempo: precisa de um quadro por vblank para o deslocamento ser contínuo.</summary>
    public bool HighFrequency => true;
    Theme.Theme _theme = Themes.F1_1998;
    bool Analog => _theme.Style == ThemeStyle.Broadcast2000s;
    public void UseTheme(Theme.Theme theme) => _theme = theme;
    public (float Width, float Height) DesignSize => Analog ? AnalogSize() : (L.Width, 197);

    // Gráfico
    // (o gráfico e a largura de cada bloco seguem as colunas visíveis; todas visíveis = mockup)
    const float GX = 21, GW = 408, GY = 78, GH = 88;
    // Barras verticais
    const float BarW = 22, BarY = 46, BarH = 109, BarPitch = 54;
    // Marcha / velocidade
    const float GearBlockW = 120, BlockGap = 36, GearGap = 27, EdgeRight = 4, MinStdWidth = 280;

    readonly record struct StdLayout(float GraphX, float ThrX, float GearCx, float Width);

    /// <summary>Blocos visíveis (gráfico, barras, marcha) lado a lado, sem buracos.</summary>
    StdLayout L
    {
        get
        {
            float x = GX, graphX = x, thrX = 0, gearCx = 0;
            if (_cfg.ColumnVisible("graph")) x += GW + BlockGap;
            if (_cfg.ColumnVisible("bars")) { thrX = x; x += BarPitch + BarW + GearGap; }
            if (_cfg.ColumnVisible("gear")) { if (!_cfg.ColumnVisible("graph")) x = Math.Max(x, 150); /* o rotulo GEAR nao pode invadir o titulo e a barra do titulo precisa de largura util */ gearCx = x + GearBlockW / 2; x += GearBlockW; }
            else if (_cfg.ColumnVisible("bars")) x -= GearGap;
            else if (_cfg.ColumnVisible("graph")) x -= BlockGap;
            return new StdLayout(graphX, thrX, gearCx, Math.Max(MinStdWidth, x + EdgeRight));
        }
    }


    WidgetSettings _cfg = new() { Id = "inputs" };
    public void Configure(WidgetSettings s) => _cfg = s;

    public void Draw(ThemeCanvas c, OverlayModel m)
    {
        var t = c.Theme;
        if (Analog) { DrawAnalog(c, t, m); return; }
        var (w, h) = DesignSize;
        c.Panel(0, 0, w, h);
        Chrome.Header(c, "INPUTS", 19, 11, 150, maxRight: !_cfg.ColumnVisible("graph") && _cfg.ColumnVisible("gear") ? L.GearCx - 45 : w - 14);
        bool graph = _cfg.ColumnVisible("graph"), bars = _cfg.ColumnVisible("bars"), gear = _cfg.ColumnVisible("gear");
        var lay = L;
        if (graph) { DrawLegend(c, t, lay.GraphX); DrawGraphFrame(c, t, lay.GraphX, GY, GW, GH); }

        var p = m.Session?.Player;
        if (!m.Connected || p is null)
        {
            c.Text(m.Connected ? "NO DATA" : "WAITING FOR AMS2", t.Label, GX + 10, GY + 28, 380, 30, t.LabelColor, shadow: t.TextShadow);
            return;
        }

        if (graph) DrawTrace(c, t, m.Inputs, lay.GraphX, GY, GW, GH);
        if (bars)
        {
            DrawBar(c, t, lay.ThrX, p.Inputs.Throttle, t.ThrottleColor, "THR");
            DrawBar(c, t, lay.ThrX + BarPitch, p.Inputs.Brake, t.BrakeColor, "BRK");
        }
        if (gear) DrawGear(c, t, lay.GearCx, p.Gear, p.SpeedMps * 3.6);
    }

    static void DrawLegend(ThemeCanvas c, Theme.Theme t, float gx)
    {
        var f = t.Label with { Size = 17 };
        (string, Color4)[] items = [("THROTTLE", t.ThrottleColor), ("BRAKE", t.BrakeColor), ("STEERING", t.SteeringColor)];
        for (int i = 0; i < items.Length; i++)
        {
            float y = 14 + i * 21;
            c.FillRect(gx + 293, y + 7, 20, 8, items[i].Item2);
            c.Text(items[i].Item1, f, gx + 321, y, 110, 22, t.TitleColor, shadow: t.TextShadow);
        }
    }

    static void DrawGraphFrame(ThemeCanvas c, Theme.Theme t, float GX, float GY, float GW, float GH)
    {
        bool b04 = t.Style == ThemeStyle.Broadcast2000s;
        c.FillRect(GX, GY, GW, GH, b04 ? new Color4(0, 0, 0, 0.6f) : new Color4(0, 0, 0, 0.22f));
        c.Line(GX, GY, GX, GY + GH, t.GraphAxis, 2);
        c.Line(GX, GY + GH, GX + GW, GY + GH, t.GraphAxis, 2);
        var f = t.Label with { Size = 16 };
        if (b04)
        {
            var cf = t.Label with { Size = 13 };
            Chrome.Caption(c, GX, GY + GH + 3, "10s", 18, cf);
            float zw = c.Measure("0s", cf) + 18;
            Chrome.Caption(c, GX + GW - zw, GY + GH + 3, "0s", 18, cf);
            return;
        }
        c.Text("10s", f, GX, GY + GH + 2, 50, 22, t.TitleColor, shadow: t.TextShadow);
        c.Text("0s", f, GX + GW - 50, GY + GH + 2, 50, 22, t.TitleColor, HAlign.Right, t.TextShadow);
    }

    // Buffers do gráfico: criados uma vez (o widget é desenhado a cada vblank; nada é alocado por quadro).
    InputSample[]? _snap;
    float[] _thr = [], _brk = [], _str = [];
    bool[] _ok = [];
    /// <summary>Quanto tempo o último valor é mantido (zero-order hold) à direita da última amostra, em s: cobre o atraso até a próxima escrita do jogo.</summary>
    const double HoldSeconds = 0.05;

    /// <summary>
    /// Uma coluna de pixel por vez: valor interpolado no instante da coluna (10 s = largura do gráfico). O instante "agora" é o do
    /// RENDER (relógio do anel), não o do último passo do provider: o gráfico desloca de forma contínua por tempo, não por amostra.
    /// </summary>
    void DrawTrace(ThemeCanvas c, Theme.Theme t, InputRing? ring, float GX, float GY, float GW, float GH)
    {
        if (ring is null) return;
        const double win = OverlayDataProvider.InputWindowSeconds;
        int cols = (int)GW;
        if (_thr.Length < cols) { _thr = new float[cols]; _brk = new float[cols]; _str = new float[cols]; _ok = new bool[cols]; }
        var snap = _snap ??= new InputSample[ring.Usable];
        double now = ring.Now;
        int n = ring.CopyFrom(now - win - 0.25, snap); // um pouco antes da borda esquerda, para interpolar a primeira coluna
        if (n < 2) return;
        var thr = _thr; var brk = _brk; var str = _str; var ok = _ok;
        double firstT = snap[0].T, lastT = snap[n - 1].T;
        int k = 0;
        for (int x = 0; x < cols; x++)
        {
            double tt = now - win + (x + 0.5) / cols * win;
            ok[x] = false;
            if (tt < firstT || tt > lastT + HoldSeconds) continue;
            if (tt >= lastT) { thr[x] = snap[n - 1].Throttle; brk[x] = snap[n - 1].Brake; str[x] = snap[n - 1].Steering; ok[x] = true; continue; }
            while (k < n - 2 && snap[k + 1].T < tt) k++;
            var a = snap[k]; var b = snap[k + 1];
            float f = b.T > a.T ? (float)Math.Clamp((tt - a.T) / (b.T - a.T), 0, 1) : 0;
            thr[x] = a.Throttle + (b.Throttle - a.Throttle) * f;
            brk[x] = a.Brake + (b.Brake - a.Brake) * f;
            str[x] = a.Steering + (b.Steering - a.Steering) * f;
            ok[x] = true;
        }

        float bottom = GY + GH - 1, span = GH - 4;
        Color4 Fill(Color4 col) => new(col.R, col.G, col.B, 0.5f);
        var thrFill = Fill(t.ThrottleColor); var brkFill = Fill(t.BrakeColor);
        for (int x = 0; x < cols; x++)
        {
            if (!ok[x]) continue;
            float fx = GX + x;
            c.FillRect(fx, bottom - thr[x] * span, 1, thr[x] * span, thrFill);
            c.FillRect(fx, bottom - brk[x] * span, 1, brk[x] * span, brkFill);
        }
        for (int x = 1; x < cols; x++)
        {
            if (!ok[x] || !ok[x - 1]) continue;
            float fx = GX + x;
            if (thr[x - 1] + thr[x] > 0.01f) c.Line(fx - 1, bottom - thr[x - 1] * span, fx, bottom - thr[x] * span, t.ThrottleColor, 2);
            if (brk[x - 1] + brk[x] > 0.01f) c.Line(fx - 1, bottom - brk[x - 1] * span, fx, bottom - brk[x] * span, t.BrakeColor, 2);
            float mid = GY + GH / 2, amp = GH / 2 - 4;
            c.Line(fx - 1, mid + Math.Clamp(str[x - 1], -1, 1) * amp, fx, mid + Math.Clamp(str[x], -1, 1) * amp, t.SteeringColor, 1.6f);
        }
    }

    static void DrawBar(ThemeCanvas c, Theme.Theme t, float x, double value, Color4 color, string label)
    {
        c.FillRect(x, BarY, BarW, BarH, new Color4(0, 0, 0, 0.45f));
        c.StrokeRect(x, BarY, BarW, BarH, t.GraphAxis, 2);
        float fh = (float)Math.Clamp(value, 0, 1) * (BarH - 4);
        c.FillRect(x + 2, BarY + BarH - 2 - fh, BarW - 4, fh, color);
        c.Text(label, t.Label with { Size = 17 }, x - 20, BarY + BarH + 3, BarW + 40, 24, t.TitleColor, HAlign.Center, t.TextShadow);
    }

    static void DrawGear(ThemeCanvas c, Theme.Theme t, float GearCx, int gear, double kph)
    {
        c.Text("GEAR", t.Label with { Size = 21 }, GearCx - 50, 11, 100, 30, t.TitleColor, HAlign.Center, t.TextShadow);
        string g = gear switch { < 0 => "R", 0 => "N", _ => gear.ToString(CultureInfo.InvariantCulture) };
        c.FillRoundRect(GearCx - 30, 45, 60, 57, t.BoxRadius, t.AccentFill);
        // A fonte de numeros do f1-1998 (F1 Broadcast 98 Values) so tem digitos: "N" e "R" caiam numa fonte do sistema fina e
        // destoante. Letras usam a fonte do titulo (mesma familia/peso do "GEAR" e do "KPH").
        var gf = g.Length == 1 && !char.IsDigit(g[0]) && t.Numbers.Family.StartsWith("F1 Broadcast", StringComparison.Ordinal) ? t.Title with { Size = 36 } : t.Numbers;
        c.Text(g, gf, GearCx - 30, 43, 60, 57, t.AccentInk, HAlign.Center);
        c.Text(Math.Round(kph).ToString("0", CultureInfo.InvariantCulture), t.Numbers, GearCx - 60, 108, 120, 40, t.ValueColor, HAlign.Center, t.ValueShadow);
        c.Text("KPH", t.Label with { Size = 21 }, GearCx - 50, 144, 100, 28, t.TitleColor, HAlign.Center, t.TextShadow);
    }

    // ---- Estilo 2004-2008: cluster tacômetro + marcha/pedais + barra de velocidade em arco (ref. f1-2000s-speedo-dial) ----
    // Coordenadas de projeto = pixels da referência ampliados 4x (origem no canto do tacômetro).
    const float TCx = 172, TCy = 170, TDiscR = 170;           // tacômetro
    const float CellX = 202, CellW = 148, CellH = 38, CellPitch = 44, CellY0 = 175;
    const float ACx = 182, ACy = 328, ARin = 66, ARout = 114; // arco da barra de velocidade (centro, raios interno/externo)
    const float BarX0 = 10, SpdY = 394, SpdH = 48;            // trecho reto da barra (12 segmentos verdes, 0-240 km/h)
    const float ClusterW = 360, ClusterH = 490, GraphW = 342, GraphH = 84, GraphSectionH = 8 + GraphH + 26;

    static Color4 Rgb(int r, int g, int b, float a = 1f) => new(r / 255f, g / 255f, b / 255f, a);
    static readonly Color4 PanelDark = new(0.04f, 0.04f, 0.05f, 0.72f);
    static readonly Color4 SegOff = new(0.26f, 0.27f, 0.29f, 0.55f);

    static (float X, float Y) Pol(float cx, float cy, float r, double deg) =>
        (cx + r * (float)Math.Cos(deg * Math.PI / 180), cy + r * (float)Math.Sin(deg * Math.PI / 180));

    /// <summary>Setor anular (raios r0..r1, ângulos de tela em graus) preenchido por raios finos sobrepostos.</summary>
    static void Wedge(ThemeCanvas c, float cx, float cy, float r0, float r1, double a0, double a1, Color4 col)
    {
        double lo = Math.Min(a0, a1), hi = Math.Max(a0, a1);
        double step = Math.Max(0.15, 1.0 / r1 * 180 / Math.PI);
        for (double a = lo; a <= hi + 1e-6; a += step)
        {
            var (x1, y1) = Pol(cx, cy, r0, a); var (x2, y2) = Pol(cx, cy, r1, a);
            c.Line(x1, y1, x2, y2, col, 1.6f);
        }
        var (ex1, ey1) = Pol(cx, cy, r0, hi); var (ex2, ey2) = Pol(cx, cy, r1, hi);
        c.Line(ex1, ey1, ex2, ey2, col, 1.6f);
    }

    /// <summary>Ponteiro afilado do cubo até <paramref name="len"/> (largura w0 no cubo, w1 na ponta).</summary>
    static void Needle(ThemeCanvas c, float cx, float cy, double deg, float len, float w0, float w1, Color4 col)
    {
        const int n = 40;
        for (int i = 0; i < n; i++)
        {
            float f0 = (float)i / n, f1 = (i + 1.4f) / n;
            var (x1, y1) = Pol(cx, cy, len * f0, deg); var (x2, y2) = Pol(cx, cy, Math.Min(len, len * f1), deg);
            c.Line(x1, y1, x2, y2, col, w0 + (w1 - w0) * (f0 + f1) / 2);
        }
    }

    /// <summary>Fim da escala do tacômetro em milhares de rpm: 18900 -> 20 (como na referência); sem dado, 20.</summary>
    static int ScaleEnd(double maxRpm) => maxRpm > 1000 ? Math.Clamp((int)Math.Ceiling(maxRpm / 1000.0) + 1, 8, 30) : 20;
    static int ScaleStart(int end) => end >= 12 ? 6 : 0;

    void DrawAnalog(ThemeCanvas c, Theme.Theme t, OverlayModel m)
    {
        var p = m.Session?.Player;
        bool live = m.Connected && p is not null;
        double kph = live ? p!.SpeedMps * 3.6 : 0;
        double rpm = live ? p!.Rpm : 0;
        var white = new Color4(1, 1, 1, 1);
        var shadow = new ShadowToken(1.5f, 1.5f, new Color4(0, 0, 0, 0.7f));

        // Painéis translúcidos: disco do tacômetro e placa da barra de velocidade.
        c.FillEllipse(TCx, TCy, TDiscR, TDiscR, PanelDark);
        c.FillRoundRect(0, 330, 346, ClusterH - 330, 28, PanelDark);

        // ---- Tacômetro ----
        int end = ScaleEnd(live ? p!.MaxRpm : 0), start = ScaleStart(end);
        double step = 270.0 / (end - start);
        double AngleOf(double r) => 90 + Math.Clamp(r / 1000.0 - start, 0, end - start) * step;
        Wedge(c, TCx, TCy, 101, 113, 90, 360, white);   // anel grosso
        Wedge(c, TCx, TCy, 92, 94.6f, 90, 360, white);  // anéis finos
        Wedge(c, TCx, TCy, 85, 87.6f, 90, 360, white);
        var nf = t.Label with { Size = 22 };
        for (int k = start; k <= end; k++)
        {
            double a = 90 + (k - start) * step;
            var (x1, y1) = Pol(TCx, TCy, 118, a); var (x2, y2) = Pol(TCx, TCy, 130, a);
            c.Line(x1, y1, x2, y2, white, 3f);
            var (tx, ty) = Pol(TCx, TCy, 148, a);
            c.Text(k.ToString(CultureInfo.InvariantCulture), nf, tx - 24, ty - 14, 48, 28, white, HAlign.Center, shadow);
        }
        double na = AngleOf(rpm);
        var (tailX, tailY) = Pol(TCx, TCy, 30, na + 180);
        Needle(c, TCx, TCy, na + 180, 30, 17, 17, white);            // contrapeso
        c.FillEllipse(tailX, tailY, 11, 11, white);
        Needle(c, TCx, TCy, na, 112, 15, 3.5f, white);                // agulha
        c.FillEllipse(TCx, TCy, 21, 21, white);
        c.FillEllipse(TCx, TCy, 13, 13, Rgb(225, 228, 232));

        // ---- Marcha e pedais (pilha de células à direita do tacômetro) ----
        float y = CellY0;
        var cf = t.Label with { Size = 26 };
        if (_cfg.ColumnVisible("gear"))
        {
            string g = !live ? "-" : p!.Gear switch { < 0 => "R", 0 => "N", _ => p.Gear.ToString(CultureInfo.InvariantCulture) };
            Chrome.WhiteCell(c, CellX, y, CellW, CellH, "", cf);
            c.Text("Gear", cf, CellX, y - 1, CellW * 0.62f, CellH, t.NameCellInk, HAlign.Center);
            c.Text(g, cf, CellX + CellW * 0.62f, y - 1, CellW * 0.38f - 12, CellH, t.NameCellInk, HAlign.Right);
            y += CellPitch;
        }
        if (_cfg.ColumnVisible("bars"))
        {
            PedalCell(c, y, "Throttle", live ? p!.Inputs.Throttle : 0, [new(0f, Rgb(96, 230, 96)), new(0.35f, Rgb(26, 185, 23)), new(1f, Rgb(12, 112, 20))], cf, shadow);
            PedalCell(c, y + CellPitch, "Brake", live ? p!.Inputs.Brake : 0, [new(0f, Rgb(244, 84, 64)), new(0.35f, Rgb(210, 28, 20)), new(1f, Rgb(120, 8, 8))], cf, shadow);
        }

        // ---- Barra de velocidade em arco: 12 verdes (0-240), 3 amarelos (240-280), 3 laranjas (280-320), 1 vermelho (320-340) ----
        Color4 green = Rgb(52, 208, 82), yellow = Rgb(226, 202, 31), orange = Rgb(205, 122, 24), red = Rgb(128, 12, 16);
        float pitch = (ACx - BarX0) / 12f;
        for (int i = 0; i < 12; i++)
            c.FillRect(BarX0 + i * pitch, SpdY, pitch - 3, SpdH, kph > i * 20 ? green : SegOff);
        const double seg = 14.2, gap = 1.6;
        for (int i = 0; i < 6; i++)
        {
            double a0 = 90 - i * seg, a1 = 90 - (i + 1) * seg;
            double startKph = 240 + i * (40.0 / 3);
            Wedge(c, ACx, ACy, ARin, ARout, a1 + gap / 2, a0 - gap / 2, kph > startKph ? (i < 3 ? yellow : orange) : SegOff);
        }
        Wedge(c, ACx, ACy, ARin, ARout, -24, 3.6, kph > 320 ? red : SegOff);
        var lf = t.Label with { Size = 20 };
        (string, double)[] marks = [("240", 85), ("280", 47), ("320", 16), ("340", -8)];
        foreach (var (txt, a) in marks)
        {
            var (lx, ly) = Pol(ACx, ACy, 140, a);
            c.Text(txt, lf, lx - 30, ly - 13, 60, 26, white, HAlign.Center, shadow);
        }
        c.Text(Math.Round(kph).ToString("0", CultureInfo.InvariantCulture) + " km/h", lf, BarX0, 455, 130, 26, white, HAlign.Left, shadow);

        if (_cfg.ColumnVisible("graph"))
        {
            // Gráfico opcional dos últimos 10 s (mesmos dados do estilo padrão).
            float gy = ClusterH + 8;
            DrawGraphFrame(c, t, 0, gy, GraphW, GraphH);
            if (live) DrawTrace(c, t, m.Inputs, 0, gy, GraphW, GraphH);
            else Chrome.Notice(c, m.Connected ? "NO DATA" : "WAITING FOR AMS2", 10, gy + 24, 280);
        }
    }

    (float Width, float Height) AnalogSize()
        => (ClusterW, ClusterH + (_cfg.ColumnVisible("graph") ? GraphSectionH : 0));

    /// <summary>Célula escura que enche (verde/vermelho) proporcionalmente ao valor, com o rótulo branco por cima.</summary>
    static void PedalCell(ThemeCanvas c, float y, string label, double value, BarStop[] stops, FontToken font, ShadowToken shadow)
    {
        c.FillRect(CellX, y + CellH, CellW, 1.5f, new Color4(0.04f, 0.04f, 0.08f, 0.6f));
        c.FillRect(CellX, y, CellW, CellH, new Color4(0.09f, 0.1f, 0.11f, 0.9f));
        float fw = (float)Math.Clamp(value, 0, 1) * CellW;
        if (fw > 0.5f) c.VGradientRect(CellX, y, fw, CellH, stops);
        c.Text(label, font, CellX, y - 1, CellW, CellH, new Color4(1, 1, 1, 1), HAlign.Center, shadow);
    }
}
