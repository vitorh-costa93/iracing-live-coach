using Ams2.Core;
using Ams2.Core.Calc;
using Ams2.Core.Reading;
using Ams2.Shared.Liveries;
using Ams2.Shared.PlayerNames;

namespace Ams2.OverlayHost.Data;

/// <summary>Quadro de dados que os widgets leem: só modelo neutro, nunca a estrutura crua.</summary>
public sealed record OverlayModel(
    bool Connected,
    ReadStatus Status,
    double Now,
    long Frame,
    SessionSnapshot? Session,
    IReadOnlyList<RelativeRow> Relative,
    FuelEstimate? Fuel,
    IReadOnlyList<StandingRow> Standings,
    InputRing? Inputs,
    BroadcastState? Broadcast = null,
    BoardState? Board = null,
    RadarFrame? Radar = null,
    bool PlayerDriving = false,   // regra unica de visibilidade dos widgets (PlayerDrivingTracker): so true quando o jogador esta no carro
    IReadOnlyDictionary<int, int>? Grid = null,   // grid de largada (GridTracker): indice do carro -> posicao de largada; vazio = desconhecido
    LaunchState? Launch = null,                   // largada do jogador 0-100/0-200 km/h (LaunchTracker) + melhor guardado da pista+carro
    QualiTableState? Quali = null,                // tabela de melhores voltas (QualiTable + OutLapTracker) e relogio da sessao
    QualiLapState? QualiLap = null,               // volta em andamento do jogador: setores, parciais e resultado (QualiLapTracker)
    QualiEndState? QualiEnd = null,               // fim da classificacao (QualiEndTracker): instante do fim e mudancas da tabela depois dele
    LapDeltaState? LapDelta = null,               // delta ao vivo da volta atual vs melhor volta observada (LapDeltaTracker); widget Lap Delta
    ShowEventTimes? Events = null)                // instantes dos eventos da Exibicao (ShowEventTracker): gatilho "por evento" dos widgets
{
    /// <summary>Grupo da sessao para o filtro de visibilidade ("practice"/"qualify"/"race"); null = sem dados ou tipo invalido (nao filtra).</summary>
    public string? SessionGroup => Connected ? SessionGroups.IdOf(Session?.Kind) : null;

    public static readonly OverlayModel Empty = new(false, ReadStatus.Disconnected, 0, 0, null, [], null, [], null);
}

/// <summary>Linha da classificação: gap para o líder em segundos (null = líder ou sem dado) e voltas de atraso.</summary>
public sealed record StandingRow(CarSnapshot Car, double? GapToLeader, int LapsBehind, bool IsPlayer);

public static class StandingsBuilder
{
    public static IReadOnlyList<StandingRow> Build(SessionSnapshot s, GapTracker tracker, double now)
    {
        if (s.TrackLength <= 0) return [];
        var cars = s.Cars.Where(c => !c.InGarage && c.Position > 0).OrderBy(c => c.Position).ToList();
        if (cars.Count == 0) return [];
        var leader = cars[0];
        double len = s.TrackLength;
        var rows = new List<StandingRow>(cars.Count);
        foreach (var c in cars)
        {
            double? gap = null; int laps = 0;
            if (c.Index != leader.Index)
            {
                double delta = leader.LapDistance - c.LapDistance;
                if (delta > len / 2) delta -= len; else if (delta < -len / 2) delta += len;
                laps = (int)Math.Round((leader.TotalDistance(len) - c.TotalDistance(len) - delta) / len);
                if (laps <= 0) gap = tracker.GapSeconds(now, c, leader, 1);
            }
            rows.Add(new StandingRow(c, gap, Math.Max(0, laps), c.IsPlayer));
        }
        return rows;
    }
}

/// <summary>
/// Provider único e compartilhado por todos os widgets: um leitor, um GapTracker, um FuelTracker.
/// <see cref="Start"/> sobe um loop de ~60 Hz; <see cref="Tick"/> executa um passo (usado pelo --png com relógio simulado).
/// Os widgets leem <see cref="Current"/>, um record imutável publicado de forma atômica.
/// </summary>
public sealed class OverlayDataProvider : IDisposable
{
    readonly SharedMemoryReader _reader;
    readonly Func<double> _clock;
    readonly GapTracker _gaps = new();
    readonly FuelTracker _fuel = new();
    readonly BroadcastTracker _broadcast = new();
    readonly GridTracker _grid = new();
    readonly LaunchTracker _launch;
    readonly OutLapTracker _outLaps = new();
    readonly QualiLapTracker _qualiLap = new();
    readonly QualiEndTracker _qualiEnd = new();
    readonly LapDeltaTracker _lapDelta = new();
    readonly ShowEventTracker _events = new();
    readonly BoardTracker _board;
    /// <summary>Radar lateral (pose de mundo dos carros). As opcoes (alcance, sensibilidade) vem do perfil, pelo host.</summary>
    public RadarTracker Radar { get; } = new();
    readonly PlayerDrivingTracker _driving = new();
    readonly int _ahead, _behind;
    volatile OverlayModel _current = OverlayModel.Empty;
    CancellationTokenSource? _cts;
    Thread? _thread;
    long _frame;
    /// <summary>Histórico das entradas (10 s). Gravado pelo <see cref="InputSampler"/> dedicado (taxa do jogo) quando há
    /// <c>inputSource</c>; sem ele, o <see cref="Tick"/> grava uma amostra por passo (--png com relógio simulado, testes).</summary>
    public InputRing Inputs { get; }
    readonly InputSampler? _sampler;
    public const double InputWindowSeconds = 10;
    bool _wasConnected;
    uint _lastRadarSequence = uint.MaxValue;
    bool _wasRadarConnected;
    readonly PlayerNameStore? _names;
    readonly LiveryDetector? _liveries;
    string _playerDiag = "";
    string _liveryModel = "";
    int _liveryField = -1;
    readonly DriverCountryResolver? _countries;
    double _namesRefreshAt;

    public OverlayDataProvider(IRawMemorySource source, Func<double> clock, int ahead = 4, int behind = 4, BoardOptions? board = null,
        Func<IRawMemorySource>? inputSource = null, PlayerNameStore? names = null, LaunchStore? launch = null, LiveryDetector? liveries = null)
    {
        _names = names;
        _liveries = liveries;
        if (liveries is not null) _countries = new DriverCountryResolver(liveries.Catalog);
        _launch = new LaunchTracker(launch);
        Inputs = new InputRing(clock);
        if (inputSource is not null) _sampler = new InputSampler(inputSource(), Inputs, clock, InputStats);
        _reader = new SharedMemoryReader(source);
        _board = new BoardTracker(board);
        _clock = clock;
        _ahead = ahead; _behind = behind;
    }

    public OverlayModel Current => _current;
    /// <summary>Configuração dinâmica exclusiva do GAP 1993 (ponto em percentual e retenção em segundos).</summary>
    public void SetGap93Options(double pointPercent, double holdSeconds, bool enabled = true, int lapInterval = Ams2.Core.Calc.Broadcast93Rules.GapEveryLaps) =>
        _board.SetGap93Options(pointPercent, holdSeconds, enabled, lapInterval);
    /// <summary>Exibicao do Race Board 1999 (duracoes e pecas do perfil).</summary>
    public void SetRace99Options(Ams2.Core.Calc.Race99Options options) => _board.SetRace99Options(options);
    /// <summary>Exibicao do board 2004/2012/2018 (pecas ligadas e segundos por pagina da torre).</summary>
    public void SetBoardPieces(Ams2.Core.Calc.BoardPieces pieces) => _board.SetBoardPieces(pieces);
    /// <summary>Nomes de exibicao do jogador por modelo de carro (null = sem substituicao).</summary>
    public PlayerNameStore? Names => _names;
    /// <summary>Medição: passos do provider e amostras de entrada gravadas (usadas pelo --measure).</summary>
    public RateStats TickStats { get; } = new();
    public RateStats InputStats { get; } = new();
    public bool HasInputSampler => _sampler is not null;

    public void Start(double hz = 60)
    {
        if (_thread != null) return;
        _sampler?.Start();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _thread = new Thread(() =>
        {
            var period = TimeSpan.FromSeconds(1 / hz);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var next = period;
            while (!token.IsCancellationRequested)
            {
                try { Tick(); } catch (Exception ex) { Console.Error.WriteLine($"[Provider] {ex.GetType().Name}: {ex.Message}"); }
                var wait = next - sw.Elapsed;
                if (wait > TimeSpan.Zero) Thread.Sleep(wait);
                next += period;
                if (sw.Elapsed - next > period * 5) next = sw.Elapsed + period; // atrasou muito: não acumular
            }
        }) { IsBackground = true, Name = "Ams2 data provider" };
        _thread.Start();
    }

    /// <summary>Um passo: lê a memória, atualiza os trackers e publica um novo <see cref="OverlayModel"/>.</summary>
    public OverlayModel Tick()
    {
        double now = _clock();
        TickStats.Mark();
        var r = _reader.Poll();
        OverlayModel model;
        if (r.Status == ReadStatus.Ok && r.Snapshot is { } s)
        {
            if (!_wasConnected) { _gaps.Reset(); _fuel.Reset(); _broadcast.Reset(); _board.Reset(); Radar.Reset(); _driving.Reset(); _grid.Reset(); _launch.Reset(); _outLaps.Reset(); _qualiLap.Reset(); _qualiEnd.Reset(); _lapDelta.Reset(); _events.Reset(); }
            _wasConnected = true;
            s = ApplyPlayerName(now, s);
            if (_countries is not null) { s = PlayerIdentity.ApplyOpponentCountries(s, _countries.Resolve); s = PlayerIdentity.ApplyOpponentNumbers(s, _countries.ResolveNumber); }
            if (_sampler is null && s.Player is { } pl) SampleFromSnapshot(now, pl.Inputs);
            if (s.InSession) _gaps.Update(now, s.TrackLength, s.Cars);
            var rel = s.InSession ? RelativeBuilder.Build(s, _gaps, now, _ahead, _behind) : [];
            var fuel = s.InSession ? _fuel.Update(s) : null;
            var standings = s.InSession ? StandingsBuilder.Build(s, _gaps, now) : [];
            var bc = s.InSession ? _broadcast.Update(now, s) : BroadcastState.Empty;
            // Board: depois do GapTracker (usa o gap em tempo). Fora de sessão: estado vazio (o tracker se zera sozinho).
            bool driving = _driving.Update(now, s);
            var board = _board.Update(now, s, _gaps, driving);
            // Repetir a mesma escrita do jogo nao e uma nova pose. Preserva o instante da pose
            // para o render continuar extrapolando, em vez de reiniciar o movimento a cada tick.
            var radar = !_wasRadarConnected || s.Sequence != _lastRadarSequence || !s.InSession
                ? Radar.Update(s, now) : Radar.Current;
            _lastRadarSequence = s.Sequence;
            _wasRadarConnected = true;
            var grid = s.InSession ? _grid.Update(s) : _grid.Grid;
            var launch = s.InSession ? _launch.Update(now, s) : _launch.State;
            // Classificacao: calculada em toda sessao (barata); os widgets de classificacao decidem quando aparecer.
            if (s.InSession) _outLaps.Update(s);
            var quali = s.InSession ? QualiTable.Build(s, _outLaps.IsOutLap) : null;
            var trackedQualiLap = _qualiLap.Update(now, s);
            var qualiLap = s.InSession ? trackedQualiLap : null;
            var qualiEnd = quali is not null ? _qualiEnd.Update(now, s, quali) : null;
            var lapDelta = s.InSession ? _lapDelta.Update(s, trackedQualiLap) : null;
            var events = s.InSession ? _events.Update(now, s, bc, qualiLap, qualiEnd) : ShowEventTimes.Empty;
            model = new OverlayModel(true, r.Status, now, ++_frame, s, rel, fuel, standings, Inputs, bc, board, radar, driving, grid, launch, quali, qualiLap, qualiEnd, lapDelta, events);
        }
        else
        {
            // Torn: leitura rasgada é transitória: mantém o quadro anterior e NÃO conta como desconexão
            // (senão os trackers de gap/combustível seriam zerados a cada leitura rasgada). O resto = desconectado.
            if (r.Status != ReadStatus.Torn) { _wasConnected = false; _wasRadarConnected = false; }
            model = r.Status == ReadStatus.Torn ? _current : new OverlayModel(false, r.Status, now, ++_frame, null, [], null, [], Inputs);
        }
        _current = model;
        return model;
    }

    /// <summary>Escolha explicita do usuario (Control Center) vence a deteccao automatica; a deteccao so preenche quando nao ha escolha.</summary>
    public static PlayerNameEntry? ChooseIdentity(PlayerNameEntry? manual, PlayerNameEntry? detected)
        => manual is { HasOverride: true } ? manual : detected ?? manual;

    /// <summary>Registra o carro do jogador no store (modelo, nome do jogo) e aplica a identidade da pintura escolhida (piloto, equipe, pais).
    /// So o Name/TeamName/Country mudam: indices e distancias intactos, o GapTracker nao e afetado.</summary>
    SessionSnapshot ApplyPlayerName(double now, SessionSnapshot s)
    {
        if (_names is null) return s;
        if (now >= _namesRefreshAt || now < _namesRefreshAt - 5) { _namesRefreshAt = now + 1; _names.Refresh(); }
        var pc = s.PlayerCar;
        _names.Observe(pc?.CarName ?? "", pc?.OriginalName.Length > 0 ? pc.OriginalName : pc?.Name ?? "");
        if (_liveries is not null)
        {
            // Pintura lida do jogo (varredura em thread propria, so ao entrar numa sessao ou trocar de carro); a escolha manual e o plano B.
            string model = pc is null ? "" : PlayerNameStore.ModelKey(pc.CarName);
            string liveryKey = (pc?.CarName.Trim() ?? "") + "|" + (pc?.ClassName ?? ""); // classe/fornecedor podem mudar sem mudar ModelKey
            if (liveryKey != _liveryModel) { _liveryModel = liveryKey; if (model.Length == 0) _liveries.Forget(); else _liveries.Request(pc!.CarName, pc.ClassName); }
            int field = s.Cars.Count;
            if (field != _liveryField && model.Length > 0) { _liveryField = field; _liveries.SetOpponents(s.Cars.Where(c => c.Index != pc!.Index).Select(c => c.Name)); } // IA da sessao: tira as pinturas dela dos candidatos
            var withPlayer = PlayerIdentity.Apply(s, m => ChooseIdentity(_names.Get(m), _liveries.Get(m)));
            // Equipe da IA depende do carro/classe da pintura, nunca do tema ou do nome global do piloto.
            var result = PlayerIdentity.ApplyFieldByCar(withPlayer, c => _liveries.FindDriver(c.Name, c.CarName, c.ClassName) is { } e ? (e.Country, e.Team) : null);
            string diag = $"{result.PlayerCar?.Name}|{result.PlayerCar?.Country}|{_liveries.Get(pc?.CarName ?? "")?.Country}";
            if (diag != _playerDiag) { _playerDiag = diag; Console.Error.WriteLine($"[Player] nome|pais|detectado = {diag}"); }
            return result;
        }
        return PlayerIdentity.Apply(s, _names.Get);
    }

    /// <summary>Caminho sem amostrador (relógio simulado): uma amostra por passo do provider.</summary>
    void SampleFromSnapshot(double now, InputsSnapshot i)
    {
        if (now < Inputs.LastT) Inputs.Clear();
        Inputs.Add(new InputSample(now, (float)i.Throttle, (float)i.Brake, (float)i.Steering));
        InputStats.Mark();
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _thread?.Join(500);
        _sampler?.Dispose();
        _reader.Dispose();
    }
}
