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
    FuelEstimate? Fuel)
{
    public static readonly OverlayModel Empty = new(false, ReadStatus.Disconnected, 0, 0, null, [], null);
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
    readonly int _ahead, _behind;
    volatile OverlayModel _current = OverlayModel.Empty;
    CancellationTokenSource? _cts;
    Thread? _thread;
    long _frame;
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
            if (!_wasConnected) { _gaps.Reset(); _fuel.Reset(); }
            _wasConnected = true;
            if (s.InSession) _gaps.Update(now, s.TrackLength, s.Cars);
            var rel = s.InSession ? RelativeBuilder.Build(s, _gaps, now, _ahead, _behind) : [];
            var fuel = s.InSession ? _fuel.Update(s) : null;
            model = new OverlayModel(true, r.Status, now, ++_frame, s, rel, fuel);
        }
        else
        {
            _wasConnected = false;
            // Torn: mantém o quadro anterior (leitura rasgada é transitória); o resto = desconectado.
            model = r.Status == ReadStatus.Torn ? _current : new OverlayModel(false, r.Status, now, ++_frame, null, [], null);
        }
        _current = model;
        return model;
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _thread?.Join(500);
        _reader.Dispose();
    }
}
