using System.Globalization;
using Ams2.Core.Reading;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Gfx;
using Ams2.OverlayHost.Native;
using Ams2.OverlayHost.Theme;
using Ams2.OverlayHost.Widgets;

namespace Ams2.OverlayHost;

/// <summary>
/// Uso: Ams2.OverlayHost [--fake] [--png arquivo] [--real] [--theme f1-1998] [--scale 1.0] [--bg RRGGBB|none]
///                       [--x N] [--y N] [--seconds N]
///   --fake   usa o escritor falso em processo (sem o jogo).
///   --png    renderiza um quadro do widget Relative para o arquivo e sai (usa --fake, a menos que --real).
///   Sair do overlay: Ctrl+Alt+Q (a janela não recebe foco nem cliques) ou --seconds.
/// </summary>
internal static class Program
{
    sealed record Options(bool Fake, string? Png, bool Real, string ThemeId, float Scale, string Bg, int? X, int? Y, double Seconds);

    [STAThread]
    static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        Win32.AttachConsole(-1);
        try
        {
            var o = Parse(args);
            return o.Png is not null ? RenderPng(o) : RunOverlay(o);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Ams2.OverlayHost] {ex}");
            return 1;
        }
    }

    static Options Parse(string[] a)
    {
        string? Val(string name) { int i = Array.IndexOf(a, name); return i >= 0 && i + 1 < a.Length ? a[i + 1] : null; }
        return new Options(
            Fake: a.Contains("--fake"),
            Png: Val("--png"),
            Real: a.Contains("--real"),
            ThemeId: Val("--theme") ?? Themes.F1_1998.Id,
            Scale: float.Parse(Val("--scale") ?? "1", CultureInfo.InvariantCulture),
            Bg: Val("--bg") ?? "5A6055",
            X: Val("--x") is { } x ? int.Parse(x) : null,
            Y: Val("--y") is { } y ? int.Parse(y) : null,
            Seconds: double.Parse(Val("--seconds") ?? "0", CultureInfo.InvariantCulture));
    }

    static IRawMemorySource FakeOrReal(bool fake, Func<double> clock) => fake ? new FakeRawSource(clock) : new MemoryMappedSource();

    /// <summary>Renderiza um quadro do Relative para PNG. Com o escritor falso, avança um relógio simulado
    /// por 40 s a 60 Hz para o GapTracker ter histórico; com --real, lê o jogo por ~3 s.</summary>
    static int RenderPng(Options o)
    {
        bool fake = !o.Real;
        double simNow = 0;
        var wall = System.Diagnostics.Stopwatch.StartNew();
        Func<double> clock = fake ? () => simNow : () => wall.Elapsed.TotalSeconds;
        using var provider = new OverlayDataProvider(FakeOrReal(fake, clock), clock);
        if (fake) for (int i = 0; i < 40 * 60; i++) { simNow += 1.0 / 60; provider.Tick(); }
        else while (wall.Elapsed.TotalSeconds < 3) { provider.Tick(); Thread.Sleep(16); }

        var theme = Themes.Get(o.ThemeId);
        var widget = new RelativeWidget();
        int w = (int)Math.Ceiling(widget.DesignSize.Width * o.Scale), h = (int)Math.Ceiling(widget.DesignSize.Height * o.Scale);
        using var gfx = DeviceResources.CreateOffscreen(w, h);
        Console.WriteLine($"[Fonts] dir={gfx.Fonts.Directory} families=[{string.Join(", ", gfx.Fonts.Families)}]");
        using var canvas = new ThemeCanvas(gfx, theme, o.Scale);
        gfx.BeginFrame();
        canvas.Begin();
        widget.Draw(canvas, provider.Current);
        canvas.End();
        gfx.EndFrame();

        (byte, byte, byte)? bg = o.Bg.Equals("none", StringComparison.OrdinalIgnoreCase) ? null
            : (Convert.ToByte(o.Bg[..2], 16), Convert.ToByte(o.Bg[2..4], 16), Convert.ToByte(o.Bg[4..6], 16));
        PngWriter.SaveFromPremultipliedBgra(o.Png!, gfx.ReadPixelsBgra(), w, h, bg);
        var m = provider.Current;
        Console.WriteLine($"[PNG] {o.Png} {w}x{h} tema={theme.Id} conectado={m.Connected} linhas={m.Relative.Count}");
        foreach (var r in m.Relative) Console.WriteLine($"   P{r.Car.Position} {RelativeWidget.Code(r.Car.Name)} {(r.IsPlayer ? "(voce)" : RelativeWidget.FormatGap(r))}");
        return 0;
    }

    static int RunOverlay(Options o)
    {
        Win32.SetProcessDpiAwarenessContext(-4); // per-monitor v2
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Func<double> clock = () => sw.Elapsed.TotalSeconds;
        using var provider = new OverlayDataProvider(FakeOrReal(o.Fake, clock), clock);
        provider.Start(60);

        var theme = Themes.Get(o.ThemeId);
        var widget = new RelativeWidget();
        int w = (int)Math.Ceiling(widget.DesignSize.Width * o.Scale), h = (int)Math.Ceiling(widget.DesignSize.Height * o.Scale);
        int x = o.X ?? 40;
        int y = o.Y ?? Math.Max(0, Win32.GetSystemMetrics(1) - h - 40);
        var window = OverlayWindow.Create("AMS2 Overlay - Relative", x, y, w, h);
        OverlayWindow.RegisterQuitHotkey();

        using var gfx = DeviceResources.CreateForWindow(window.Handle, w, h);
        using var canvas = new ThemeCanvas(gfx, theme, o.Scale);
        Console.WriteLine($"[Overlay] {w}x{h} em ({x},{y}), fake={o.Fake}, fontes=[{string.Join(", ", gfx.Fonts.Families)}]. Ctrl+Alt+Q sai.");

        var run = System.Diagnostics.Stopwatch.StartNew();
        while (OverlayWindow.Pump())
        {
            if (o.Seconds > 0 && run.Elapsed.TotalSeconds >= o.Seconds) break;
            gfx.BeginFrame();
            canvas.Begin();
            widget.Draw(canvas, provider.Current);
            canvas.End();
            gfx.EndFrame(); // Present(1): ritmo de ~60 Hz pelo vsync
        }
        window.Close();
        return 0;
    }
}
