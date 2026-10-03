using Ams2.Core;
using Ams2.Core.Calc;
using Ams2.Core.Reading;

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
    BoardState? Board = null)
{
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
    readonly BoardTracker _board;
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

    public OverlayDataProvider(IRawMemorySource source, Func<double> clock, int ahead = 4, int behind = 4, BoardOptions? board = null,
        Func<IRawMemorySource>? inputSource = null)
    {
        Inputs = new InputRing(clock);
        if (inputSource is not null) _sampler = new InputSampler(inputSource(), Inputs, clock, InputStats);
        _reader = new SharedMemoryReader(source);
        _board = new BoardTracker(board);
        _clock = clock;
        _ahead = ahead; _behind = behind;
    }

    public OverlayModel Current => _current;
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
            if (!_wasConnected) { _gaps.Reset(); _fuel.Reset(); _broadcast.Reset(); _board.Reset(); }
            _wasConnected = true;
            if (_sampler is null && s.Player is { } pl) SampleFromSnapshot(now, pl.Inputs);
            if (s.InSession) _gaps.Update(now, s.TrackLength, s.Cars);
            var rel = s.InSession ? RelativeBuilder.Build(s, _gaps, now, _ahead, _behind) : [];
            var fuel = s.InSession ? _fuel.Update(s) : null;
            var standings = s.InSession ? StandingsBuilder.Build(s, _gaps, now) : [];
            var bc = s.InSession ? _broadcast.Update(now, s) : BroadcastState.Empty;
            // Board: depois do GapTracker (usa o gap em tempo). Fora de sessão: estado vazio (o tracker se zera sozinho).
            var board = _board.Update(now, s, _gaps);
            model = new OverlayModel(true, r.Status, now, ++_frame, s, rel, fuel, standings, Inputs, bc, board);
        }
        else
        {
            // Torn: leitura rasgada é transitória: mantém o quadro anterior e NÃO conta como desconexão
            // (senão os trackers de gap/combustível seriam zerados a cada leitura rasgada). O resto = desconectado.
            if (r.Status != ReadStatus.Torn) _wasConnected = false;
            model = r.Status == ReadStatus.Torn ? _current : new OverlayModel(false, r.Status, now, ++_frame, null, [], null, [], Inputs);
        }
        _current = model;
        return model;
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
