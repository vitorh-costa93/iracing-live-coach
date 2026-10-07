namespace Ams2.Core.Calc;

/// <summary>
/// Gap em tempo por tabela de passagem. O AMS2 não fornece EstTime: guardamos, por carro, o instante em que
/// cruzou cada faixa da pista (a cada <c>binSize</c> metros) e comparamos com o instante em que o outro carro
/// passa pela mesma faixa.
/// </summary>
public sealed class GapTracker
{
    public const int MaxCars = 64;
    const double MaxGapSeconds = 600;
    const double NoiseSeconds = 0.5;
    const double MaxStepMeters = 500; // saltos maiores (teleporte/pit/reset) não preenchem faixas

    readonly double _binSize;
    double _trackLength;
    int _binCount;
    readonly double[][] _cross = new double[MaxCars][];
    readonly double[] _lastDist = new double[MaxCars];
    readonly double[] _lastTime = new double[MaxCars];
    readonly bool[] _seen = new bool[MaxCars];

    public GapTracker(double binSizeMeters = 10) => _binSize = binSizeMeters;

    public void Reset()
    {
        Array.Clear(_cross);
        Array.Clear(_seen);
        _binCount = 0;
    }

    /// <summary>Atualiza a tabela. <paramref name="now"/> em segundos (relógio monotônico; qualquer origem).</summary>
    public void Update(double now, double trackLength, IReadOnlyList<CarSnapshot> cars)
    {
        if (trackLength <= 0) return;
        if (Math.Abs(trackLength - _trackLength) > 0.5) { Reset(); _trackLength = trackLength; _binCount = (int)Math.Ceiling(trackLength / _binSize); }

        foreach (var c in cars)
        {
            if (c.Index is < 0 or >= MaxCars) continue;
            double d = Wrap(c.LapDistance);
            if (_seen[c.Index] && !c.InGarage)
            {
                double step = d - _lastDist[c.Index];
                if (step < -_trackLength / 2) step += _trackLength; // cruzou a linha de largada
                if (step > 0 && step <= MaxStepMeters) Record(c.Index, _lastDist[c.Index], step, _lastTime[c.Index], now);
            }
            _lastDist[c.Index] = d;
            _lastTime[c.Index] = now;
            _seen[c.Index] = true;
        }
    }

    void Record(int idx, double from, double step, double t0, double t1)
    {
        var table = _cross[idx] ??= NewTable();
        double to = from + step;
        for (long k = (long)Math.Floor(from / _binSize) + 1; k * _binSize <= to; k++)
            table[k % _binCount] = t0 + (k * _binSize - from) / step * (t1 - t0);
    }

    double[] NewTable() { var t = new double[_binCount]; Array.Fill(t, double.NaN); return t; }
    double Wrap(double d) => d < 0 ? d + _trackLength : d >= _trackLength ? d - _trackLength : d;

    /// <summary>
    /// Tempo em segundos entre o carro <paramref name="other"/> e o jogador no instante <paramref name="now"/>.
    /// Positivo = <paramref name="other"/> está à frente; negativo = atrás. null = sem dado confiável.
    /// </summary>
    public double? GapSeconds(double now, CarSnapshot player, CarSnapshot other, double signedDistanceAhead)
    {
        if (_binCount == 0) return null;
        bool ahead = signedDistanceAhead >= 0;
        var leader = ahead ? other : player;
        var follower = ahead ? player : other;
        double? gap = FollowerDelay(now, leader, follower);
        return gap is null ? null : ahead ? gap : -gap;
    }

    double? FollowerDelay(double now, CarSnapshot leader, CarSnapshot follower)
    {
        if (leader.Index is < 0 or >= MaxCars || _cross[leader.Index] is not { } table) return null;
        double fd = Wrap(follower.LapDistance);
        long bin = (long)Math.Floor(fd / _binSize);
        double leaderTime = table[bin % _binCount];
        if (double.IsNaN(leaderTime)) return null;
        double offset = fd - bin * _binSize;
        double speed = Math.Max(follower.SpeedMps, 5);
        double gap = now - offset / speed - leaderTime;
        if (gap is < 0 and >= -NoiseSeconds) gap = 0; // lado a lado: ruído da interpolação
        return gap is >= 0 and <= MaxGapSeconds ? gap : null;
    }
}
