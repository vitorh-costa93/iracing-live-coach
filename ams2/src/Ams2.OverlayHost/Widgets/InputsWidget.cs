using System.Globalization;
using Ams2.Core.Calc;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Theme;
using Ams2.Shared.Profiles;
using Vortice.Win32.Numerics;

namespace Ams2.OverlayHost.Widgets;

/// <summary>
/// Pedais: grÃ¡fico de LINHAS dos Ãºltimos 10 s (sÃ³ acelerador e freio, em todos os temas), legenda, barras verticais THR/BRK,
/// marcha e velocidade. No 2004â€“2008 o cluster analÃ³gico (tacÃ´metro + barra de velocidade) Ã© a coluna "speedo" e o grÃ¡fico entra
/// embaixo, combinÃ¡vel com ele. SÃ³ lÃª <see cref="OverlayModel.Inputs"/> e o jogador.
/// </summary>
public sealed class InputsWidget : IWidget
{
    readonly bool _graphOnly;
    public InputsWidget(bool graphOnly = false) => _graphOnly = graphOnly;
    public string Id => _graphOnly ? "inputgraph" : "inputs";
    /// <summary>O grÃ¡fico rola por tempo: precisa de um quadro por vblank para o deslocamento ser contÃ­nuo.</summary>
    public bool HighFrequency => true;
    Theme.Theme _theme = Themes.F1_1998;
    bool Analog => _theme.Style == ThemeStyle.Broadcast2000s;
    public void UseTheme(Theme.Theme theme) => _theme = theme;
    public (float Width, float Height) DesignSize => _graphOnly ? (PlateW, PlotTop + GraphH + PlateBottom) : Analog ? AnalogSize() : (L.Width, 197);

    // GrÃ¡fico
    // (o grÃ¡fico e a largura de cada bloco seguem as colunas visÃ­veis; todas visÃ­veis = mockup)
    const float GX = 21, GY = 78, GH = 88;
    /// <summary>Largura do grÃ¡fico: 408 do mockup x largura configurada da coluna "graph".</summary>
    float GW => MathF.Round(_cfg.Width("graph", 408));
    SpeedUnit Unit => _cfg.Fmt.SpeedOrDefault;
    // Barras verticais
    const float BarW = 22, BarY = 46, BarH = 109, BarPitch = 54;
    // Marcha / velocidade
    const float GearBlockW = 120, BlockGap = 36, GearGap = 27, EdgeRight = 4, MinStdWidth = 280;

    readonly record struct StdLayout(float GraphX, float ThrX, float GearCx, float Width);

    /// <summary>Blocos visÃ­veis (grÃ¡fico, barras, marcha) lado a lado, sem buracos.</summary>
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
        if (_graphOnly)
        {
            DrawGraph2004(c, t, m, m.Connected && m.Session?.Player is not null, 0,
                new ShadowToken(1.5f, 1.5f, new Color4(0, 0, 0, 0.7f)));
            return;
        }
        if (Analog) { DrawAnalog(c, t, m); return; }
        var (w, h) = DesignSize;
        c.Panel(0, 0, w, h);
        Chrome.Header(c, "INPUTS", 19, 11, 150, maxRight: !_cfg.ColumnVisible("graph") && _cfg.ColumnVisible("gear") ? L.GearCx - 45 : w - 14);
        bool graph = _cfg.ColumnVisible("graph"), bars = _cfg.ColumnVisible("bars"), gear = _cfg.ColumnVisible("gear");
        var lay = L;
        float gw = GW;
        if (graph) { DrawLegend(c, t, lay.GraphX + gw - 115); DrawGraphFrame(c, t, lay.GraphX, GY, gw, GH); }

        var p = m.Session?.Player;
        if (!m.Connected || p is null)
        {
            c.Text(m.Connected ? "NO DATA" : "WAITING FOR AMS2", t.Label, GX + 10, GY + 28, 380, 30, t.LabelColor, shadow: t.TextShadow);
            return;
        }

        var telemetry = RenderPlayerTelemetry.Read(m.Inputs, p);
        if (graph) DrawTrace(c, t, m.Inputs, lay.GraphX, GY, gw, GH, t.ThrottleColor, t.BrakeColor, 2.2f);
        if (bars)
        {
            DrawBar(c, t, lay.ThrX, telemetry.Throttle, t.ThrottleColor, "THR");
            DrawBar(c, t, lay.ThrX + BarPitch, telemetry.Brake, t.BrakeColor, "BRK");
        }
        if (gear) DrawGear(c, t, lay.GearCx, telemetry.Gear, DisplayFormat.Speed(telemetry.SpeedMps, Unit), DisplayFormat.SpeedLabel(Unit));
    }

    /// <summary>Legenda Ã  direita, acima do grÃ¡fico: sÃ³ acelerador e freio (o volante saiu do grÃ¡fico).</summary>
    static void DrawLegend(ThemeCanvas c, Theme.Theme t, float x)
    {
        var f = t.Label with { Size = 17 };
        (string, Color4)[] items = [("THROTTLE", t.ThrottleColor), ("BRAKE", t.BrakeColor)];
        for (int i = 0; i < items.Length; i++)
        {
            float y = 22 + i * 24;
            c.FillRect(x, y + 10, 20, 3, items[i].Item2);   // amostra em linha, como o traÃ§o do grÃ¡fico
            c.Text(items[i].Item1, f, x + 28, y, 110, 22, t.TitleColor, shadow: t.TextShadow);
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

    // Buffers do grÃ¡fico: criados uma vez (o widget Ã© desenhado a cada vblank; nada Ã© alocado por quadro).
    InputSample[]? _snap;
    System.Numerics.Vector2[] _tracePoints = [];
    const double HoldSeconds = 0.05;

    /// <summary>Vertices no instante real da amostra: o traco inteiro rola em subpixels, sem saltos entre colunas fixas.</summary>
    void DrawTrace(ThemeCanvas c, Theme.Theme t, InputRing? ring, float GX, float GY, float GW, float GH, Color4 thrColor, Color4 brkColor, float width)
    {
        if (ring is null) return;
        const double win = OverlayDataProvider.InputWindowSeconds;
        var snap = _snap ??= new InputSample[ring.Usable];
        double now = ring.Now;
        int n = ring.CopyFrom(now - win - 0.25, snap);
        if (n < 2) return;
        if (_tracePoints.Length < n + 1) _tracePoints = new System.Numerics.Vector2[ring.Usable + 1];
        float bottom = GY + GH - 1 - width / 2, span = GH - 3 - width;
        using var clip = c.Clip(GX, GY, GW, GH);
        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = 0; i < n; i++)
            {
                float value = pass == 0 ? snap[i].Throttle : snap[i].Brake;
                _tracePoints[i] = new(GX + GW - (float)((now - snap[i].T) / win * GW),
                    bottom - Math.Clamp(value, 0, 1) * span);
            }
            double tail = Math.Min(now, snap[n - 1].T + HoldSeconds);
            _tracePoints[n] = new(GX + GW - (float)((now - tail) / win * GW), _tracePoints[n - 1].Y);
            c.Polyline(_tracePoints.AsSpan(0, n + 1), pass == 0 ? thrColor : brkColor, width);
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

    static void DrawGear(ThemeCanvas c, Theme.Theme t, float GearCx, int gear, double speed, string unit)
    {
        c.Text("GEAR", t.Label with { Size = 21 }, GearCx - 50, 11, 100, 30, t.TitleColor, HAlign.Center, t.TextShadow);
        string g = gear switch { < 0 => "R", 0 => "N", _ => gear.ToString(CultureInfo.InvariantCulture) };
        c.FillRoundRect(GearCx - 30, 45, 60, 57, t.BoxRadius, t.AccentFill);
        // A fonte de numeros do f1-1998 (F1 Broadcast 98 Values) so tem digitos: "N" e "R" caiam numa fonte do sistema fina e
        // destoante. Letras usam a fonte do titulo (mesma familia/peso do "GEAR" e do "KPH").
        var gf = g.Length == 1 && !char.IsDigit(g[0]) && t.Numbers.Family.StartsWith("F1 Broadcast", StringComparison.Ordinal) ? t.Title with { Element = "value", Size = 36 } : t.Numbers;
        if (t.Style == ThemeStyle.Modern2018) gf = t.Numbers with { Weight = Math.Max(t.Numbers.Weight, 700), Size = 30 };   // caixa branca com número preto em negrito
        c.Text(g, gf, GearCx - 30, 43, 60, 57, t.AccentInk, HAlign.Center);
        c.Text(Math.Round(speed).ToString("0", CultureInfo.InvariantCulture), t.Numbers, GearCx - 60, 108, 120, 40, t.ValueColor, HAlign.Center, t.ValueShadow);
        c.Text(unit, t.Label with { Size = 21 }, GearCx - 50, 144, 100, 28, t.TitleColor, HAlign.Center, t.TextShadow);
    }

    // ---- Estilo 2004-2008: cluster tacÃ´metro + marcha/pedais + barra de velocidade em arco (ref. f1-2000s-speedo-dial) ----
    // Coordenadas de projeto = pixels da referÃªncia ampliados 4x (origem no canto do tacÃ´metro).
    const float TCx = 172, TCy = 170, TDiscR = 170;           // tacÃ´metro
    const float CellX = 202, CellW = 148, CellH = 38, CellPitch = 44, CellY0 = 175;
    const float ACx = 182, ACy = 328, ARin = 66, ARout = 114; // arco da barra de velocidade (centro, raios interno/externo)
    const float BarX0 = 10, SpdY = 394, SpdH = 48;            // trecho reto da barra (12 segmentos verdes, 0-240 km/h)
    const float ClusterW = 360, ClusterH = 490, GraphW = 342, GraphH = 84;

    static Color4 Rgb(int r, int g, int b, float a = 1f) => new(r / 255f, g / 255f, b / 255f, a);
    static readonly Color4 PanelDark = new(0.04f, 0.04f, 0.05f, 0.72f);
    static readonly Color4 SegOff = new(0.26f, 0.27f, 0.29f, 0.55f);

    static (float X, float Y) Pol(float cx, float cy, float r, double deg) =>
        (cx + r * (float)Math.Cos(deg * Math.PI / 180), cy + r * (float)Math.Sin(deg * Math.PI / 180));

    /// <summary>Setor anular (raios r0..r1, Ã¢ngulos de tela em graus) preenchido por raios finos sobrepostos.</summary>
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

    /// <summary>Ponteiro afilado do cubo atÃ© <paramref name="len"/> (largura w0 no cubo, w1 na ponta).</summary>
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

    /// <summary>Fim da escala do tacÃ´metro em milhares de rpm: 18900 -> 20 (como na referÃªncia); sem dado, 20.</summary>
    static int ScaleEnd(double maxRpm) => maxRpm > 1000 ? Math.Clamp((int)Math.Ceiling(maxRpm / 1000.0) + 1, 8, 30) : 20;
    static int ScaleStart(int end) => end >= 12 ? 6 : 0;

    bool Speedo => _cfg.ColumnVisible("speedo");

    /// <summary>Altura das cÃ©lulas empilhadas quando o velocÃ­metro estÃ¡ oculto (marcha + velocidade, pedais).</summary>
    float StackH => (_cfg.ColumnVisible("gear") ? 2 * CellPitch : 0) + (_cfg.ColumnVisible("bars") ? 2 * CellPitch : 0);
    /// <summary>Largura da placa do grÃ¡fico: 342 x largura configurada da coluna "graph".</summary>
    float PlateW => MathF.Round(_cfg.Width("graph", GraphW));

    void DrawAnalog(ThemeCanvas c, Theme.Theme t, OverlayModel m)
    {
        var p = m.Session?.Player;
        bool live = m.Connected && p is not null;
        var telemetry = live ? RenderPlayerTelemetry.Read(m.Inputs, p!) : default;
        double kph = telemetry.SpeedMps * 3.6;
        double rpm = telemetry.Rpm;
        var white = new Color4(1, 1, 1, 1);
        var shadow = new ShadowToken(1.5f, 1.5f, new Color4(0, 0, 0, 0.7f));
        var cf = t.Label with { Size = 26 };
        string speedTxt = Math.Round(DisplayFormat.Speed(telemetry.SpeedMps, Unit)).ToString("0", CultureInfo.InvariantCulture) + " " + DisplayFormat.SpeedLabelShort(Unit);

        if (Speedo) DrawCluster(c, t, live, kph, rpm, telemetry.MaxRpm, speedTxt, white, shadow);

        // ---- Marcha e pedais: Ã  direita do tacÃ´metro; sem o velocÃ­metro, empilhados no canto (marcha, velocidade, pedais) ----
        float cx = Speedo ? CellX : 0, y = Speedo ? CellY0 : 0;
        if (_cfg.ColumnVisible("gear"))
        {
            string g = !live ? "-" : telemetry.Gear switch { < 0 => "R", 0 => "N", _ => telemetry.Gear.ToString(CultureInfo.InvariantCulture) };
            Chrome.WhiteCell(c, cx, y, CellW, CellH, "", cf);
            c.Text("Gear", cf, cx, y - 1, CellW * 0.62f, CellH, t.NameCellInk, HAlign.Center);
            c.Text(g, cf with { Element = "value" }, cx + CellW * 0.62f, y - 1, CellW * 0.38f - 12, CellH, t.NameCellInk, HAlign.Right);
            y += CellPitch;
            if (!Speedo) { Chrome.BlackCell(c, cx, y, CellW, CellH, speedTxt, cf with { Size = 24, Element = "value" }, HAlign.Center); y += CellPitch; }
        }
        if (_cfg.ColumnVisible("bars"))
        {
            PedalCell(c, cx, y, "Throttle", telemetry.Throttle, [new(0f, Rgb(96, 230, 96)), new(0.35f, Rgb(26, 185, 23)), new(1f, Rgb(12, 112, 20))], cf, shadow);
            PedalCell(c, cx, y + CellPitch, "Brake", telemetry.Brake, [new(0f, Rgb(244, 84, 64)), new(0.35f, Rgb(210, 28, 20)), new(1f, Rgb(120, 8, 8))], cf, shadow);
        }

    }

    void DrawCluster(ThemeCanvas c, Theme.Theme t, bool live, double kph, double rpm, double maxRpm, string speedTxt, Color4 white, ShadowToken shadow)
    {
        // PainÃ©is translÃºcidos: disco do tacÃ´metro e placa da barra de velocidade.
        c.FillEllipse(TCx, TCy, TDiscR, TDiscR, PanelDark);
        c.FillRoundRect(0, 330, 346, ClusterH - 330, 28, PanelDark);

        // ---- TacÃ´metro ----
        int end = ScaleEnd(live ? maxRpm : 0), start = ScaleStart(end);
        double step = 270.0 / (end - start);
        double AngleOf(double r) => 90 + Math.Clamp(r / 1000.0 - start, 0, end - start) * step;
        Wedge(c, TCx, TCy, 101, 113, 90, 360, white);   // anel grosso
        Wedge(c, TCx, TCy, 92, 94.6f, 90, 360, white);  // anÃ©is finos
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

        // ---- Barra de velocidade em arco: 12 verdes (0-240), 3 amarelos (240-280), 3 laranjas (280-320), 1 vermelho (320-340) km/h ----
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
        (int, double)[] marks = [(240, 85), (280, 47), (320, 16), (340, -8)];
        foreach (var (kmh, a) in marks)
        {
            // A escala da barra Ã© em km/h; em mph sÃ³ os rÃ³tulos mudam (149 / 174 / 199 / 211).
            string txt = Math.Round(DisplayFormat.SpeedFromKph(kmh, Unit)).ToString("0", CultureInfo.InvariantCulture);
            var (lx, ly) = Pol(ACx, ACy, 140, a);
            c.Text(txt, lf with { Element = "value" }, lx - 30, ly - 13, 60, 26, white, HAlign.Center, shadow);
        }
        c.Text(speedTxt, lf with { Element = "value" }, BarX0, 455, 130, 26, white, HAlign.Left, shadow);
    }

    // GrÃ¡fico 2004â€“2008: placa escura arredondada como a da barra de velocidade, legendas em cÃ©lulas da transmissÃ£o (verde "Throttle",
    // vermelha "Brake", branca "10 s"), grade fina e as duas linhas.
    const float PlatePad = 10, CapH = 24, PlotTop = 6 + CapH + 8, PlateBottom = 10;

    void DrawGraph2004(ThemeCanvas c, Theme.Theme t, OverlayModel m, bool live, float y0, ShadowToken shadow)
    {
        float w = PlateW, h = PlotTop + GraphH + PlateBottom;
        c.FillRoundRect(0, y0, w, h, 18, PanelDark);
        var capF = t.Label with { Size = 16 };
        float x = PlatePad;
        x += Chrome.Caption(c, x, y0 + 6, "Throttle", CapH, capF, Chrome.CellKind.Green) + 4;
        Chrome.Caption(c, x, y0 + 6, "Brake", CapH, capF, Chrome.CellKind.Red);
        float tw = c.Measure("10 s", capF) + 18;
        Chrome.Caption(c, w - PlatePad - tw, y0 + 6, "10 s", CapH, capF);

        float gx = PlatePad, gy = y0 + PlotTop, gw = w - 2 * PlatePad;
        c.FillRect(gx, gy, gw, GraphH, new Color4(0, 0, 0, 0.35f));
        var grid = new Color4(1, 1, 1, 0.13f);
        for (int i = 1; i < 4; i++) c.Line(gx, gy + GraphH * i / 4f, gx + gw, gy + GraphH * i / 4f, grid, 1);
        c.Line(gx, gy + GraphH, gx + gw, gy + GraphH, new Color4(1, 1, 1, 0.55f), 1.5f);
        if (live) DrawTrace(c, t, m.Inputs, gx, gy, gw, GraphH, Rgb(52, 208, 82), Rgb(236, 44, 36), 2.6f);
        else c.Text(m.Connected ? "NO DATA" : "WAITING FOR AMS2", capF with { Size = 20 }, gx + 8, gy + GraphH / 2 - 14, gw - 16, 28, new Color4(1, 1, 1, 0.85f), shadow: shadow);
    }

    (float Width, float Height) AnalogSize()
    {
        float w = Speedo ? ClusterW : StackH > 0 ? CellW : 0, h = Speedo ? ClusterH : StackH > 0 ? StackH - (CellPitch - CellH) : 0;
        return w <= 0 ? (CellW, CellH) : (w, h);
    }

    /// <summary>CÃ©lula escura que enche (verde/vermelho) proporcionalmente ao valor, com o rÃ³tulo branco por cima.</summary>
    static void PedalCell(ThemeCanvas c, float x, float y, string label, double value, BarStop[] stops, FontToken font, ShadowToken shadow)
    {
        c.FillRect(x, y + CellH, CellW, 1.5f, new Color4(0.04f, 0.04f, 0.08f, 0.6f));
        c.FillRect(x, y, CellW, CellH, new Color4(0.09f, 0.1f, 0.11f, 0.9f));
        float fw = (float)Math.Clamp(value, 0, 1) * CellW;
        if (fw > 0.5f) c.VGradientRect(x, y, fw, CellH, stops);
        c.Text(label, font, x, y - 1, CellW, CellH, new Color4(1, 1, 1, 1), HAlign.Center, shadow);
    }
}
