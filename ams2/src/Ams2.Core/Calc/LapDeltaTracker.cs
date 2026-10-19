namespace Ams2.Core.Calc;

/// <summary>Delta ao vivo do jogador: <see cref="Delta"/> = tempo da volta atual menos o tempo da melhor volta observada na mesma
/// distancia (negativo = mais rapido). <see cref="BestLap"/> = tempo da volta de referencia; null/null = ainda sem referencia.</summary>
public sealed record LapDeltaState(double? Delta, double? BestLap)
{
    public static readonly LapDeltaState Empty = new(null, null);
}

/// <summary>
/// Delta da volta (estilo iRacing "Lap Delta"). O AMS2 nao expoe o tempo corrente da volta nem um delta pronto, entao a referencia e
/// construida aqui: durante cada volta limpa o tempo decorrido (<see cref="QualiLapState.Elapsed"/>, do QualiLapTracker) e gravado a cada
/// <see cref="BinMeters"/> m de LapDistance (interpolando entre duas amostras); ao fechar uma volta valida (sem pit, sem invalidacao, nao
/// e volta de saida) mais rapida que a referencia, ela vira a nova referencia. A referencia e a melhor volta OBSERVADA pelo overlay
/// (uma melhor volta feita antes de abri-lo nao tem tracado, entao a primeira volta limpa vira a referencia).
/// </summary>
public sealed class LapDeltaTracker
{
    public const double BinMeters = 5;
    const double MaxStepMeters = 400, MinCoverage = 0.9;

    double _len;
    int _generation = -1, _lap = -1;
    double[] _cur = [], _best = [];
    double? _bestLap;
    double _prevD = double.NaN, _prevT = double.NaN;
    bool _curBad;
    int _lastPromotedLap = int.MinValue;
    LapDeltaState _state = LapDeltaState.Empty;

    public LapDeltaState State => _state;

    public void Reset()
    {
        _len = 0; _generation = -1; _lap = -1; _cur = []; _best = []; _bestLap = null;
        _prevD = _prevT = double.NaN; _curBad = false; _lastPromotedLap = int.MinValue; _state = LapDeltaState.Empty;
    }

    public LapDeltaState Update(SessionSnapshot s, QualiLapState q)
    {
        var pc = s.PlayerCar;
        if (!s.InSession) return _state;
        if (pc is null || q.CarIndex < 0 || s.TrackLength <= 0) return _state = LapDeltaState.Empty with { BestLap = _bestLap };
        if (q.SessionGeneration != _generation || Math.Abs(s.TrackLength - _len) > 1)
        {
            Reset(); _generation = q.SessionGeneration; _len = s.TrackLength;
            _cur = NewTrace(); _best = [];
        }
        // Volta fechada: se foi limpa e mais rapida que a referencia, o tracado dela passa a ser a referencia.
        if (q.LastResult is { } r && r.Lap != _lastPromotedLap)
        {
            _lastPromotedLap = r.Lap;
            if (!r.Invalid && !r.WasOutLap && !_curBad && (_bestLap is not { } b || r.LapTime < b) && Finish(r.LapTime) is { } trace)
            { _best = trace; _bestLap = r.LapTime; }
        }
        if (pc.LapsCompleted != _lap)
        {
            _lap = pc.LapsCompleted; _cur = NewTrace(); _prevD = _prevT = double.NaN; _curBad = false;
        }
        bool clean = q.Elapsed is { } e && double.IsFinite(e) && e >= 0 && !q.InPit && !q.OutLap;
        if (!clean || pc.LapInvalid) { _curBad = true; _prevD = _prevT = double.NaN; return _state = LapDeltaState.Empty with { BestLap = _bestLap }; }

        double d = Math.Clamp(pc.LapDistance, 0, _len), t = q.Elapsed!.Value;
        Record(d, t);
        _prevD = d; _prevT = t;
        double? delta = _best.Length > 0 && Reference(d) is { } rt ? t - rt : null;
        return _state = new LapDeltaState(delta, _bestLap);
    }

    double[] NewTrace()
    {
        var a = new double[(int)Math.Ceiling(_len / BinMeters) + 1];
        Array.Fill(a, double.NaN);
        a[0] = 0;
        return a;
    }

    /// <summary>Grava o tempo nas fronteiras de BinMeters atravessadas entre a amostra anterior e a atual.</summary>
    void Record(double d, double t)
    {
        if (double.IsNaN(_prevD) || d <= _prevD || d - _prevD > MaxStepMeters || t < _prevT) return;
        int from = (int)Math.Floor(_prevD / BinMeters) + 1, to = (int)Math.Floor(d / BinMeters);
        for (int k = from; k <= to && k < _cur.Length; k++)
        {
            double at = _prevT + (k * BinMeters - _prevD) / (d - _prevD) * (t - _prevT);
            if (double.IsNaN(_cur[k])) _cur[k] = at;
        }
    }

    /// <summary>Fecha o tracado da volta com o tempo oficial na linha; null se faltou cobertura (volta iniciada no meio, saltos).</summary>
    double[]? Finish(double lapTime)
    {
        var a = (double[])_cur.Clone();
        a[^1] = lapTime;
        int known = a.Count(v => !double.IsNaN(v));
        if (known < a.Length * MinCoverage) return null;
        // Preenche buracos curtos por interpolacao linear entre fronteiras conhecidas.
        for (int k = 1; k < a.Length - 1; k++)
        {
            if (!double.IsNaN(a[k])) continue;
            int lo = k - 1, hi = k + 1;
            while (hi < a.Length - 1 && double.IsNaN(a[hi])) hi++;
            for (int j = k; j < hi; j++) a[j] = a[lo] + (a[hi] - a[lo]) * (j - lo) / (hi - lo);
        }
        return a;
    }

    double? Reference(double d)
    {
        int k = Math.Min((int)Math.Floor(d / BinMeters), _best.Length - 2);
        if (k < 0) return null;
        double span = k == _best.Length - 2 ? _len - k * BinMeters : BinMeters;
        double f = span <= 0 ? 0 : Math.Clamp((d - k * BinMeters) / span, 0, 1);
        return _best[k] + (_best[k + 1] - _best[k]) * f;
    }
}
