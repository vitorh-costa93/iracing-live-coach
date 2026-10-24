using System.Globalization;
using Ams2.Core;
using Ams2.Core.Calc;
using Ams2.Core.Calc.GpxSat;
using Ams2.OverlayHost.Data;
using Ams2.Shared.Profiles;

namespace Ams2.OverlayHost.Widgets.GpxSat;

/// <summary>
/// Opcoes do widget "gpxsat" (gpxsat.ini do GPxPatch traduzido para o perfil; ids em <see cref="WidgetCatalog"/>). Padroes = os do GPx:
/// ShowMode 0, ShowTimeLeft 1, standings de corrida desligado, de classificacao ligado, SpeedSplit 0, FadingSpeed 10 (passo 2x = 20 ms).
/// </summary>
public sealed record GpxOptions
{
    /// <summary>ShowMode: "all" (0), "sky" (1: so sky messages + time left), "none" (2).</summary>
    public string ShowMode { get; init; } = "all";
    public bool ShowTimeLeft { get; init; } = true;
    /// <summary>Corrida: "off" (0), "standings" (1), "pitstops" (2).</summary>
    public string RaceStandings { get; init; } = "off";
    public bool QualiStandings { get; init; } = true;
    /// <summary>Corrida: "ahead" (0), "behind" (1), "trap" (2).</summary>
    public string SpeedSplitRace { get; init; } = "ahead";
    /// <summary>Fora da corrida: "split" (0), "trap" (1).</summary>
    public string SpeedSplitQuali { get; init; } = "split";
    public bool DirectorsCut { get; init; }
    public bool ShowLogo { get; init; }
    /// <summary>FadingSpeed do gpxsat.ini (ms por passo no GPx); o passo aqui e 2x (pedido: metade da velocidade real).</summary>
    public double FadingSpeed { get; init; } = 10;
    public double SkySeconds { get; init; } = 8;
    public double SplitSeconds { get; init; } = 8;
    public double PageSeconds { get; init; } = 6;
    public bool OriginalFonts { get; init; } = true;
    /// <summary>Standings de corrida por evento (como no GPxPatch): so aparecem, estaticos, numa janela finita depois que o lider fecha a volta; fora dela o slot fica vazio. Sem ciclo continuo.</summary>
    public bool Rotation { get; init; } = true;
    /// <summary>Duracao de cada pagina dos standings por evento (2x PageSeconds: da tempo dos demais carros cruzarem a linha).</summary>
    public double StandingsPageSeconds => PageSeconds * 2;
    public bool Stretch { get; init; }
    /// <summary>Digital 2004/2010: nomes em caixa mista ("V. Costa") como nas transmissoes; Classic/Digital mantem "V. COSTA".</summary>
    public bool MixedCaseNames { get; init; }
    /// <summary>Previa (--png): forca uma tela pelo nome ("time", "fl", "stR"...). Fora do catalogo.</summary>
    public string? PreviewScreen { get; init; }
    /// <summary>Previa: ms desde o inicio da animacao (negativo = assentada).</summary>
    public double PreviewMs { get; init; } = -1;

    public double StepMs => Math.Clamp(FadingSpeed, 1, 100) * 2;

    public static GpxOptions From(WidgetSettings s, bool classicClock = false)
    {
        static double Num(WidgetSettings s, string id, double d)
            => double.TryParse(s.Option(id), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : d;
        static bool Flag(WidgetSettings s, string id, bool d) => bool.TryParse(s.Option(id), out var v) ? v : d;
        static string Pick(WidgetSettings s, string id, string d) => (s.Option(id) ?? d).Trim().ToLowerInvariant();
        // Duracoes pela Exibicao (timings sky/split/page; leem tambem as opcoes antigas skySeconds/splitSeconds/pageSeconds), mesmos padroes e limites.
        var show = ShowPlan.For(s, "f1-1998");
        return new GpxOptions
        {
            ShowMode = Pick(s, "showMode", "all"),
            ShowTimeLeft = Flag(s, "showTimeLeft", !classicClock),
            RaceStandings = Pick(s, "raceStandings", "off"),
            QualiStandings = Flag(s, "qualiStandings", true),
            SpeedSplitRace = Pick(s, "speedSplitRace", "ahead"),
            SpeedSplitQuali = Pick(s, "speedSplitQuali", "split"),
            DirectorsCut = Flag(s, "directorsCut", false),
            ShowLogo = Flag(s, "showLogo", false),
            FadingSpeed = Num(s, "fadingSpeed", 10),
            SkySeconds = show.Timing("sky", 8),
            SplitSeconds = show.Timing("split", 8),
            PageSeconds = show.Timing("page", 6),
            OriginalFonts = Flag(s, "originalFonts", true),
            Rotation = Flag(s, "rotation", true),
            Stretch = Pick(s, "aspect", "pillarbox") == "stretch",
            PreviewScreen = s.Option("previewScreen") ?? (s.Option("layoutPreview") == "true" ? "str" : null),   // editor de layout: standings
            PreviewMs = Num(s, "previewMs", -1),
        };
    }
}

/// <summary>
/// Estado que os modos leem a cada quadro: modelo, opcoes, geometria do modo e os rastreadores proprios do GPxSAT (sky messages,
/// janelas de split/gap/trap). Ajudantes de texto comuns (nome "A. SENNA" respeitando o formato do perfil, equipe, posicao).
/// </summary>
public sealed class GpxContext
{
    public required OverlayModel Model { get; init; }
    public required SessionSnapshot Session { get; init; }
    public required GpxOptions Options { get; init; }
    public required WidgetSettings Settings { get; init; }
    public required GpxGeometry G { get; init; }
    public required GpxSkyTracker Sky { get; init; }
    public required GpxSplitTracker Split { get; init; }

    public double Now => Model.Now;
    public BroadcastState Broadcast => Model.Broadcast ?? BroadcastState.Empty;
    /// <summary>Carro em foco (camera do GPx = carro do jogador / vista do AMS2).</summary>
    public CarSnapshot? Focus => Session.PlayerCar;
    public bool IsRace => Session.Kind == SessionKind.Race;
    public bool ShowAll => Options.ShowMode == "all";
    public bool ShowSky => Options.ShowMode is "all" or "sky";

    public string Name(CarSnapshot car) => Settings.Name(car, GpxFormat.DriverName(car.Name, Options.MixedCaseNames));
    public static string Team(CarSnapshot car) => BroadcastUi.Team(car);

    /// <summary>Posicao exibida: corrida = posicao do jogo; fora dela = posicao na tabela de melhores voltas.</summary>
    public int PositionOf(CarSnapshot car)
    {
        if (!IsRace && Model.Quali?.Row(car.Index) is { HasTime: true } r) return r.Rank;
        return car.Position;
    }

    /// <summary>Pagina corrente (troca a cada PageSeconds) de <paramref name="count"/> itens em paginas de <paramref name="perPage"/>.</summary>
    public int PageOf(int count, int perPage)
    {
        int pages = Math.Max(1, (count + perPage - 1) / perPage);
        return pages == 1 ? 0 : (int)(Math.Floor(Math.Max(0, Now) / Options.PageSeconds) % pages);
    }

    /// <summary>Janela de gap com dado para a rotacao: aberta, ou ja resolvida (gap/voltas); o cronometro do gap atras sem resultado nao entra.</summary>
    public bool GapReady(GpxGapWindow? w) => w is not null && (w.Gap is not null || w.Laps > 0 || w.ActiveAt(Now));

    public bool Preview(string screen) => string.Equals(Options.PreviewScreen, screen, StringComparison.OrdinalIgnoreCase);
    public bool Previewing => !string.IsNullOrWhiteSpace(Options.PreviewScreen);
}
