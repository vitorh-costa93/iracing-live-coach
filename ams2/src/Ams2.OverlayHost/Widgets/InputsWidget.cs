using System.Globalization;
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

        if (graph) DrawTrace(c, t, m.InputHistory, m.Now, lay.GraphX, GY, GW, GH);
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

    /// <summary>Uma coluna de pixel por vez: valor interpolado no instante da coluna (10 s = largura do gráfico).</summary>
    static void DrawTrace(ThemeCanvas c, Theme.Theme t, IReadOnlyList<InputSample> hist, double now, float GX, float GY, float GW, float GH)
    {
        if (hist.Count < 2) return;
        const double win = OverlayDataProvider.InputWindowSeconds;
        int cols = (int)GW;
        float[] thr = new float[cols], brk = new float[cols], str = new float[cols];
        bool[] ok = new bool[cols];
        int k = 0;
        for (int x = 0; x < cols; x++)
        {
            double tt = now - win + (x + 0.5) / cols * win;
            if (tt < hist[0].T || tt > hist[^1].T) continue;
            while (k < hist.Count - 2 && hist[k + 1].T < tt) k++;
            var a = hist[k]; var b = hist[k + 1];
            float f = b.T > a.T ? (float)Math.Clamp((tt - a.T) / (b.T - a.T), 0, 1) : 0;
            thr[x] = a.Throttle + (b.Throttle - a.Throttle) * f;
            brk[x] = a.Brake + (b.Brake - a.Brake) * f;
            str[x] = a.Steering + (b.Steering - a.Steering) * f;
            ok[x] = true;
        }

        float bottom = GY + GH - 1, span = GH - 4;
        Color4 Fill(Color4 col) => new(col.R, col.G, col.B, 0.5f);
        for (int x = 0; x < cols; x++)
        {
            if (!ok[x]) continue;
            float fx = GX + x;
            c.FillRect(fx, bottom - thr[x] * span, 1, thr[x] * span, Fill(t.ThrottleColor));
            c.FillRect(fx, bottom - brk[x] * span, 1, brk[x] * span, Fill(t.BrakeColor));
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

    // ---- Estilo 2004-2008: velocímetro analógico com pedais embutidos ----
    const float DialCx = 150, DialCy = 150, DialR = 142, DialMaxKph = 360;
    const float RpX = 322, RpW = 302;

    static (float X, float Y) Polar(float r, double deg) =>
        (DialCx + r * (float)Math.Cos(deg * Math.PI / 180), DialCy + r * (float)Math.Sin(deg * Math.PI / 180));

    static double DialAngle(double kph) => 135 + Math.Clamp(kph / DialMaxKph, 0, 1) * 270;

    void DrawAnalog(ThemeCanvas c, Theme.Theme t, OverlayModel m)
    {
        var (w, h) = DesignSize;
        c.Panel(0, 0, w, h);
        var p = m.Session?.Player;
        bool live = m.Connected && p is not null;
        double kph = live ? p!.SpeedMps * 3.6 : 0;
        var black = new Color4(0.03f, 0.03f, 0.05f, 1f);

        // Mostrador: aro claro, fundo preto, marcas e números da escala.
        c.FillEllipse(DialCx, DialCy, DialR, DialR, t.GraphAxis);
        c.FillEllipse(DialCx, DialCy, DialR - 5, DialR - 5, black);
        for (int v = 0; v <= (int)DialMaxKph; v += 10)
        {
            bool major = v % 40 == 0;
            var (x1, y1) = Polar(DialR - 10, DialAngle(v));
            var (x2, y2) = Polar(DialR - (major ? 26 : 18), DialAngle(v));
            c.Line(x1, y1, x2, y2, v >= 300 ? t.BrakeColor : t.TitleColor, major ? 2.6f : 1.4f);
            if (major)
            {
                var (tx, ty) = Polar(DialR - 45, DialAngle(v));
                c.Text(v.ToString(CultureInfo.InvariantCulture), t.Label with { Size = 17 }, tx - 24, ty - 12, 48, 24, t.TitleColor, HAlign.Center);
            }
        }
        c.Text("km/h", t.Label with { Size = 16 }, DialCx - 40, DialCy + 52, 80, 22, t.LabelColor, HAlign.Center);
        c.Text(Math.Round(kph).ToString("0", CultureInfo.InvariantCulture), t.Numbers with { Size = 30 }, DialCx - 50, DialCy + 74, 100, 36, t.TitleColor, HAlign.Center);

        // Ponteiro (com contrapeso) e cubo.
        double a = DialAngle(kph);
        var (nx, ny) = Polar(DialR - 22, a);
        var (bx, by) = Polar(-26, a);
        c.Line(bx, by, nx, ny, t.TitleColor, 4.5f);
        c.FillEllipse(DialCx, DialCy, 13, 13, t.TitleColor);
        c.FillEllipse(DialCx, DialCy, 5, 5, black);

        // Coluna da direita: blocos visíveis (marcha+RPM, pedais, gráfico) empilhados sem buracos.
        float y = 16;
        if (_cfg.ColumnVisible("gear"))
        {
            // RPM em LEDs (20 segmentos: verde, amarelo, vermelho).
            const int leds = 20;
            double frac = live && p!.MaxRpm > 0 ? Math.Clamp(p.Rpm / p.MaxRpm, 0, 1) : 0;
            int lit = (int)Math.Round(frac * leds);
            float lw = (RpW - (leds - 1) * 2) / leds;
            for (int i = 0; i < leds; i++)
            {
                var col = i < 12 ? t.ThrottleColor : i < 17 ? new Color4(0.95f, 0.8f, 0.1f, 1f) : t.BrakeColor;
                if (i >= lit) col = new Color4(col.R * 0.22f, col.G * 0.22f, col.B * 0.22f, 1f);
                c.FillRect(RpX + i * (lw + 2), y, lw, 20, col);
            }
            Chrome.Caption(c, RpX, y + 24, "RPM", 18, t.Label with { Size = 13 });
            string g = !live ? "-" : p!.Gear switch { < 0 => "R", 0 => "N", _ => p.Gear.ToString(CultureInfo.InvariantCulture) };
            Chrome.WhiteCell(c, RpX, y + 46, 90, 36, "Gear", t.Text);
            Chrome.Box(c, RpX + 90, y + 46, 50, 36, g, t.Numbers, Chrome.CellKind.Navy, HAlign.Center, 0);
            y += GearSectionH;
        }
        if (_cfg.ColumnVisible("bars"))
        {
            PedalBar(c, t, y, "THROTTLE", live ? p!.Inputs.Throttle : 0, t.ThrottleColor);
            PedalBar(c, t, y + 34, "BRAKE", live ? p!.Inputs.Brake : 0, t.BrakeColor);
            y += BarsSectionH;
        }
        if (_cfg.ColumnVisible("graph"))
        {
            // Gráfico dos últimos 10 s (mesmos dados do estilo padrão).
            const float gh = 84;
            DrawGraphFrame(c, t, RpX, y, RpW, gh);
            if (live) DrawTrace(c, t, m.InputHistory, m.Now, RpX, y, RpW, gh);
            else Chrome.Notice(c, m.Connected ? "NO DATA" : "WAITING FOR AMS2", RpX + 10, y + 24, 280);
        }
    }

    // Alturas dos blocos da coluna da direita (incluem a folga até o próximo; todos visíveis = mockup 640x300).
    const float GearSectionH = 92, BarsSectionH = 78, GraphSectionH = 84 + 26, AnalogPad = 4, DialW = 300;

    (float Width, float Height) AnalogSize()
    {
        float y = 16;
        bool any = false;
        if (_cfg.ColumnVisible("gear")) { y += GearSectionH; any = true; }
        if (_cfg.ColumnVisible("bars")) { y += BarsSectionH; any = true; }
        if (_cfg.ColumnVisible("graph")) { y += GraphSectionH; any = true; }
        return (any ? 640 : DialW, Math.Max(300, y + AnalogPad));
    }

    static void PedalBar(ThemeCanvas c, Theme.Theme t, float y, string label, double value, Color4 color)
    {
        // Rótulo em célula branca e barra em célula preta (estilo 2004–2008).
        const float lw = 104;
        Chrome.WhiteCell(c, RpX, y, lw, 28, label, t.Label with { Size = 17 });
        Chrome.Box(c, RpX + lw, y, RpW - lw, 28, "", t.Label, Chrome.CellKind.Black);
        c.FillRect(RpX + lw + 2, y + 2, (float)Math.Clamp(value, 0, 1) * (RpW - lw - 4), 24, color);
    }
}
