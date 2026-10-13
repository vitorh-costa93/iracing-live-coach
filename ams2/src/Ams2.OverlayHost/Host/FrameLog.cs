using System.Diagnostics;
using System.Globalization;

namespace Ams2.OverlayHost.Host;

/// <summary>
/// Telemetria barata de apresentacao de um widget HF: intervalo entre Presents e duracao do Present, em buffers fixos
/// (sem alocacao por quadro). A cada 5 s escreve UMA linha [FPS] no stdout (host-out.log) se houve quadros.
/// </summary>
internal sealed class FrameLog(string widget)
{
    const int Capacity = 4096;
    static readonly long WindowTicks = 5 * Stopwatch.Frequency;
    static readonly double ToMs = 1000.0 / Stopwatch.Frequency;

    readonly double[] _interval = new double[Capacity];
    readonly double[] _present = new double[Capacity];
    readonly double[] _wait = new double[Capacity];
    int _n;
    long _prevEnd, _windowStart;
    int _over25;

    /// <summary>Registra um Present concluido (timestamps de Stopwatch).</summary>
    public void Add(long presentStart, long presentEnd, long waitTicks = 0)
    {
        if (_windowStart == 0) _windowStart = presentEnd;
        double presentMs = (presentEnd - presentStart) * ToMs;
        double waitMs = waitTicks * ToMs;
        if (_prevEnd != 0)
        {
            double ms = (presentEnd - _prevEnd) * ToMs;
            if (ms < 2000) // ignora pausas (janela oculta/jogo fora do carro)
            {
                if (_n < Capacity) { _interval[_n] = ms; _present[_n] = presentMs; _wait[_n] = waitMs; _n++; }
                if (ms > 25) _over25++;
            }
        }
        _prevEnd = presentEnd;
        if (presentEnd - _windowStart >= WindowTicks) Flush(presentEnd);
    }

    void Flush(long now)
    {
        double secs = (now - _windowStart) * (1.0 / Stopwatch.Frequency);
        int n = _n;
        if (n > 0)
        {
            Array.Sort(_interval, 0, n);
            Array.Sort(_present, 0, n);
            Array.Sort(_wait, 0, n);
            var ci = CultureInfo.InvariantCulture;
            string line = string.Create(ci, $"[FPS] widget={widget} fps={n / secs:F1} interval p50/p95/p99/max={Pct(_interval, n, .5):F1}/{Pct(_interval, n, .95):F1}/{Pct(_interval, n, .99):F1}/{_interval[n - 1]:F1} ms present p50/p95/p99/max={Pct(_present, n, .5):F1}/{Pct(_present, n, .95):F1}/{Pct(_present, n, .99):F1}/{_present[n - 1]:F1} ms wait p50/p95/p99/max={Pct(_wait, n, .5):F1}/{Pct(_wait, n, .95):F1}/{Pct(_wait, n, .99):F1}/{_wait[n - 1]:F1} ms frames>25ms={_over25}");
            try { Console.WriteLine(line); } catch { }
        }
        _n = 0; _over25 = 0; _windowStart = now;
    }

    static double Pct(double[] a, int n, double p) => a[Math.Min(n - 1, (int)(n * p))];
}
