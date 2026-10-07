using System.Diagnostics;

namespace Ams2.Core.Calc;

/// <summary>Resultado de <see cref="RateStats.Report"/>: taxa média e distribuição do intervalo entre marcas (ms).</summary>
public readonly record struct RateReport(long Frames, double Seconds, double PerSecond, double P50Ms, double P95Ms, double MaxMs)
{
    public override string ToString() => Frames == 0 ? "sem amostras"
        : $"{PerSecond,7:F1}/s  (n={Frames} em {Seconds:F2}s; intervalo ms p50={P50Ms:F1} p95={P95Ms:F1} max={MaxMs:F1})";
}

/// <summary>
/// Medidor de taxa para fps de render e taxa de amostragem. Um escritor (<see cref="Mark"/>), buffer circular fixo
/// (sem alocação por marca); o relatório é calculado depois, fora do caminho quente.
/// </summary>
public sealed class RateStats(int capacity = 16384)
{
    readonly double[] _t = new double[capacity];
    long _n;

    public static double Now() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    public long Count => Volatile.Read(ref _n);

    public void Mark() => Mark(Now());

    public void Mark(double t)
    {
        long n = _n;
        _t[(int)(n % _t.Length)] = t;
        Volatile.Write(ref _n, n + 1);
    }

    /// <summary>Taxa e intervalos das marcas com tempo &gt;= <paramref name="fromT"/> (para descartar o aquecimento).</summary>
    public RateReport Report(double fromT = 0)
    {
        long n = Volatile.Read(ref _n);
        long first = Math.Max(0, n - _t.Length);
        var ts = new List<double>((int)(n - first));
        for (long i = first; i < n; i++) { double t = _t[(int)(i % _t.Length)]; if (t >= fromT) ts.Add(t); }
        if (ts.Count < 2) return new(ts.Count, 0, 0, 0, 0, 0);
        var gaps = new double[ts.Count - 1];
        for (int i = 1; i < ts.Count; i++) gaps[i - 1] = (ts[i] - ts[i - 1]) * 1000;
        Array.Sort(gaps);
        double span = ts[^1] - ts[0];
        return new(ts.Count, span, (ts.Count - 1) / span, gaps[gaps.Length / 2], gaps[(int)Math.Min(gaps.Length - 1, gaps.Length * 0.95)], gaps[^1]);
    }
}
