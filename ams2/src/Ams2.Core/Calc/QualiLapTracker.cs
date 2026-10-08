namespace Ams2.Core.Calc;

/// <summary>Cor do setor como na TV: roxo = melhor geral, verde = melhor pessoal, amarelo = mais lento; None = sem comparacao (volta invalida/pit).</summary>
public enum SectorMark { None, Slower, PersonalBest, OverallBest }

/// <summary>Setor concluido (<see cref="Index"/> 0..2). <see cref="FromMemory"/> = tempo do jogo (mCurrentSectorNTimes) em vez do derivado.</summary>
public sealed record QualiSector(int Index, double Time, SectorMark Mark, bool FromMemory);

/// <summary>Parcial ao fim do setor <see cref="Sector"/> (1 ou 2): tempo da volta ate ali e diferenca para o melhor pessoal e para o lider (mesmo ponto).</summary>
public sealed record QualiSplit(int Sector, double Elapsed, double? DeltaPersonal, double? DeltaLeader, double At);

/// <summary>
/// Volta concluida do jogador: tempo, posicao na tabela com essa volta, diferenca para o 1o (melhor dos OUTROS carros; negativo = nova pole),
/// diferenca para o melhor pessoal anterior, setores e instante (relogio do provider) em que cruzou a linha.
/// </summary>
public sealed record QualiLapResult(int Lap, double LapTime, int Position, double? GapToFirst, double? DeltaPersonal, bool Improved, bool Invalid,
    IReadOnlyList<QualiSector?> Sectors, double At, bool FromMemory)
{
    public CarSnapshot? ReferenceCar { get; init; }
}

/// <summary>Volta em andamento do jogador (placa de volta da classificacao).</summary>
public sealed record QualiLapState(
    int CarIndex,
    int Lap,                                   // CurrentLap do jogo
    int Sector,                                // 0..2
    bool InPit,
    bool OutLap,
    double? Elapsed,                           // tempo corrente da volta (null = inicio da volta nao observado)
    IReadOnlyList<QualiSector?> Sectors,       // setores ja concluidos nesta volta
    IReadOnlyList<double?> PersonalBestSectors,
    IReadOnlyList<double?> OverallBestSectors,
    QualiSplit? LastSplit,                     // ultima parcial (S1/S2) desta volta
    double? PersonalBestLap,
    double? LeaderBestLap,
    int LeaderIndex,                           // -1 = ninguem com tempo
    QualiLapResult? LastResult)
{
    /// <summary>Tracker session/reset generation, so presentation can reset even when car and track stay the same.</summary>
    public int SessionGeneration { get; init; }
    public int PitExitGeneration { get; init; }
    public static readonly QualiLapState Empty = new(-1, 0, 0, false, false, null, [null, null, null], [null, null, null], [null, null, null], null, null, null, -1, null);
}

/// <summary>
/// Cronometragem da classificacao. Acompanha TODOS os carros (o melhor setor geral precisa de todos) e expoe a volta do jogador.
/// Relogio: o do provider (<c>now</c>). Instantes de passagem interpolados entre duas amostras (linha: distancia desenrolada;
/// marcas S1/S2: aprendidas pela intersecao dos intervalos de todas as passagens, como no BoardTracker).
/// <para>
/// Setores (decisao): o $pcars2$ tem mCurrentSector{1,2,3}Times e mFastestSector{1,2,3}Times por carro, mas ainda nao foram conferidos
/// em sessao real. Por isso o tempo de cada setor e DERIVADO da troca de <see cref="CarSnapshot.Sector"/> e substituido pelo valor da
/// memoria quando ela traz um numero novo e plausivel (diferenca &lt; <see cref="MemoryTolerance"/> s do derivado) no mesmo quadro ou
/// ate <see cref="MemorySettleSeconds"/> s depois. O melhor setor de cada carro = menor entre os observados (voltas validas, sem pit) e o
/// mFastestSectorNTimes da memoria (que cobre o que aconteceu antes do overlay abrir). Tempo da volta: derivado da linha e trocado pelo
/// LastLapTime do jogo quando ele muda para um valor plausivel na mesma janela.
/// </para>
/// </summary>
public sealed class QualiLapTracker
{
    const int MaxCars = 64;
    public const double MemoryTolerance = 1.0, MemorySettleSeconds = 1.5;
    const double MaxStepMeters = 400;

    readonly bool[] _seen = new bool[MaxCars];
    readonly bool[] _previousPit = new bool[MaxCars];
    readonly int[] _laps = new int[MaxCars], _sector = new int[MaxCars];
    readonly double[] _dist = new double[MaxCars], _t = new double[MaxCars];
    readonly double[] _lapStart = new double[MaxCars], _secStart = new double[MaxCars];
    readonly bool[] _lapBad = new bool[MaxCars], _secBad = new bool[MaxCars], _lapPit = new bool[MaxCars];
    readonly double[,] _secTime = new double[MaxCars, 3], _prevMem = new double[MaxCars, 3], _best = new double[MaxCars, 3];
    readonly bool[,] _secMem = new bool[MaxCars, 3];
    readonly double[][] _bestSplits = new double[MaxCars][];   // parciais acumuladas [S1, S1+S2, volta] da melhor volta observada
    readonly double[] _bestLapObs = new double[MaxCars];
    // Pendencia de troca pelo valor da memoria: setor k (-1 = nada), ate quando, melhor anterior.
    readonly int[] _pendSec = new int[MaxCars];
    readonly double[] _pendUntil = new double[MaxCars], _pendPrevBest = new double[MaxCars];
    readonly double[] _bLo = [0, double.NegativeInfinity, double.NegativeInfinity], _bHi = [0, double.PositiveInfinity, double.PositiveInfinity];
    double _len;
    SessionKind _kind;
    string _track = "";
    string _variation = "", _carName = "";
    int _player = -1;
    QualiSplit? _split;
    QualiLapResult? _result;
    double _resultPendUntil = double.NegativeInfinity, _resultPrevLast, _prevPlayerLast;
    double _prevPlayerBest;
    QualiLapState _state = QualiLapState.Empty;
    int _sessionGeneration;
    int _pitExitGeneration;
    bool _restartPending;
    double _lastNow = double.NaN;
    double? _remaining;

    public QualiLapTracker() => Reset();

    public QualiLapState State => _state;

    public void Reset()
    {
        _sessionGeneration++;
        _lastNow = double.NaN; _remaining = null; _restartPending = false;
        Array.Clear(_seen); Array.Fill(_pendSec, -1);
        for (int i = 0; i < MaxCars; i++) ResetCar(i);
        _bLo[1] = _bLo[2] = double.NegativeInfinity; _bHi[1] = _bHi[2] = double.PositiveInfinity;
        _split = null; _result = null; _resultPendUntil = double.NegativeInfinity; _prevPlayerBest = 0; _player = -1;
        _state = QualiLapState.Empty;
    }

    void ResetCar(int i)
    {
        _seen[i] = _previousPit[i] = false; _lapStart[i] = _secStart[i] = double.NaN; _lapBad[i] = _secBad[i] = _lapPit[i] = false;
        for (int k = 0; k < 3; k++) { _secTime[i, k] = double.NaN; _prevMem[i, k] = 0; _best[i, k] = double.NaN; _secMem[i, k] = false; }
        _bestSplits[i] = null!; _bestLapObs[i] = double.NaN; _pendSec[i] = -1;
    }

    public QualiLapState Update(double now, SessionSnapshot s)
    {
        // Pause, menu and replay retain the current stint. Loading marks a new session even on the same track.
        if (!s.InSession) { if (s.GameState == 3) _restartPending = true; return _state; }
        var observedPlayer = s.PlayerCar;
        bool restarted = _restartPending || now < _lastNow
            || (s.TimeRemainingSeconds is { } remaining && _remaining is { } previous && remaining > previous + 5)
            || (observedPlayer is { Index: >= 0 and < MaxCars } player && _seen[player.Index] && player.LapsCompleted < _laps[player.Index]);
        if (restarted || s.Kind != _kind || !string.Equals(s.Track, _track, StringComparison.Ordinal)
            || !string.Equals(s.TrackVariation, _variation, StringComparison.Ordinal)
            // PlayerCar pode ser null por um instante: so uma troca real de carro (nome nao vazio -> outro nao vazio) reseta.
            || (!string.IsNullOrEmpty(observedPlayer?.CarName) && _carName.Length > 0 && !string.Equals(observedPlayer!.CarName, _carName, StringComparison.Ordinal))
            || Math.Abs(s.TrackLength - _len) > 1)
        {
            Reset(); _kind = s.Kind; _track = s.Track; _variation = s.TrackVariation;
            _carName = observedPlayer?.CarName ?? ""; _len = s.TrackLength;
        }
        else if (_carName.Length == 0 && !string.IsNullOrEmpty(observedPlayer?.CarName)) _carName = observedPlayer.CarName;
        _lastNow = now; _remaining = s.TimeRemainingSeconds;
        if (_len <= 0 || s.Cars.Count == 0) return _state = QualiLapState.Empty;
        var pc = s.PlayerCar;
        if (pc is not null && pc.Index != _player) { _player = pc.Index; _split = null; _result = null; _prevPlayerBest = pc.BestLapTime; }

        foreach (var c in s.Cars)
        {
            int i = c.Index;
            if (i is < 0 or >= MaxCars) continue;
            if (_seen[i] && c.LapsCompleted < _laps[i]) ResetCar(i);   // sessao reiniciada para este carro
            bool pit = c.InPitLane || c.InGarage;
            if (i == _player && _seen[i] && !pit && _previousPit[i]) _pitExitGeneration++;
            // A single garage/pit sample is sufficient to identify the subsequent out lap.
            if (!_seen[i]) _lapBad[i] = _secBad[i] = _lapPit[i] = pit || (c.LapsCompleted == 0 && c.BestLapTime <= 0);
            double d = Math.Clamp(c.LapDistance, 0, _len);
            if (_seen[i])
            {
                if (pit) { _lapBad[i] = true; _secBad[i] = true; _lapPit[i] = true; }
                if (c.LapInvalid) _lapBad[i] = true;
                int prev = _sector[i], sec = c.Sector;
                if (sec != prev && prev is 0 or 1 && sec == prev + 1)
                {
                    double tm = CrossTime(i, d, Boundary(prev + 1, _dist[i], d), now, c.SpeedMps);
                    CompleteSector(i, c, prev, tm, now, s);
                }
                else if (sec != prev && !(prev == 2 && sec == 0) && c.LapsCompleted == _laps[i]) _secStart[i] = double.NaN;   // salto de setor: perde a referencia
                if (c.LapsCompleted == _laps[i] + 1)
                {
                    double tl = CrossTime(i, d, _len, now, c.SpeedMps);
                    if (_sector[i] == 2 || sec == 0) CompleteSector(i, c, 2, tl, now, s);
                    CompleteLap(i, c, tl, now, s);
                }
                else if (c.LapsCompleted != _laps[i]) { _lapStart[i] = _secStart[i] = double.NaN; ClearLap(i); }
            }
            ApplyPending(i, c, now);
            _seen[i] = true; _laps[i] = c.LapsCompleted; _sector[i] = c.Sector; _dist[i] = d; _t[i] = now;
            _previousPit[i] = pit;
            for (int k = 0; k < 3; k++) _prevMem[i, k] = c.CurSector(k);
        }

        if (pc is not null) ApplyResultMemory(pc, now);
        _state = Compose(now, s, pc);
        if (pc is not null) { _prevPlayerBest = pc.BestLapTime; _prevPlayerLast = pc.LastLapTime; }
        return _state;
    }

    // ---------------------------------------------------------------- setores e voltas

    void CompleteSector(int i, CarSnapshot c, int k, double tm, double now, SessionSnapshot s)
    {
        double derived = double.IsNaN(_secStart[i]) ? double.NaN : tm - _secStart[i];
        double mem = c.CurSector(k);
        bool memNew = mem > 0 && Math.Abs(mem - _prevMem[i, k]) > 1e-6;
        bool memOk = memNew && (double.IsNaN(derived) || Math.Abs(mem - derived) < MemoryTolerance);
        double time = memOk ? mem : derived;
        bool valid = !_secBad[i] && !_lapBad[i] && !c.InPitLane && !c.InGarage;
        _pendPrevBest[i] = _best[i, k];
        if (!double.IsNaN(time))
        {
            _secTime[i, k] = time; _secMem[i, k] = memOk;
            if (valid && (double.IsNaN(_best[i, k]) || time < _best[i, k])) _best[i, k] = time;
            if (!memOk && valid) { _pendSec[i] = k; _pendUntil[i] = now + MemorySettleSeconds; }
        }
        _secStart[i] = tm; _secBad[i] = false;
        if (i == _player && k < 2 && !double.IsNaN(_lapStart[i]) && !double.IsNaN(time))
        {
            double elapsed = tm - _lapStart[i];
            int leader = LeaderIndex(s);
            _split = new QualiSplit(k + 1, elapsed, SplitDelta(i, k, elapsed), leader >= 0 ? SplitDelta(leader, k, elapsed) : null, tm);
        }
    }

    double? SplitDelta(int car, int k, double elapsed) => _bestSplits[car] is { } sp ? elapsed - sp[k] : null;

    /// <summary>Valor da memoria que chegou depois da troca de setor: substitui o derivado (e corrige o melhor) enquanto a janela estiver aberta.</summary>
    void ApplyPending(int i, CarSnapshot c, double now)
    {
        int k = _pendSec[i];
        if (k < 0) return;
        if (now > _pendUntil[i]) { _pendSec[i] = -1; return; }
        double mem = c.CurSector(k);
        if (mem <= 0 || Math.Abs(mem - _prevMem[i, k]) < 1e-6 || Math.Abs(mem - _secTime[i, k]) >= MemoryTolerance) return;
        if (k < 2) { _secTime[i, k] = mem; _secMem[i, k] = true; }   // S3: a volta ja foi fechada, so o melhor e corrigido
        _best[i, k] = double.IsNaN(_pendPrevBest[i]) ? mem : Math.Min(_pendPrevBest[i], mem);
        _pendSec[i] = -1;
    }

    void CompleteLap(int i, CarSnapshot c, double tl, double now, SessionSnapshot s)
    {
        double lap = double.IsNaN(_lapStart[i]) ? double.NaN : tl - _lapStart[i];
        bool valid = !_lapBad[i] && !c.LapInvalid && !double.IsNaN(lap);
        var sectors = SectorsOf(i, s);
        if (valid && !double.IsNaN(_secTime[i, 0]) && !double.IsNaN(_secTime[i, 1]) && (double.IsNaN(_bestLapObs[i]) || lap < _bestLapObs[i]))
        {
            _bestLapObs[i] = lap;
            _bestSplits[i] = [_secTime[i, 0], _secTime[i, 0] + _secTime[i, 1], lap];
        }
        if (i == _player && !double.IsNaN(lap))
        {
            _result = MakeResult(c, lap, !valid, sectors, tl, s, fromMemory: false);
            _resultPendUntil = now + MemorySettleSeconds + 0.5;
            _resultPrevLast = _prevPlayerLast;
            _split = null;
        }
        _lapStart[i] = tl; _lapBad[i] = _lapPit[i] = c.InPitLane || c.InGarage;
        ClearLap(i);
    }

    QualiLapResult MakeResult(CarSnapshot c, double lap, bool invalid, IReadOnlyList<QualiSector?> sectors, double at, SessionSnapshot s, bool fromMemory)
    {
        double before = Positive(_prevPlayerBest) ?? double.NaN;
        double my = invalid ? before : double.IsNaN(before) ? lap : Math.Min(before, lap);
        var others = s.Cars.Where(o => o.Index != c.Index && o.BestLapTime > 0).Select(o => o.BestLapTime).ToList();
        int pos = double.IsNaN(my) ? others.Count + 1 : 1 + others.Count(b => b < my);
        double? gap = others.Count > 0 ? lap - others.Min() : null;
        double? dPers = double.IsNaN(before) ? null : lap - before;
        bool improved = !invalid && (double.IsNaN(before) || lap < before);
        return new QualiLapResult(c.CurrentLap > 0 ? c.CurrentLap - 1 : c.LapsCompleted, lap, pos, gap, dPers, improved, invalid, sectors, at, fromMemory) { ReferenceCar = s.Cars.Where(o => o.Index != c.Index && double.IsFinite(o.BestLapTime) && o.BestLapTime > 0).MinBy(o => o.BestLapTime) };
    }

    /// <summary>LastLapTime do jogo que muda logo depois da linha e bate com o derivado: vira o tempo oficial do resultado.</summary>
    void ApplyResultMemory(CarSnapshot pc, double now)
    {
        if (_result is not { FromMemory: false } r || now > _resultPendUntil) return;
        double v = pc.LastLapTime;
        if (v <= 0 || Math.Abs(v - _resultPrevLast) < 1e-6 || Math.Abs(v - r.LapTime) >= MemoryTolerance) return;
        double? gap = r.GapToFirst is { } g ? g + (v - r.LapTime) : null;
        double? dp = r.DeltaPersonal is { } d ? d + (v - r.LapTime) : null;
        _result = r with { LapTime = v, GapToFirst = gap, DeltaPersonal = dp, Improved = !r.Invalid && (dp is null || dp < 0), FromMemory = true };
    }

    void ClearLap(int i) { for (int k = 0; k < 3; k++) { _secTime[i, k] = double.NaN; _secMem[i, k] = false; } }

    // ---------------------------------------------------------------- composicao

    QualiLapState Compose(double now, SessionSnapshot s, CarSnapshot? pc)
    {
        if (pc is null) return QualiLapState.Empty;
        int i = pc.Index;
        var overall = new double?[3];
        var personal = new double?[3];
        for (int k = 0; k < 3; k++)
        {
            personal[k] = BestOf(i, k, pc);
            foreach (var c in s.Cars)
                if (c.Index is >= 0 and < MaxCars && BestOf(c.Index, k, c) is { } b && (overall[k] is null || b < overall[k])) overall[k] = b;
        }
        int leader = LeaderIndex(s);
        double? elapsed = double.IsNaN(_lapStart[i]) || pc.InGarage ? null : Math.Max(0, now - _lapStart[i]);
        bool pit = pc.InPitLane || pc.InGarage;
        return new QualiLapState(i, pc.CurrentLap, Math.Clamp(pc.Sector, 0, 2), pit, !pit && _lapPit[i], elapsed,
            SectorsOf(i, s), personal, overall, _split, Positive(pc.BestLapTime) ?? Nan(_bestLapObs[i]),
            leader >= 0 ? s.Cars.First(c => c.Index == leader).BestLapTime : null, leader, _result) { SessionGeneration = _sessionGeneration, PitExitGeneration = _pitExitGeneration };
    }

    /// <summary>Setores concluidos da volta atual do carro, com a cor (comparados aos melhores atuais).</summary>
    IReadOnlyList<QualiSector?> SectorsOf(int i, SessionSnapshot s)
    {
        var list = new QualiSector?[3];
        var car = s.Cars.FirstOrDefault(c => c.Index == i);
        for (int k = 0; k < 3; k++)
        {
            double t = _secTime[i, k];
            if (double.IsNaN(t)) continue;
            double? overall = null;
            foreach (var c in s.Cars)
                if (c.Index is >= 0 and < MaxCars && BestOf(c.Index, k, c) is { } b && (overall is null || b < overall)) overall = b;
            double? mine = car is null ? Nan(_best[i, k]) : BestOf(i, k, car);
            var mark = _lapBad[i] ? SectorMark.None
                : overall is { } o && t <= o + 1e-6 ? SectorMark.OverallBest
                : mine is { } m && t <= m + 1e-6 ? SectorMark.PersonalBest : SectorMark.Slower;
            list[k] = new QualiSector(k, t, mark, _secMem[i, k]);
        }
        return list;
    }

    double? BestOf(int i, int k, CarSnapshot c)
    {
        double obs = _best[i, k], mem = c.BestSector(k);
        if (mem > 0 && (double.IsNaN(obs) || mem < obs)) return mem;
        return Nan(obs);
    }

    static int LeaderIndex(SessionSnapshot s)
    {
        int idx = -1; double best = double.MaxValue;
        foreach (var c in s.Cars) if (c.BestLapTime > 0 && c.BestLapTime < best) { best = c.BestLapTime; idx = c.Index; }
        return idx;
    }

    static double? Positive(double v) => v > 0 ? v : null;
    static double? Nan(double v) => double.IsNaN(v) ? null : v;

    // ---------------------------------------------------------------- interpolacao

    /// <summary>Instante interpolado da passagem pela distancia <paramref name="mark"/> entre a amostra anterior e a atual
    /// (<paramref name="mark"/> = comprimento da pista: linha, desenrolando a distancia).</summary>
    double CrossTime(int i, double d, double mark, double now, double speed)
    {
        double d0 = _dist[i], t0 = _t[i];
        double d1 = mark >= _len - 1e-9 && d < d0 ? d + _len : d;
        double step = d1 - d0;
        if (now > t0 && step > 0 && step <= MaxStepMeters)
            return t0 + Math.Clamp((mark - d0) / step, 0, 1) * (now - t0);
        // Sem desenrolar (o contador de voltas atrasou um quadro): a linha ficou d metros atras.
        double back = (mark >= _len - 1e-9 ? d1 : Math.Max(0, d1 - mark)) / Math.Max(speed, 1);
        return Math.Max(now - back, Math.Min(t0, now));
    }

    /// <summary>Marca do setor aprendida pela intersecao dos intervalos [antes, depois] de todas as passagens.</summary>
    double Boundary(int k, double d0, double d1)
    {
        if (d1 <= d0) return d1;
        double lo = Math.Max(_bLo[k], d0), hi = Math.Min(_bHi[k], d1);
        if (lo > hi) { lo = d0; hi = d1; }
        _bLo[k] = lo; _bHi[k] = hi;
        return (lo + hi) / 2;
    }
}
