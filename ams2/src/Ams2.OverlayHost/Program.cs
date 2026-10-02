using System.Globalization;
using Ams2.Core.Reading;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Host;
using Ams2.Shared.Ipc;
using Ams2.Shared.Profiles;
using Ams2.OverlayHost.Gfx;
using Ams2.OverlayHost.Native;
using Ams2.OverlayHost.Theme;
using Ams2.OverlayHost.Widgets;

namespace Ams2.OverlayHost;

/// <summary>
/// Uso: Ams2.OverlayHost [--fake] [--png arquivo] [--real] [--theme f1-1998] [--scale 1.0] [--bg RRGGBB|none]
///                       [--widget relative|standings|fuel|tyres|weather|inputs] [--sim N] [--x N] [--y N] [--seconds N]
///                       [--pipe NOME] [--profiles-dir PASTA] [--profile NOME] [--edit]
///   Sem --widget: uma janela por widget, configuradas pelo perfil ativo (%AppData%\ams2-live-coach) e controladas pelo Control Center (IPC).
///   Com --widget: so aquele widget, sem salvar perfil (--x/--y/--scale sobrescrevem).
///   --edit   inicia no modo de edicao do layout (Ctrl+Alt+E alterna; arraste, roda ou canto para escalar).
///   --fake   usa o escritor falso em processo (sem o jogo).
///   --png    renderiza um quadro do widget Relative para o arquivo e sai (usa --fake, a menos que --real).
///   Sair do overlay: Ctrl+Alt+Q (a janela não recebe foco nem cliques) ou --seconds.
/// </summary>
internal static class Program
{
    sealed record Options(bool Fake, string? Png, bool Real, string? ThemeId, float? Scale, string Bg, int? X, int? Y, double Seconds, string? Widget, double Sim,
        string Pipe, string? ProfilesDir, string? Profile, bool Edit);

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
            ThemeId: Val("--theme"),
            Scale: Val("--scale") is { } sc ? float.Parse(sc, CultureInfo.InvariantCulture) : null,
            Bg: Val("--bg") ?? "5A6055",
            X: Val("--x") is { } x ? int.Parse(x) : null,
            Y: Val("--y") is { } y ? int.Parse(y) : null,
            Seconds: double.Parse(Val("--seconds") ?? "0", CultureInfo.InvariantCulture),
            Widget: Val("--widget"),
            Sim: double.Parse(Val("--sim") ?? "40", CultureInfo.InvariantCulture),
            Pipe: Val("--pipe") ?? IpcProtocol.DefaultPipeName,
            ProfilesDir: Val("--profiles-dir"),
            Profile: Val("--profile"),
            Edit: a.Contains("--edit"));
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
        if (fake) for (int i = 0; i < (int)(o.Sim * 60); i++) { simNow += 1.0 / 60; provider.Tick(); }
        else while (wall.Elapsed.TotalSeconds < 3) { provider.Tick(); Thread.Sleep(16); }

        float scale = o.Scale ?? 1f;
        var theme = Themes.Get(o.ThemeId);
        var widget = WidgetRegistry.Create(o.Widget);
        widget.UseTheme(theme);
        int w = (int)Math.Ceiling(widget.DesignSize.Width * scale), h = (int)Math.Ceiling(widget.DesignSize.Height * scale);
        using var gfx = DeviceResources.CreateOffscreen(w, h);
        Console.WriteLine($"[Fonts] dir={gfx.Fonts.Directory} families=[{string.Join(", ", gfx.Fonts.Families)}]");
        using var canvas = new ThemeCanvas(gfx, theme, scale);
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

        var store = new ProfileStore(o.ProfilesDir);
        bool single = o.Widget is not null;
        using var host = new HostController(provider, store, o.Fake, o.ThemeId, o.Profile, single ? [o.Widget!] : null, persist: !single);
        if (single && (o.X is not null || o.Y is not null || o.Scale is not null))
            host.ApplyPatch(o.Widget!, new WidgetPatch { X = o.X, Y = o.Y, Scale = o.Scale });
        OverlayWindow.RegisterHotkeys();
        host.StartIpc(o.Pipe);
        if (o.Edit) host.SetEditModeDirect(true);
        Console.WriteLine($"[Overlay] perfil='{host.Profile.Name}' tema={host.Profile.ThemeId} fake={o.Fake} pipe={o.Pipe}. Ctrl+Alt+Q sai, Ctrl+Alt+E edita o layout.");
        host.Run(o.Seconds);
        return 0;
    }
}
