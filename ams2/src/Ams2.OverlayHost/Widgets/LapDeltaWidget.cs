using System.Globalization;
using Ams2.Core.Calc;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Theme;
using Ams2.Shared.Profiles;
using Vortice.Win32.Numerics;

namespace Ams2.OverlayHost.Widgets;

/// <summary>
/// Lap Delta (estilo iRacing): a esquerda o delta ao vivo da volta atual contra a melhor volta observada (valor com sinal, verde = mais rapido,
/// vermelho = mais lento, e barra centrada em +-1 s); no meio as parciais S1 S2 S3 (tempo do setor e diferenca para o melhor setor pessoal; "BEST" quando
/// o setor e o melhor); a direita o tempo da ultima volta com a diferenca para o melhor anterior. Dados: LapDeltaTracker (tracado por distancia) e
/// QualiLapTracker (setores/ultima volta), ambos sobre a memoria compartilhada do AMS2. Mesmo desenho em todos os temas (cores do tema).
/// </summary>
public sealed class LapDeltaWidget : IWidget
{
    public string Id => "lapdelta";
    public bool HighFrequency => true;
    WidgetSettings _cfg = new() { Id = "lapdelta" };
    public void Configure(WidgetSettings s) => _cfg = s;

    public const float W = 740, H = 112;
    public (float Width, float Height) DesignSize => (W, H);

    /// <summary>Segundos em que os setores da volta recem-fechada continuam na tela (a volta seguinte ja zerou os setores).</summary>
    public const double SectorHold = 8;
    readonly ShowRef _show = new();
    /// <summary>Delta (s) que enche a barra para cada lado.</summary>
    public const double BarRange = 1.0;

    static Color4 Rgb(int r, int g, int b, float a = 1f) => new(r / 255f, g / 255f, b / 255f, a);
    static readonly Color4 Green = Rgb(37, 194, 58), Red = Rgb(232, 50, 50), Purple = Rgb(200, 120, 255);

    public static Color4 DeltaInk(double d) => d <= 0 ? Green : Red;

    /// <summary>Fracao da barra preenchida (-1..1); negativo = mais rapido.</summary>
    public static double BarFraction(double delta) => Math.Clamp(delta / BarRange, -1, 1);

    public void Draw(ThemeCanvas c, OverlayModel m)
    {
        var t = c.Theme;
        c.FillRect(0, 0, W, H, t.PanelFill);
        var q = m.Connected ? m.QualiLap : null;
        var ld = m.Connected ? m.LapDelta : null;
        var label = t.Label with { Size = 16, Tracking = 0.5f };

        // ---- Delta ao vivo ----
        const float lw = 260;
        c.Text("LAP DELTA", label, 12, 4, 130, 22, t.LabelColor);
        string best = ld?.BestLap is { } bl ? "BEST " + _cfg.Fmt.FormatLapTime(bl) : "NO REFERENCE";
        c.Text(best, label, 110, 4, lw - 122, 22, t.LabelColor, HAlign.Right);
        double? delta = ld?.Delta;
        string txt = delta is { } d ? _cfg.Fmt.FormatGap(d) : "--.---";
        var ink = delta is { } dd ? DeltaInk(dd) : t.ValueColor;
        c.Text(txt, BroadcastUi.Fit(c, txt, t.Numbers with { Size = 50, Element = "gap" }, lw - 24), 12, 24, lw - 24, 56, ink, HAlign.Center);
        const float bx = 14, by = 86, bw = lw - 28, bh = 16;
        c.FillRect(bx, by, bw, bh, Rgb(255, 255, 255, 0.10f));
        if (delta is { } dv)
        {
            double f = BarFraction(dv);
            float half = bw / 2, len = (float)(Math.Abs(f) * half);
            c.FillRect(f < 0 ? bx + half - len : bx + half, by, len, bh, DeltaInk(dv));
        }
        c.FillRect(bx + bw / 2 - 1, by - 3, 2, bh + 6, t.TextColor);
        c.FillRect(lw, 10, 1.5f, H - 20, Rgb(255, 255, 255, 0.25f));

        // ---- Parciais ----
        IReadOnlyList<QualiSector?> sectors = q is null ? [] : q.LastResult is { } lr && m.Now - lr.At >= 0 && m.Now - lr.At < _show.Get(_cfg, c.Theme.Style).Timing("sectors", SectorHold) ? lr.Sectors : q.Sectors;
        const float sx0 = lw + 8, sw = 104;
        for (int k = 0; k < 3; k++)
        {
            float x = sx0 + k * sw;
            var sec = k < sectors.Count ? sectors[k] : null;
            c.Text("S" + (k + 1), label, x, 4, sw, 22, t.LabelColor, HAlign.Center);
            string time = sec is null ? "--.---" : Time(sec.Time);
            c.Text(time, BroadcastUi.Fit(c, time, t.Numbers with { Size = 26, Element = "time" }, sw - 8), x, 28, sw, 36, sec is null ? t.LabelColor : t.ValueColor, HAlign.Center);
            string sub = ""; Color4 subInk = t.LabelColor;
            if (sec is not null)
            {
                switch (sec.Mark)
                {
                    case SectorMark.OverallBest: sub = "BEST"; subInk = Purple; break;
                    case SectorMark.PersonalBest: sub = "BEST"; subInk = Green; break;
                    case SectorMark.Slower:
                        if (q!.PersonalBestSectors.Count > k && q.PersonalBestSectors[k] is { } pb) { sub = _cfg.Fmt.FormatGap(sec.Time - pb); subInk = Red; }
                        break;
                }
            }
            c.Text(sub, t.Numbers with { Size = 22, Element = "gap" }, x, 66, sw, 30, subInk, HAlign.Center);
        }
        float lx = sx0 + 3 * sw + 8;
        c.FillRect(lx - 4, 10, 1.5f, H - 20, Rgb(255, 255, 255, 0.25f));

        // ---- Ultima volta ----
        float rw = W - lx - 8;
        c.Text("LAST LAP", label, lx, 4, rw, 22, t.LabelColor, HAlign.Center);
        var res = q?.LastResult;
        string lap = res is null ? "-:--.---" : res.Invalid ? "INVALID" : _cfg.Fmt.FormatLapTime(res.LapTime);
        var lapInk = res is null || res.Invalid ? t.LabelColor : res.Improved ? Green : t.ValueColor;
        c.Text(lap, BroadcastUi.Fit(c, lap, t.Numbers with { Size = 30, Element = "time" }, rw - 8), lx, 28, rw, 36, lapInk, HAlign.Center);
        string ld2 = res is { Invalid: false, DeltaPersonal: { } dp } ? _cfg.Fmt.FormatGap(dp) : "";
        c.Text(ld2, t.Numbers with { Size = 22, Element = "gap" }, lx, 66, rw, 30, res?.DeltaPersonal is { } x2 ? DeltaInk(x2) : t.LabelColor, HAlign.Center);
    }

    /// <summary>Tempo de setor com milesimos ("35.643"; acima de 1 min usa o formato de volta).</summary>
    string Time(double s) => s < 60 ? s.ToString("0.000", CultureInfo.InvariantCulture) : _cfg.Fmt.FormatLapTime(s);
}
