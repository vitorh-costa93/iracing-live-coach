using System.Globalization;
using Ams2.Core.Calc;
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
///                       [--widget relative|standings|fuel|tyres|weather|inputs|lapcounter|drivercaption|pitstops|pittimer|winner|board] [--sim N] [--x N] [--y N] [--seconds N]
///                       [--cols id,id|none|all] [--rows N] [--top N] [--near N] [--font FAMILIA] [--opacity 0.2..1]   (so com --png: configura o widget como o perfil)
///                       [--pipe NOME] [--profiles-dir PASTA] [--profile NOME] [--edit]
///   Sem --widget: uma janela por widget, configuradas pelo perfil ativo (%AppData%\ams2-live-coach) e controladas pelo Control Center (IPC).
///   Com --widget: so aquele widget, sem salvar perfil (--x/--y/--scale sobrescrevem).
///   --edit   inicia no modo de edicao do layout (Ctrl+Alt+E alterna; arraste, roda ou canto para escalar).
///   --measure (so com --fake e --seconds): ao sair imprime fps de render por janela, passos do provider e taxa de amostragem das entradas ([FPS] ...).
///   --fake   usa o escritor falso em processo (sem o jogo).
///   --png    renderiza um quadro do widget Relative para o arquivo e sai (usa --fake, a menos que --real).
///   Variaveis do --fake: AMS2_FAKE_PITS=1, AMS2_FAKE_FINISH=1, AMS2_FAKE_GEAR/KPH/RPM/MAXRPM e AMS2_FAKE_BOARD=1 (corrida de
///   20 carros com volta de ~20 s para o widget rotativo inferior; linha do tempo em ams2/reference/board-spec.md).
///   Sair do overlay: Ctrl+Alt+Q (a janela não recebe foco nem cliques) ou --seconds.
/// </summary>
internal static class Program
{
    sealed record Options(bool Fake, string? Png, bool Real, string? ThemeId, float? Scale, string Bg, int? X, int? Y, double Seconds, string? Widget, double Sim,
        string Pipe, string? ProfilesDir, string? Profile, bool Edit, string? Cols, int? Rows, string? Font, float? Opacity, int? Top, int? Near, bool Measure);

    [STAThread]
    static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        Win32.AttachConsole(-1);
        try
        {
            if (args.Contains("--dump-sizes")) return DumpSizes();
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
            Edit: a.Contains("--edit"),
            Cols: Val("--cols"),
            Rows: Val("--rows") is { } rw ? int.Parse(rw) : null,
            Font: Val("--font"),
            Opacity: Val("--opacity") is { } op ? float.Parse(op, CultureInfo.InvariantCulture) : null,
            Top: Val("--top") is { } tp ? int.Parse(tp) : null,
            Near: Val("--near") is { } nr ? int.Parse(nr) : null,
            Measure: a.Contains("--measure"));
    }

    /// <summary>Imprime "tema widget largura altura" (unidades de design, perfil padrao do tema) para o teste de sobreposicao conferir a tabela de WidgetLayout.</summary>
    static int DumpSizes()
    {
        foreach (var theme in Themes.All)
        {
            var profile = ProfileFactory.CreateDefault("Padrão", theme.Id);
            foreach (var s in profile.Widgets)
            {
                var widget = WidgetRegistry.Create(s.Id);
                widget.UseTheme(theme);
                widget.Configure(s.Normalized());
                Console.WriteLine($"{theme.Id} {s.Id} {widget.DesignSize.Width.ToString(CultureInfo.InvariantCulture)} {widget.DesignSize.Height.ToString(CultureInfo.InvariantCulture)}");
            }
        }
        return 0;
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
        // Configuracao do widget como no perfil: --cols = ids das colunas VISIVEIS separados por virgula ("none" = nenhuma, omitido = todas).
        var settings = new WidgetSettings
        {
            Id = widget.Id, Scale = scale, Rows = o.Rows, TopCount = o.Top, NearCount = o.Near, Font = o.Font, Opacity = o.Opacity ?? 1f,
            Columns = o.Cols is null || o.Cols == "all" ? null : o.Cols == "none" ? [] : o.Cols.Split(',', StringSplitOptions.RemoveEmptyEntries),
        }.Normalized();
        widget.Configure(settings);
        int w = (int)Math.Ceiling(widget.DesignSize.Width * scale), h = (int)Math.Ceiling(widget.DesignSize.Height * scale);
        using var gfx = DeviceResources.CreateOffscreen(w, h);
        Console.WriteLine($"[Fonts] dir={gfx.Fonts.Directory} families=[{string.Join(", ", gfx.Fonts.Families)}]");
        using var canvas = new ThemeCanvas(gfx, theme, scale) { Opacity = settings.Opacity, FontOverride = settings.Font };
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

    /// <summary>Relatório do --measure (descarta 1 s de aquecimento). Só faz sentido com o escritor falso.</summary>
    static void PrintMeasure(Options o, HostController host, OverlayDataProvider provider, double fromT)
    {
        Console.WriteLine($"[FPS] fake={o.Fake} seconds={o.Seconds}");
        foreach (var (id, st) in host.RenderStats) Console.WriteLine($"[FPS] render {id,-14} {st.Report(fromT)}");
        Console.WriteLine($"[FPS] provider.tick          {provider.TickStats.Report(fromT)}");
        Console.WriteLine($"[FPS] inputs.amostragem      {provider.InputStats.Report(fromT)}");
    }

    static int RunOverlay(Options o)
    {
        if (o.Measure && !o.Fake) { Console.Error.WriteLine("--measure exige --fake (nao mede o jogo real)."); return 2; }
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
        double t0 = RateStats.Now();
        host.Run(o.Seconds);
        if (o.Measure) PrintMeasure(o, host, provider, t0 + 1.0);
        return 0;
    }
}
