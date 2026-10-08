using System.Globalization;
using System.Numerics;
using Ams2.Core;
using Ams2.Core.Calc;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Theme;
using Ams2.Shared.Profiles;
using Vortice.Win32.Numerics;

namespace Ams2.OverlayHost.Widgets;

/// <summary>
/// Painel "RACE START 0-200km/h" do gráfico de TV 2018 (ref. f1-2018-crops-livespeed-racestart-radio-caption.jpg, 2º recorte): placa preta
/// com filete vermelho no topo, título branco em duas linhas ("RACE START" / "0-200km/h") e, por linha, uma faixa preta com o tique da cor
/// da classe + nome em negrito e embaixo uma faixa cinza-escura com o tempo grande ("4.6" + "s" pequeno). Linhas: o jogador (última largada,
/// <see cref="LaunchTracker"/>) e "BEST" (melhor anterior da pista+carro). Aparece ao alcançar o alvo e fica showFor s.
/// Exclusivo do tema f1-2018. Opções (WidgetCatalog.OptionsFor("f1-2018", "racestart")): target (100/200), showBest, showFor, always.
/// </summary>
public sealed class RaceStartWidget : IWidget
{
    public string Id => "racestart";
    public const float W = 300, HeadH = 88, NameH = 40, TimeH = 62, RowH = NameH + TimeH;
    public (float Width, float Height) DesignSize => (W, HeadH + RowH * (ShowBest ? 2 : 1));
    WidgetSettings _cfg = new() { Id = "racestart" };
    readonly Broadcast18Motion _motion18 = new();
    public void Configure(WidgetSettings s) => _cfg = s;

    int Target => _cfg.OptionOr("target", "200") == "100" ? 100 : 200;
    bool ShowBest => !string.Equals(_cfg.OptionOr("showBest", "true"), "false", StringComparison.OrdinalIgnoreCase);
    double ShowFor => double.TryParse(_cfg.OptionOr("showFor", "10"), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? Math.Clamp(v, 5, 30) : 10;
    bool Always => string.Equals(_cfg.OptionOr("always", "false"), "true", StringComparison.OrdinalIgnoreCase);
    /// <summary>Opção "always": o host deixa a janela aberta mesmo sem o jogador no carro.</summary>
    public bool IgnoresDrivingGate => Always;

    static Color4 Rgb(int r, int g, int b, float a = 1f) => new(r / 255f, g / 255f, b / 255f, a);
    static readonly Color4 Red = Rgb(225, 6, 0), Plate = Rgb(0, 0, 0, 0.9f), NameStrip = Rgb(0, 0, 0, 0.94f), TimeStrip = Rgb(34, 36, 40, 0.88f);

    /// <summary>Alfa do painel: com "always" 1; senão aparece quando o alvo é alcançado e some após showFor s (com fade).</summary>
    public float Alpha(OverlayModel m)
    {
        if (Always) return 1f;
        var last = m.Launch?.Last;
        if (last?.Time(Target) is null) return 0f;
        return BroadcastUi.Fade(m.Now - last.At(Target), ShowFor);
    }

    public bool IsIdle(OverlayModel model) => Alpha(model) <= 0.01f;

    public void Draw(ThemeCanvas c, OverlayModel m)
    {
        if (!m.Connected) { _motion18.Reset(); return; }
        var launch = m.Launch ?? LaunchState.Empty;
        var last = launch.Last;
        int target = Target;
        // BEST = melhor ANTES da última largada; sem largada nesta pista+carro, o melhor guardado.
        double? mine = last?.Time(target), best = last is not null ? last.PrevBest(target) : launch.StoredBest(target);
        var car = m.Session?.PlayerCar;
        string name = car is null ? "" : _cfg.Name(car, BroadcastUi.ShortName(car, m.Session!.Cars)).ToUpperInvariant();
        int ci = car is null ? 0 : Math.Max(0, m.Session!.Cars.Select(f => f.ClassName).Distinct().ToList().IndexOf(car.ClassName));
        var tick = Chrome.ClassTick(ci);

        double at = last?.At(target) ?? double.NaN;
        float reveal = Always ? 1 : mine is null ? 0 : _motion18.Evaluate(m.Now, at, at + ShowFor, BroadcastUi.State(m).SessionSeenT);
        BroadcastUi.WithReveal18(c, reveal, DesignSize.Width, DesignSize.Height, () =>
        {
            var t = c.Theme;
            var (w, h) = DesignSize;
            c.FillRect(0, 0, w, HeadH, Plate);
            c.FillRect(0, 0, w, 5, t.AccentBar.A > 0 ? t.AccentBar : Red);
            var title = t.Title with { Weight = 400, Size = 27, Tracking = 0.5f };
            c.Text("RACE START", title, 0, 7, w, 40, t.TitleColor, HAlign.Center);
            c.Text($"0-{target}km/h", title, 0, 44, w, 40, t.TitleColor, HAlign.Center);

            float y = HeadH;
            Row(c, t, y, w, name, tick, mine, last: !ShowBest);
            if (ShowBest) Row(c, t, y + RowH, w, "BEST", tick, best, last: true);
        });
    }

    void Row(ThemeCanvas c, Theme.Theme t, float y, float w, string name, Color4 tick, double? seconds, bool last)
    {
        c.FillRect(0, y, w, NameH, NameStrip);
        Chrome.Tick(c, 16, y + 9, NameH - 18, tick, 5);
        var nf = t.Text with { Weight = 700, Size = 23, Tracking = 0.5f };
        c.Text(name, BroadcastUi.Fit(c, name, nf, w - 46), 32, y, w - 40, NameH, t.TextColor);

        float ty = y + NameH;
        if (last)
        {
            // Última faixa: canto inferior direito arredondado (como a placa do Live Speed e do Pit Lane).
            const float r = 12;
            Span<Vector2> pts = stackalloc Vector2[3 + 9 + 1];
            pts[0] = new(0, ty); pts[1] = new(w, ty); pts[2] = new(w, ty + TimeH - r);
            for (int i = 1; i <= 9; i++)
            {
                float a = MathF.PI / 2 * i / 9;
                pts[2 + i] = new(w - r + r * MathF.Cos(a), ty + TimeH - r + r * MathF.Sin(a));
            }
            pts[12] = new(0, ty + TimeH);
            c.FillPolygon(pts, TimeStrip);
        }
        else c.FillRect(0, ty, w, TimeH, TimeStrip);

        string value = Format(seconds);
        var big = t.Numbers with { Element = "time", Weight = 400, Size = 46, Tracking = 0.5f };
        var small = t.Label with { Weight = 400, Size = 21, Tracking = 0f };
        float bw = c.Measure(value, big), sw = seconds is null ? 0 : c.Measure("s", small) + 4;
        float x0 = MathF.Round((w - bw - sw) / 2);
        c.Text(value, big, x0, ty + 2, bw + 8, TimeH - 4, t.TextColor);
        if (seconds is not null) c.Text("s", small, x0 + bw + 4, ty + 22, sw + 6, TimeH - 26, t.TextColor);
    }

    /// <summary>Tempo com um decimal ("4.6"); sem tempo, "-.-".</summary>
    public static string Format(double? seconds)
        => seconds is { } s && s > 0 ? (Math.Round(s, 1)).ToString("0.0", CultureInfo.InvariantCulture) : "-.-";
}
