using Ams2.Core.Calc;

namespace Ams2.Core.Reading;

/// <summary>
/// Amostrador dedicado das entradas e instrumentos. Roda em thread própria, lê pedais/volante/velocidade/RPM/marcha + contador de
/// sequência do mapa (leitura mínima, sem montar o snapshot de 64 carros) e grava uma amostra por escrita do jogo, no instante
/// em que a detecta. Limita a taxa a <c>maxHz</c> para não encher o histórico com fonte que muda a cada leitura (escritor falso).
/// </summary>
public sealed class InputSampler : IDisposable
{
    readonly IRawMemorySource _source;
    readonly InputRing _ring;
    readonly Func<double> _clock;
    readonly RateStats? _stats;
    readonly double _minInterval;
    uint _lastSeq = uint.MaxValue;
    double _lastT = double.NegativeInfinity;
    Thread? _thread;
    volatile bool _stop;

    public InputSampler(IRawMemorySource source, InputRing ring, Func<double> clock, RateStats? stats = null, double maxHz = 240)
    {
        _source = source; _ring = ring; _clock = clock; _stats = stats;
        _minInterval = 1.0 / maxHz;
    }

    /// <summary>Um passo de amostragem (público para testes). true = gravou uma amostra nova.</summary>
    public bool PollOnce()
    {
        double t = _clock();
        if (t < _lastT) { _ring.Clear(); _lastT = double.NegativeInfinity; _lastSeq = uint.MaxValue; } // relógio voltou
        if (t - _lastT < _minInterval) return false;
        if (!_source.TryReadInputs(out var r)) { _lastSeq = uint.MaxValue; return false; }
        if (r.Seq == _lastSeq) return false;
        _lastSeq = r.Seq; _lastT = t;
        _ring.Add(new InputSample(t, r.Throttle, r.Brake, r.Steering, r.SpeedMps, r.Rpm, r.MaxRpm, r.Gear));
        _stats?.Mark();
        return true;
    }

    /// <summary>Sobe a thread (prioridade acima do normal; espera de ~1 ms entre leituras, sem ocupar a CPU).</summary>
    public void Start()
    {
        if (_thread != null) return;
        _thread = new Thread(() =>
        {
            while (!_stop)
            {
                try { PollOnce(); } catch (Exception ex) { Console.Error.WriteLine($"[InputSampler] {ex.GetType().Name}: {ex.Message}"); Thread.Sleep(50); }
                Thread.Sleep(1);
            }
        }) { IsBackground = true, Name = "Ams2 input sampler", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    public void Dispose()
    {
        _stop = true;
        _thread?.Join(500);
        _source.Dispose();
    }
}
