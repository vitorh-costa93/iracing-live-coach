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
    IReadOnlyList<InputSample> InputHistory,
    BroadcastState? Broadcast = null)
{
    public static readonly OverlayModel Empty = new(false, ReadStatus.Disconnected, 0, 0, null, [], null, [], []);
}

/// <summary>Linha da classificação: gap para o líder em segundos (null = líder ou sem dado) e voltas de atraso.</summary>
public sealed record StandingRow(CarSnapshot Car, double? GapToLeader, int LapsBehind, bool IsPlayer);

/// <summary>Amostra dos pedais para o gráfico (T = relógio do provider, em segundos).</summary>
public readonly record struct InputSample(double T, float Throttle, float Brake, float Steering);

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
    readonly int _ahead, _behind;
    volatile OverlayModel _current = OverlayModel.Empty;
    CancellationTokenSource? _cts;
    Thread? _thread;
    long _frame;
    readonly List<InputSample> _inputs = [];
    InputSample[] _inputsPublished = [];
    double _lastInputT = double.NegativeInfinity;
    public const double InputWindowSeconds = 10, InputSampleSeconds = 1.0 / 30;
    bool _wasConnected;

    public OverlayDataProvider(IRawMemorySource source, Func<double> clock, int ahead = 4, int behind = 4)
    {
        _reader = new SharedMemoryReader(source);
        _clock = clock;
        _ahead = ahead; _behind = behind;
    }

    public OverlayModel Current => _current;

    public void Start(double hz = 60)
    {
        if (_thread != null) return;
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
        var r = _reader.Poll();
        OverlayModel model;
        if (r.Status == ReadStatus.Ok && r.Snapshot is { } s)
        {
            if (!_wasConnected) { _gaps.Reset(); _fuel.Reset(); _broadcast.Reset(); }
            _wasConnected = true;
            if (s.InSession) _gaps.Update(now, s.TrackLength, s.Cars);
            var rel = s.InSession ? RelativeBuilder.Build(s, _gaps, now, _ahead, _behind) : [];
            var fuel = s.InSession ? _fuel.Update(s) : null;
            var standings = s.InSession ? StandingsBuilder.Build(s, _gaps, now) : [];
            var bc = s.InSession ? _broadcast.Update(now, s) : BroadcastState.Empty;
            model = new OverlayModel(true, r.Status, now, ++_frame, s, rel, fuel, standings, SampleInputs(now, s), bc);
        }
        else
        {
            // Torn: leitura rasgada é transitória: mantém o quadro anterior e NÃO conta como desconexão
            // (senão os trackers de gap/combustível seriam zerados a cada leitura rasgada). O resto = desconectado.
            if (r.Status != ReadStatus.Torn) _wasConnected = false;
            model = r.Status == ReadStatus.Torn ? _current : new OverlayModel(false, r.Status, now, ++_frame, null, [], null, [], []);
        }
        _current = model;
        return model;
    }

    InputSample[] SampleInputs(double now, SessionSnapshot s)
    {
        if (s.Player is null) return _inputsPublished;
        if (_inputs.Count > 0 && now < _inputs[^1].T) { _inputs.Clear(); _lastInputT = double.NegativeInfinity; } // relógio voltou
        if (now - _lastInputT < InputSampleSeconds) return _inputsPublished;
        _lastInputT = now;
        var i = s.Player.Inputs;
        _inputs.Add(new InputSample(now, (float)i.Throttle, (float)i.Brake, (float)i.Steering));
        int drop = _inputs.FindIndex(x => x.T >= now - InputWindowSeconds);
        if (drop > 0) _inputs.RemoveRange(0, drop);
        return _inputsPublished = _inputs.ToArray();
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _thread?.Join(500);
        _reader.Dispose();
    }
}
