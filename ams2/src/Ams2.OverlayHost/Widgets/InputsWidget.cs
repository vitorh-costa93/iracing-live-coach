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
    public (float Width, float Height) DesignSize => (692, 197);

    // Gráfico
    const float GX = 21, GY = 78, GW = 408, GH = 88;
    // Barras verticais
    const float BarW = 22, BarY = 46, BarH = 109, ThrX = 465, BrkX = 519;
    // Marcha / velocidade
    const float GearCx = 628;

    WidgetSettings _cfg = new() { Id = "inputs" };
    public void Configure(WidgetSettings s) => _cfg = s;

    public void Draw(ThemeCanvas c, OverlayModel m)
    {
        var t = c.Theme;
        var (w, h) = DesignSize;
        c.Panel(0, 0, w, h);
        Chrome.Header(c, "INPUTS", 19, 11, 150);
        bool graph = _cfg.ColumnVisible("graph"), bars = _cfg.ColumnVisible("bars"), gear = _cfg.ColumnVisible("gear");
        if (graph) { DrawLegend(c, t); DrawGraphFrame(c, t); }

        var p = m.Session?.Player;
        if (!m.Connected || p is null)
        {
            c.Text(m.Connected ? "NO DATA" : "WAITING FOR AMS2", t.Label, GX + 10, GY + 28, 380, 30, t.LabelColor, shadow: t.TextShadow);
            return;
        }

        if (graph) DrawTrace(c, t, m.InputHistory, m.Now);
        if (bars)
        {
            DrawBar(c, t, ThrX, p.Inputs.Throttle, t.ThrottleColor, "THR");
            DrawBar(c, t, BrkX, p.Inputs.Brake, t.BrakeColor, "BRK");
        }
        if (gear) DrawGear(c, t, p.Gear, p.SpeedMps * 3.6);
    }

    static void DrawLegend(ThemeCanvas c, Theme.Theme t)
    {
        var f = t.Label with { Size = 17 };
        (string, Color4)[] items = [("THROTTLE", t.ThrottleColor), ("BRAKE", t.BrakeColor), ("STEERING", t.SteeringColor)];
        for (int i = 0; i < items.Length; i++)
        {
            float y = 14 + i * 21;
            c.FillRect(314, y + 7, 20, 8, items[i].Item2);
            c.Text(items[i].Item1, f, 342, y, 110, 22, t.TitleColor, shadow: t.TextShadow);
        }
    }

    static void DrawGraphFrame(ThemeCanvas c, Theme.Theme t)
    {
        c.FillRect(GX, GY, GW, GH, new Color4(0, 0, 0, 0.22f));
        c.Line(GX, GY, GX, GY + GH, t.GraphAxis, 2);
        c.Line(GX, GY + GH, GX + GW, GY + GH, t.GraphAxis, 2);
        var f = t.Label with { Size = 16 };
        c.Text("10s", f, GX, GY + GH + 2, 50, 22, t.TitleColor, shadow: t.TextShadow);
        c.Text("0s", f, GX + GW - 50, GY + GH + 2, 50, 22, t.TitleColor, HAlign.Right, t.TextShadow);
    }

    /// <summary>Uma coluna de pixel por vez: valor interpolado no instante da coluna (10 s = largura do gráfico).</summary>
    static void DrawTrace(ThemeCanvas c, Theme.Theme t, IReadOnlyList<InputSample> hist, double now)
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

    static void DrawGear(ThemeCanvas c, Theme.Theme t, int gear, double kph)
    {
        c.Text("GEAR", t.Label with { Size = 21 }, GearCx - 50, 11, 100, 30, t.TitleColor, HAlign.Center, t.TextShadow);
        c.FillRect(GearCx - 30, 45, 60, 57, t.AccentFill);
        string g = gear switch { < 0 => "R", 0 => "N", _ => gear.ToString(CultureInfo.InvariantCulture) };
        c.Text(g, t.Numbers, GearCx - 30, 43, 60, 57, t.AccentInk, HAlign.Center);
        c.Text(Math.Round(kph).ToString("0", CultureInfo.InvariantCulture), t.Numbers, GearCx - 60, 108, 120, 40, t.ValueColor, HAlign.Center, t.ValueShadow);
        c.Text("KPH", t.Label with { Size = 21 }, GearCx - 50, 144, 100, 28, t.TitleColor, HAlign.Center, t.TextShadow);
    }
}
