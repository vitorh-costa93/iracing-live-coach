using System.Globalization;
using Ams2.Core.Calc;
using Ams2.Core.Reading;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Host;
using Ams2.Shared.Ipc;
using Ams2.Shared.PlayerNames;
using Ams2.Shared.Profiles;
using Ams2.OverlayHost.Gfx;
using Ams2.OverlayHost.Native;
using Ams2.OverlayHost.Theme;
using Ams2.OverlayHost.Widgets;

namespace Ams2.OverlayHost;

/// <summary>
/// Uso: Ams2.OverlayHost [--fake] [--png arquivo] [--real] [--theme f1-1998] [--scale 1.0] [--bg RRGGBB|none]
///                       [--widget relative|standings|fuel|tyres|weather|inputs|lapcounter|drivercaption|pitstops|pittimer|winner|board|radar|livespeed|racestart|racecontrol|qualitower|qualilap|qualiresult] [--sim N] [--x N] [--y N] [--seconds N]
///                       [--cols id,id|none|all] [--rows N] [--top N] [--near N] [--font FAMILIA] [--opacity 0.2..1]   (so com --png: configura o widget como o perfil)
///                       [--radar-range 10..40] [--radar-sens 1..5]   (radar: alcance em metros e sensibilidade; so com --png)
///                       [--text-scale 0.6..2] [--settings ARQUIVO.json]   (so com --png: tamanho do texto, que redimensiona o widget; ou o WidgetSettings
///                       inteiro do perfil em JSON, com larguras/formato/cores, como o Control Center usa na previa; --scale continua valendo)
///                       [--pipe NOME] [--profiles-dir PASTA] [--profile NOME] [--edit]
///   Sem --widget: uma janela por widget, configuradas pelo perfil ativo (%AppData%\ams2-live-coach) e controladas pelo Control Center (IPC).
///   Com --widget: so aquele widget, sem salvar perfil (--x/--y/--scale sobrescrevem).
///   --edit   inicia no modo de edicao do layout (Ctrl+Alt+E alterna; arraste, roda ou canto para escalar).
///   --player-name NOME   forca o nome de exibicao do carro do jogador (so em memoria: nao grava player-names.json); sem ele o host usa os nomes por modelo do Control Center.
///   --measure (so com --fake e --seconds): ao sair imprime fps de render por janela, passos do provider e taxa de amostragem das entradas ([FPS] ...).
///   --fake   usa o escritor falso em processo (sem o jogo).
///   --png    renderiza um quadro do widget Relative para o arquivo e sai (usa --fake, a menos que --real).
///   Variaveis do --fake: AMS2_FAKE_PITS=1 (+ AMS2_FAKE_PITSTOP=N s parado do jogador), AMS2_FAKE_FINISH=1, AMS2_FAKE_GEAR/KPH/RPM/MAXRPM e AMS2_FAKE_RADAR=1 (4 carros orbitando
///   o jogador: frente, direita, atras, esquerda; ciclo de 12 s; com --png o radar fica sempre visivel; --cols none = so com carro proximo) e AMS2_FAKE_BOARD=1 (corrida de
///   20 carros com volta de ~20 s para o widget rotativo inferior; linha do tempo em ams2/reference/board-spec.md).
///   AMS2_FAKE_QUALI=1 (so --fake/--png): sessao de CLASSIFICACAO (filtro de sessao = "qualify") de 20 carros, pista de 2100 m, relogio 15:00
///   decrescente, tempos variados, OUT LAP (carros 15/16 saem da garagem em t=5/8 s), NO TIME (18/19 na garagem), IN PIT (17) e o jogador
///   (indice 5) em voltas de ~30,5 s com setores diferentes: cruza a linha em t=10,75 s e t~41,2 s (S1/S2 da volta 2 em t~21/31 s).
///   Com --png imprime a tabela ([QUALI]) e a volta do jogador ([QUALILAP]); qualitower desenhado no f1-2018 (placeholder nos outros temas); qualilap/qualiresult ainda sao placeholders.
///   Sair do overlay: Ctrl+Alt+Q (a janela não recebe foco nem cliques) ou --seconds.
/// </summary>
internal static class Program
{
    sealed record Options(bool Fake, string? Png, bool Real, string? ThemeId, float? Scale, string Bg, int? X, int? Y, double Seconds, string? Widget, double Sim,
        string Pipe, string? ProfilesDir, string? Profile, bool Edit, string? Cols, int? Rows, string? Font, float? Opacity, int? Top, int? Near, bool Measure, string? PlayerName = null, int? RadarRange = null, int? RadarSens = null,
        string? SettingsFile = null, float? TextScale = null);

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
            Measure: a.Contains("--measure"),
            PlayerName: Val("--player-name"),
            RadarRange: Val("--radar-range") is { } rr ? int.Parse(rr) : null,
            RadarSens: Val("--radar-sens") is { } rs ? int.Parse(rs) : null,
            SettingsFile: Val("--settings"),
            TextScale: Val("--text-scale") is { } ts ? float.Parse(ts, CultureInfo.InvariantCulture) : null);
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
        var names = PlayerNameStore.InMemory(); // a previa nunca toca no player-names.json do usuario
        // Largada: a previa nunca toca no launch.json do usuario (melhor anterior do AMS2_FAKE_LAUNCH so em memoria).
        using var provider = new OverlayDataProvider(FakeOrReal(fake, clock), clock, names: names, launch: fake ? FakeRawSource.FakeLaunchStore() : LaunchStore.InMemory());
        // Radar: sem --cols o previa usa o padrao do perfil (painel estilo V3; "native" liga o indicador nativo).
        string[]? cols = o.Cols is null || o.Cols == "all" ? (o.Widget == "radar" ? [] : null) : o.Cols == "none" ? [] : o.Cols.Split(',', StringSplitOptions.RemoveEmptyEntries);
        // --settings: o WidgetSettings do perfil em JSON (larguras, formato, texto...); as outras opcoes do widget na linha de comando nao se aplicam.
        WidgetSettings? fromFile = o.SettingsFile is { } sf ? System.Text.Json.JsonSerializer.Deserialize<WidgetSettings>(File.ReadAllText(sf), ProfileStore.Json) : null;
        provider.Radar.Options = RadarWidget.OptionsFor(fromFile is not null && o.Widget == "radar" ? (fromFile with { Id = "radar" }).Normalized()
            : new WidgetSettings { Id = "radar", RadarRange = o.RadarRange, RadarSensitivity = o.RadarSens, Columns = cols }.Normalized());
        if (o.PlayerName is not null) { provider.Tick(); if (provider.Current.Session?.PlayerCar is { } me) names.Set(me.CarName, o.PlayerName); }
        if (fake) for (int i = 0; i < (int)(o.Sim * 60); i++) { simNow += 1.0 / 60; provider.Tick(); }
        else while (wall.Elapsed.TotalSeconds < 3) { provider.Tick(); Thread.Sleep(16); }

        float scale = o.Scale ?? 1f;
        var theme = Themes.Get(o.ThemeId);
        var widget = WidgetRegistry.Create(o.Widget);
        widget.UseTheme(theme);
        // Configuracao do widget como no perfil: --cols = ids das colunas VISIVEIS separados por virgula ("none" = nenhuma, omitido = todas).
        var settings = (fromFile is not null ? fromFile with { Id = widget.Id, Scale = scale } : new WidgetSettings
        {
            Id = widget.Id, Scale = scale, Rows = o.Rows, TopCount = o.Top, NearCount = o.Near, Font = o.Font, Opacity = o.Opacity ?? 1f,
            RadarRange = o.RadarRange, RadarSensitivity = o.RadarSens,
            Columns = cols,
        }).Normalized();
        if (o.TextScale is { } tsc) settings = (settings with { TextScale = tsc }).Normalized();
        widget.Configure(settings);
        // Mesmo tamanho da janela real: escala de render = escala x tamanho do texto.
        float rs = settings.RenderScale;
        int w = (int)Math.Ceiling(widget.DesignSize.Width * rs), h = (int)Math.Ceiling(widget.DesignSize.Height * rs);
        using var gfx = DeviceResources.CreateOffscreen(w, h);
        Console.WriteLine($"[Fonts] dir={gfx.Fonts.Directory} families=[{string.Join(", ", gfx.Fonts.Families)}]");
        using var canvas = new ThemeCanvas(gfx, ThemeOverrides.Apply(theme, settings), rs) { Opacity = settings.Opacity, FontOverride = settings.Font };
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
        if (m.Radar is { } rf)
        {
            Console.WriteLine($"[RADAR] valido={rf.Valid} alcance={rf.RangeMeters:0}m carros={rf.Count} alertaE={rf.AlertLeft} alertaD={rf.AlertRight} aoLadoE={rf.AlongLeft} aoLadoD={rf.AlongRight} distE={rf.LeftGap:0.0} distD={rf.RightGap:0.0}");
            foreach (var rc in rf.Cars) Console.WriteLine($"   #{rc.Index} frente={rc.Forward:0.0} direita={rc.Right:0.0} {rc.Side} {rc.Zone} velRel={rc.RelSpeed:0.0}");
        }
        if (m.Session?.Kind == Ams2.Core.SessionKind.Qualify && m.Quali is { } q)
        {
            Console.WriteLine($"[QUALI] sessao={m.SessionGroup} restante={q.TimeRemaining:0.0}s linhas={q.Rows.Count}");
            foreach (var r in q.Rows)
                Console.WriteLine($"[QUALI] {r.Rank} #{r.Car.Index} {r.Car.Name} {r.Status} {(r.BestLap is { } b ? b.ToString("0.000", CultureInfo.InvariantCulture) : "-")} {(r.GapToFirst is { } g ? g.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture) : "-")}{(r.IsPlayer ? " (voce)" : "")}");
            if (m.QualiLap is { CarIndex: >= 0 } ql)
            {
                static string T(double? v) => v is { } x ? x.ToString("0.000", CultureInfo.InvariantCulture) : "-";
                Console.WriteLine($"[QUALILAP] volta={ql.Lap} setor={ql.Sector + 1} tempo={T(ql.Elapsed)} pit={ql.InPit} outlap={ql.OutLap} melhor={T(ql.PersonalBestLap)} lider={T(ql.LeaderBestLap)}#{ql.LeaderIndex}");
                Console.WriteLine($"[QUALILAP] setores={string.Join(" ", ql.Sectors.Select(x => x is null ? "-" : $"{T(x.Time)}:{x.Mark}"))} pessoais={string.Join(" ", ql.PersonalBestSectors.Select(T))} gerais={string.Join(" ", ql.OverallBestSectors.Select(T))}");
                if (ql.LastSplit is { } sp) Console.WriteLine($"[QUALILAP] parcial S{sp.Sector} {T(sp.Elapsed)} dPessoal={T(sp.DeltaPersonal)} dLider={T(sp.DeltaLeader)}");
                if (ql.LastResult is { } lr) Console.WriteLine($"[QUALILAP] resultado volta={lr.Lap} tempo={T(lr.LapTime)} pos={lr.Position} gap={T(lr.GapToFirst)} dPessoal={T(lr.DeltaPersonal)} melhorou={lr.Improved}");
            }
        }
        foreach (var r in m.Relative) Console.WriteLine($"   P{r.Car.Position} {RelativeWidget.Code(r.Car.Name)} {(r.IsPlayer ? "(voce)" : RelativeWidget.FormatGap(r))}");
        return 0;
    }

    /// <summary>--player-name: espera o primeiro quadro com o jogador (ate ~3 s) e define o nome para o modelo dele.</summary>
    static void ForceName(OverlayDataProvider provider, PlayerNameStore names, string name)
    {
        var until = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < until)
        {
            if (provider.Current.Session?.PlayerCar is { } me) { names.Set(me.CarName, name); return; }
            Thread.Sleep(20);
        }
    }

    /// <summary>Relatório do --measure (descarta 1 s de aquecimento). Só faz sentido com o escritor falso.</summary>
    static void PrintMeasure(Options o, HostController host, OverlayDataProvider provider, double fromT)
    {
        Console.WriteLine($"[FPS] fake={o.Fake} seconds={o.Seconds}");
        foreach (var (id, st) in host.RenderStats) Console.WriteLine($"[FPS] render {id,-14} {st.Report(fromT)}");
        Console.WriteLine($"[FPS] provider.tick          {provider.TickStats.Report(fromT)}");
        Console.WriteLine($"[FPS] inputs.amostragem      {provider.InputStats.Report(fromT)}");
        var cpu = System.Diagnostics.Process.GetCurrentProcess().TotalProcessorTime.TotalSeconds;
        Console.WriteLine($"[FPS] cpu do processo        {cpu / Math.Max(0.1, RateStats.Now() - (fromT - 1.0)) * 100:F1}% de 1 nucleo (media; inclui o aquecimento)  monitor={Win32.PrimaryRefreshHz()} Hz");
    }

    static int RunOverlay(Options o)
    {
        if (o.Measure && !o.Fake) { Console.Error.WriteLine("--measure exige --fake (nao mede o jogo real)."); return 2; }
        Win32.SetProcessDpiAwarenessContext(-4); // per-monitor v2
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Func<double> clock = () => sw.Elapsed.TotalSeconds;
        // Amostrador de entradas dedicado (fonte propria): grava na taxa do jogo, independente do passo de 60 Hz do provider.
        // Nomes por modelo: arquivo em --profiles-dir (ou %AppData%). O --fake sem --profiles-dir e o --player-name ficam so em memoria.
        var names = o.PlayerName is not null || (o.Fake && o.ProfilesDir is null) ? PlayerNameStore.InMemory() : new PlayerNameStore(o.ProfilesDir);
        // Melhores de largada (RACE START 2018): launch.json em --profiles-dir (ou %AppData%); --fake sem --profiles-dir fica so em memoria.
        var launch = o.Fake && o.ProfilesDir is null ? FakeRawSource.FakeLaunchStore() : new LaunchStore(LaunchStore.DefaultPath(o.ProfilesDir));
        using var provider = new OverlayDataProvider(FakeOrReal(o.Fake, clock), clock, inputSource: () => FakeOrReal(o.Fake, clock), names: names, launch: launch);
        provider.Start(60);
        if (o.PlayerName is not null) ForceName(provider, names, o.PlayerName);

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
