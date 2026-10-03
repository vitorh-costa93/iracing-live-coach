namespace Ams2.Core.Calc;

/// <summary>
/// Lógica do widget rotativo inferior ("board"), inspirado nos gráficos da transmissão de F1 (2003/2006). Puro: recebe o
/// relógio (<c>now</c>, segundos monotônicos do provider), o <see cref="SessionSnapshot"/> e o <see cref="GapTracker"/> já
/// atualizado, e devolve um <see cref="BoardState"/> imutável. Especificação completa: ams2/reference/board-spec.md.
///
/// Modos, do mais para o menos prioritário:
/// 1. LineTower (só corrida): a cada volta do líder, torre por ordem de passagem na linha em páginas de 8 (4 + 4).
/// 2. SectorGap: na passagem de cada marca de setor, gap jogador × vizinho mais próximo, da 1ª passagem do campo até 2 s
///    depois da última (teto 40 s).
/// 3. LapComparison (só corrida): nas voltas completas múltiplas de 3, as 3 últimas voltas do jogador × vizinho.
/// 4. DriverPlate: legenda do jogador.
///
/// REGRA DO VIZINHO (usada por SectorGap e LapComparison):
/// - candidatos = carro imediatamente à frente e imediatamente atrás do jogador; em corrida pela posição oficial
///   (P−1 e P+1, pulando quem está inelegível), fora de corrida pela posição física na pista (Relative: lado mais curto);
/// - inelegíveis: o próprio jogador, carros InPit (parados no box), na garagem (InGarage/DrivingOutOfGarage), Retired/DNF/DSQ;
/// - escolhe o de menor |gap em tempo| (GapTracker; sem dado, |distância| / velocidade);
/// - empate ou diferença &lt; 0,05 s (<see cref="BoardOptions.NeighborTieSeconds"/>) → o da frente;
/// - histerese: o vizinho escolhido na abertura da janela de setor (ou na volta do comparativo) não muda até ela fechar;
///   só é trocado se o carro sumir da sessão ou for para a garagem.
/// </summary>
public sealed class BoardTracker
{
    const int MaxCars = GapTracker.MaxCars;
    const double MaxStepMeters = 500;   // salto maior = teleporte/reset: cruzamento sem interpolação
    const int HistLen = 8;

    readonly BoardOptions _o;

    // Sessão
    bool _init;
    SessionKind _kind;
    string _track = "";
    double _len;
    int _maxLaps;
    double _lastNow = double.NegativeInfinity;
    long _rev;

    // Amostra anterior por carro
    readonly bool[] _seen = new bool[MaxCars];
    readonly int[] _laps = new int[MaxCars];
    readonly int[] _sector = new int[MaxCars];
    readonly double[] _dist = new double[MaxCars];
    readonly double[] _t = new double[MaxCars];

    // Histórico de voltas (anel por carro) e LastLapTime pendente
    readonly int[][] _histLap = new int[MaxCars][];
    readonly double[][] _histTime = new double[MaxCars][];
    readonly int[] _histN = new int[MaxCars];
    int _histVersion;
    readonly int[] _pendLap = new int[MaxCars];
    readonly double[] _pendSince = new double[MaxCars];
    readonly double[] _pendPrev = new double[MaxCars];
    readonly double[] _lastLapSeen = new double[MaxCars];

    // Fronteiras de setor aprendidas (índices 1 e 2; a 3 é a linha): intervalo [lo, hi] que contém a marca
    readonly double[] _bLo = new double[3], _bHi = new double[3];
    // Último cruzamento de cada marca por carro (para pré-preencher o vizinho que já passou)
    readonly double[,] _markT = new double[MaxCars, 4];
    // Amostras em volta do último cruzamento de marca de cada carro: o split jogador × vizinho é reinterpolado com a
    // mesma estimativa (a mais refinada) da marca para os dois, então o erro da estimativa praticamente se cancela.
    readonly double[] _segD0 = new double[MaxCars], _segT0 = new double[MaxCars], _segD1 = new double[MaxCars], _segT1 = new double[MaxCars];
    readonly int[] _segMarker = new int[MaxCars];

    // Eventos do quadro (reutilizado)
    readonly List<Ev> _events = new(MaxCars * 2);
    readonly record struct Ev(int Car, int Marker, double T);   // Marker 0 = volta completa; 1..3 = marca de setor

    // Torre
    bool _roundOn;
    int _roundLap;
    double _roundT, _lastCrossT, _completeT = double.NaN;
    readonly List<(int Car, double T, int Laps)> _crosses = new(MaxCars);
    readonly List<BoardTowerEntry> _entries = new(MaxCars);
    readonly bool[] _inRound = new bool[MaxCars];
    BoardTower? _tower;
    int _towerPage = -1, _towerPageCount = -1;

    // Janela de setor
    bool _secOn;
    int _secMarker, _secNb = -1;
    double _secOpenT, _secLastT, _secDoneT = double.NaN;
    readonly double[] _secCross = new double[MaxCars];
    BoardSectorGap? _sectorGap;

    // Comparativo
    int _cmpLaps = -1, _cmpNb = -1, _cmpHist = -1;
    BoardLapComparison? _cmp;

    // Legenda
    BoardDriver? _plate;
    CarSnapshot? _plateSrc;

    BoardMode _mode;
    double _modeSince;
    BoardState _state = BoardState.Empty;

    public BoardTracker(BoardOptions? options = null)
    {
        _o = options ?? BoardOptions.Default;
        Reset();
    }

    public BoardOptions Options => _o;
    public BoardState State => _state;

    /// <summary>Reset total (reinício de sessão, troca de pista/tipo de sessão, relógio voltou, reconexão).</summary>
    public void Reset()
    {
        _init = false; _kind = SessionKind.Invalid; _track = ""; _len = 0; _maxLaps = 0;
        _lastNow = double.NegativeInfinity;
        Array.Clear(_seen); Array.Clear(_histN); Array.Clear(_pendLap); Array.Clear(_lastLapSeen); Array.Clear(_inRound);
        _histVersion = 0;
        for (int k = 0; k < 3; k++) { _bLo[k] = double.NegativeInfinity; _bHi[k] = double.PositiveInfinity; }
        for (int i = 0; i < MaxCars; i++) for (int k = 0; k < 4; k++) _markT[i, k] = double.NaN;
        _roundOn = false; _roundLap = 0; _completeT = double.NaN; _crosses.Clear(); _entries.Clear();
        _tower = null; _towerPage = _towerPageCount = -1;
        CloseSector();
        _cmpLaps = _cmpNb = _cmpHist = -1; _cmp = null;
        _plate = null; _plateSrc = null;
        _mode = BoardMode.None; _modeSince = 0;
        _rev++;
        _state = BoardState.Empty with { Revision = _rev };
    }

    public BoardState Update(double now, SessionSnapshot s, GapTracker gaps)
    {
        if (!s.InSession || s.TrackLength <= 0 || s.Cars.Count == 0)
        {
            if (_init) Reset();
            return _state = BoardState.Empty with { Revision = _rev, Now = now };
        }

        int leaderLaps = 0;
        foreach (var c in s.Cars) leaderLaps = Math.Max(leaderLaps, c.LapsCompleted);
        if (!_init || s.Kind != _kind || s.Track != _track || Math.Abs(s.TrackLength - _len) > 0.5 || leaderLaps < _maxLaps || now < _lastNow)
        {
            Reset();
            _init = true; _kind = s.Kind; _track = s.Track; _len = s.TrackLength;
            _roundLap = leaderLaps; // entrou no meio da corrida: espera a próxima volta do líder
        }
        _maxLaps = leaderLaps;
        _lastNow = now;
        bool race = s.Kind == SessionKind.Race;
        var me = s.PlayerCar;

        CollectEvents(now, s);
        foreach (var e in _events)
        {
            var c = Find(s, e.Car)!;
            if (e.Marker == 0) { if (race) OnLine(c, e.T, s); }
            else OnMarker(c, e.Marker, e.T, now, s, me, gaps, race);
        }

        UpdateTower(now, s, race);
        UpdateSector(now, s, me, gaps, race);
        UpdateComparison(now, s, me, gaps, race);
        UpdatePlate(s, me);
        return _state = Compose(now, race);
    }

    // ---------------------------------------------------------------- eventos (sub-quadro)

    void CollectEvents(double now, SessionSnapshot s)
    {
        _events.Clear();
        foreach (var c in s.Cars)
        {
            int i = c.Index;
            if (i is < 0 or >= MaxCars) continue;
            double d = Wrap(c.LapDistance);
            if (_seen[i])
            {
                if (c.LapsCompleted == _laps[i] + 1 && !c.InGarage)
                {
                    _events.Add(new Ev(i, 0, CrossTime(i, d, _len, now, c.SpeedMps)));
                    _pendLap[i] = c.LapsCompleted; _pendSince[i] = now; _pendPrev[i] = _lastLapSeen[i];
                }
                int prev = _sector[i], sec = c.Sector;
                if (sec != prev && prev is >= 0 and <= 2 && sec is >= 0 and <= 2 && sec == (prev + 1) % 3 && !c.InGarage)
                {
                    int marker = sec == 0 ? 3 : sec;
                    double b = marker == 3 ? _len : Boundary(marker, _dist[i], d);
                    double t = CrossTime(i, d, b, now, c.SpeedMps);
                    _events.Add(new Ev(i, marker, t));
                    _markT[i, marker] = t;
                    _segD0[i] = _dist[i]; _segT0[i] = _t[i]; _segD1[i] = marker == 3 && d < _dist[i] ? d + _len : d; _segT1[i] = now; _segMarker[i] = marker;
                }
            }
            TrackLapTime(i, c, now);
            _seen[i] = true; _laps[i] = c.LapsCompleted; _sector[i] = c.Sector; _dist[i] = d; _t[i] = now;
        }
        if (_events.Count > 1) _events.Sort((a, b) => a.T.CompareTo(b.T));
    }

    /// <summary>Instante interpolado em que o carro passou pela distância <paramref name="mark"/> (na volta anterior se
    /// <paramref name="mark"/> = comprimento da pista) entre a amostra anterior e a atual.</summary>
    double CrossTime(int i, double d, double mark, double now, double speed)
    {
        double d0 = _dist[i], t0 = _t[i];
        double d1 = mark >= _len - 1e-9 && d < d0 ? d + _len : d;     // cruzou a linha: desenrola
        double step = d1 - d0;
        if (now > t0 && step > 0 && step <= MaxStepMeters)
            return t0 + Math.Clamp((mark - d0) / step, 0, 1) * (now - t0);
        double back = Math.Max(0, d1 - mark) / Math.Max(speed, 1);
        return Math.Max(now - back, Math.Min(t0, now));
    }

    /// <summary>Marca de setor aprendida: todos os carros cruzam a mesma marca, então a interseção dos intervalos
    /// [distância antes, distância depois] converge para ela em poucas passagens.</summary>
    double Boundary(int k, double d0, double d1)
    {
        if (d1 <= d0) return d1;
        double lo = Math.Max(_bLo[k], d0), hi = Math.Min(_bHi[k], d1);
        if (lo > hi) { lo = d0; hi = d1; }   // inconsistente (ruído/pit): recomeça
        _bLo[k] = lo; _bHi[k] = hi;
        return (lo + hi) / 2;
    }

    void TrackLapTime(int i, CarSnapshot c, double now)
    {
        if (_pendLap[i] > 0)
        {
            double v = c.LastLapTime;
            bool changed = v > 0 && Math.Abs(v - _pendPrev[i]) > 1e-6;
            if (changed || now - _pendSince[i] >= _o.LapTimeSettleSeconds)
            {
                if (v > 0) RecordLap(i, _pendLap[i], v);   // voltas sem tempo (≤ 0) são descartadas
                _pendLap[i] = 0;
            }
        }
        _lastLapSeen[i] = c.LastLapTime;
    }

    void RecordLap(int i, int lap, double time)
    {
        var laps = _histLap[i] ??= new int[HistLen];
        var times = _histTime[i] ??= new double[HistLen];
        int slot = _histN[i] % HistLen;
        laps[slot] = lap; times[slot] = time;
        _histN[i]++;
        _histVersion++;
    }

    /// <summary>Tempo registrado da volta <paramref name="lap"/> do carro (null = sem registro).</summary>
    public double? LapTime(int carIndex, int lap)
    {
        if (carIndex is < 0 or >= MaxCars || _histLap[carIndex] is not { } laps) return null;
        int n = Math.Min(_histN[carIndex], HistLen);
        for (int k = 0; k < n; k++) if (laps[k] == lap) return _histTime[carIndex][k];
        return null;
    }

    // ---------------------------------------------------------------- torre da linha

    void OnLine(CarSnapshot c, double t, SessionSnapshot s)
    {
        if (c.RaceState is RaceState.Retired or RaceState.Dnf or RaceState.Disqualified) return;
        if (c.LapsCompleted > _roundLap)
        {
            // Nova volta do líder: nova rodada (descarta a anterior, mesmo incompleta).
            _roundLap = c.LapsCompleted; _roundOn = true; _roundT = t; _lastCrossT = t; _completeT = double.NaN;
            _crosses.Clear(); _entries.Clear(); Array.Clear(_inRound);
            _towerPage = _towerPageCount = -1;
        }
        else if (!_roundOn || _inRound[c.Index]) return;

        int idx = _crosses.Count;
        _crosses.Add((c.Index, t, c.LapsCompleted));
        _inRound[c.Index] = true;
        _lastCrossT = Math.Max(_lastCrossT, t);

        int size = _o.PageSize, half = (size + 1) / 2, slot = idx % size;
        BoardGapKind kind; double gap = 0; int laps = 0; string text;
        if (idx == 0) { kind = BoardGapKind.Leader; laps = _roundLap; text = BoardText.LeaderLap(_roundLap); }
        else if (_roundLap - c.LapsCompleted > 0) { kind = BoardGapKind.Laps; laps = _roundLap - c.LapsCompleted; text = BoardText.Laps(laps); }
        else { kind = BoardGapKind.Time; gap = Math.Max(0, t - _crosses[0].T); text = BoardText.Gap(gap); }
        _entries.Add(new BoardTowerEntry(slot + 1, slot < half ? 0 : 1, slot < half ? slot : slot - half, idx + 1, c.Position, c.Index,
            c.Name, BoardText.ShortName(c, s.Cars), BoardText.Code(c.Name), kind, gap, laps, text, c.IsPlayer, c.Nationality, c.TyreSupplier, t));
        _rev++;
    }

    void UpdateTower(double now, SessionSnapshot s, bool race)
    {
        if (!race || !_roundOn) { _tower = null; return; }

        int pending = 0;
        if (double.IsNaN(_completeT))
        {
            foreach (var c in s.Cars)
                if (c.Index is >= 0 and < MaxCars && Eligible(c) && !_inRound[c.Index]) pending++;
            if (pending == 0) _completeT = _lastCrossT;
        }

        int n = _crosses.Count, size = _o.PageSize;
        double start = _roundT, hold = _o.PageHoldSeconds;
        int p = 0;
        while (true)
        {
            bool full = n >= (p + 1) * size;
            bool last = !double.IsNaN(_completeT) && n <= (p + 1) * size;
            double end = full ? Math.Max(_crosses[(p + 1) * size - 1].T, start) + hold
                : last ? Math.Max(_completeT, start) + hold
                : Math.Max(_lastCrossT, start) + _o.TowerStallSeconds;
            if (now < end)
            {
                int from = p * size, count = Math.Clamp(n - from, 0, size);
                int pageCount = Math.Max(1, (n + pending + size - 1) / size);
                if (_tower is null || p != _towerPage || count != _tower.Entries.Count || pageCount != _towerPageCount
                    || end != _tower.PageEndT || !double.IsNaN(_completeT) != _tower.Complete)
                {
                    if (p != _towerPage || _tower is null || count != _tower.Entries.Count) _rev++;
                    var page = count == 0 ? Array.Empty<BoardTowerEntry>() : _entries.GetRange(from, count).ToArray();
                    _tower = new BoardTower(_roundLap, p, pageCount, size, page, n, n + pending, !double.IsNaN(_completeT), start, end);
                    _towerPage = p; _towerPageCount = pageCount;
                }
                return;
            }
            if (!full || last) break;   // última página encerrada (ou rodada travada)
            start = end; p++;
        }
        _roundOn = false; _tower = null; _towerPage = _towerPageCount = -1; _rev++;
    }

    // ---------------------------------------------------------------- janela de setor

    void OnMarker(CarSnapshot c, int marker, double t, double now, SessionSnapshot s, CarSnapshot? me, GapTracker gaps, bool race)
    {
        if (_secOn && _secMarker == marker)
        {
            if (double.IsNaN(_secCross[c.Index])) { _secCross[c.Index] = t; _secLastT = Math.Max(_secLastT, t); }
            return;
        }
        if (me is null || !Eligible(c) && c.Index != me.Index) return;

        bool open;
        if (race)
        {
            // Primeiro carro do campo: o mais adiantado em distância de corrida entre os elegíveis.
            double mine = c.TotalDistance(_len), best = mine;
            foreach (var o in s.Cars) if (Eligible(o)) best = Math.Max(best, o.TotalDistance(_len));
            open = mine >= best - 1;
        }
        else
        {
            // Fora de corrida o campo está espalhado pela pista: a janela é do par jogador + vizinho.
            open = c.Index == me.Index || SelectNeighbor(s, me, gaps, now, race)?.Index == c.Index;
        }
        if (!open) return;

        var nb = SelectNeighbor(s, me, gaps, now, race);
        if (nb is null) return;

        _secOn = true; _secMarker = marker; _secOpenT = t; _secLastT = t; _secDoneT = double.NaN; _secNb = nb.Index;
        Array.Fill(_secCross, double.NaN);
        _secCross[c.Index] = t;
        // Vizinho/jogador que já tinham cruzado esta marca pouco antes (ex.: vizinho à frente fora de corrida).
        foreach (int k in (ReadOnlySpan<int>)[me.Index, nb.Index])
        {
            double prevT = _markT[k, marker];
            if (k != c.Index && !double.IsNaN(prevT) && prevT <= t && t - prevT < _o.SectorMaxWindowSeconds) _secCross[k] = prevT;
        }
        _sectorGap = null;
        _rev++;
    }

    void UpdateSector(double now, SessionSnapshot s, CarSnapshot? me, GapTracker gaps, bool race)
    {
        if (!_secOn) { _sectorGap = null; return; }
        if (me is null) { CloseSector(); return; }

        var nb = Find(s, _secNb);
        if (nb is null || nb.InGarage)
        {
            // Única exceção à histerese: o vizinho sumiu.
            nb = SelectNeighbor(s, me, gaps, now, race);
            if (nb is null) { CloseSector(); return; }
            _secNb = nb.Index; _sectorGap = null; _rev++;
        }

        if (double.IsNaN(_secDoneT))
        {
            bool done;
            if (race)
            {
                done = true;
                foreach (var c in s.Cars)
                    if (c.Index is >= 0 and < MaxCars && Eligible(c) && double.IsNaN(_secCross[c.Index])) { done = false; break; }
            }
            else done = !double.IsNaN(_secCross[me.Index]) && !double.IsNaN(_secCross[nb.Index]);
            if (done) _secDoneT = race ? _secLastT : Math.Max(_secCross[me.Index], _secCross[nb.Index]);
        }
        double close = Math.Min(double.IsNaN(_secDoneT) ? double.PositiveInfinity : _secDoneT + _o.SectorCloseDelaySeconds,
            _secOpenT + _o.SectorMaxWindowSeconds);
        if (now >= close) { CloseSector(); _rev++; return; }

        double pc = Recross(me.Index), nc = Recross(nb.Index);
        bool split = !double.IsNaN(pc) && !double.IsNaN(nc);
        double gap;
        if (split) gap = pc - nc;   // > 0: o vizinho passou antes (à frente)
        else
        {
            double signed = SignedDistance(me, nb, race);
            gap = gaps.GapSeconds(now, me, nb, signed) ?? signed / Math.Max(signed >= 0 ? me.SpeedMps : nb.SpeedMps, 5);
        }
        bool ahead = split ? gap >= 0 : SignedDistance(me, nb, race) >= 0;

        var g = _sectorGap;
        if (g is null || g.Neighbor.CarIndex != nb.Index || g.GapSeconds != gap || g.IsSplit != split || g.CloseT != close
            || g.Player.Position != me.Position || g.Neighbor.Position != nb.Position)
            _sectorGap = new BoardSectorGap(_secMarker,
                g is not null && g.Player.Position == me.Position ? g.Player : BoardText.Driver(me, s.Cars),
                g is not null && g.Neighbor.CarIndex == nb.Index && g.Neighbor.Position == nb.Position ? g.Neighbor : BoardText.Driver(nb, s.Cars),
                ahead, gap, split, BoardText.Gap(gap), _secOpenT, close);
    }

    /// <summary>Cruzamento da marca da janela pelo carro, reinterpolado com a estimativa atual da marca (NaN = não cruzou).</summary>
    double Recross(int i)
    {
        double t = _secCross[i];
        if (double.IsNaN(t) || _segMarker[i] != _secMarker || t < _segT0[i] - 1e-9 || t > _segT1[i] + 1e-9) return t;
        double mark = _secMarker == 3 ? _len : (_bLo[_secMarker] + _bHi[_secMarker]) / 2;
        double step = _segD1[i] - _segD0[i];
        if (double.IsInfinity(mark) || step <= 0 || step > MaxStepMeters || _segT1[i] <= _segT0[i]) return t;
        return _segT0[i] + Math.Clamp((mark - _segD0[i]) / step, 0, 1) * (_segT1[i] - _segT0[i]);
    }

    void CloseSector()
    {
        _secOn = false; _secNb = -1; _secDoneT = double.NaN; _sectorGap = null;
    }

    // ---------------------------------------------------------------- comparativo de voltas

    void UpdateComparison(double now, SessionSnapshot s, CarSnapshot? me, GapTracker gaps, bool race)
    {
        int every = Math.Max(1, _o.LapComparisonEvery);
        if (!race || me is null || me.LapsCompleted < every || me.LapsCompleted % every != 0)
        {
            _cmpLaps = _cmpNb = -1; _cmp = null;
            return;
        }
        var nb = _cmpLaps == me.LapsCompleted ? Find(s, _cmpNb) : null;
        if (nb is null || nb.InGarage)
        {
            nb = SelectNeighbor(s, me, gaps, now, race);
            _cmpLaps = me.LapsCompleted; _cmpNb = nb?.Index ?? -1; _cmp = null; _cmpHist = -1;
            if (nb is null) return;
        }
        if (_cmp is not null && _cmpHist == _histVersion && _cmp.Player.Position == me.Position && _cmp.Neighbor.Position == nb.Position) return;

        _cmpHist = _histVersion;
        int count = Math.Max(1, _o.LapComparisonLaps);
        var rows = new BoardLapRow[count];
        bool any = false;
        for (int k = 0; k < count; k++)
        {
            int lap = me.LapsCompleted - k;
            double? p = lap >= 1 ? LapTime(me.Index, lap) : null, o = lap >= 1 ? LapTime(nb.Index, lap) : null;
            double? delta = p is { } a && o is { } b ? a - b : null;
            rows[k] = new BoardLapRow(lap, p, o, delta, delta < 0);
            any |= p is not null;
        }
        _cmp = any ? new BoardLapComparison(me.LapsCompleted, BoardText.Driver(me, s.Cars), BoardText.Driver(nb, s.Cars),
            SignedDistance(me, nb, race) >= 0, rows) : null;
    }

    // ---------------------------------------------------------------- legenda

    void UpdatePlate(SessionSnapshot s, CarSnapshot? me)
    {
        if (me is null) { _plate = null; _plateSrc = null; return; }
        var p = _plateSrc;
        if (p is not null && p.Index == me.Index && p.Position == me.Position && p.Name == me.Name && p.CarName == me.CarName
            && p.Nationality == me.Nationality && p.TyreSupplier == me.TyreSupplier) return;
        _plateSrc = me;
        _plate = BoardText.Driver(me, s.Cars);
    }

    // ---------------------------------------------------------------- composição

    BoardState Compose(double now, bool race)
    {
        BoardMode mode = _tower is not null ? BoardMode.LineTower
            : _sectorGap is not null ? BoardMode.SectorGap
            : _cmp is not null ? BoardMode.LapComparison
            : _plate is not null ? BoardMode.DriverPlate
            : BoardMode.None;
        if (mode != _mode) { _mode = mode; _modeSince = now; _rev++; }

        double start = _modeSince, end = double.PositiveInfinity;
        int items = 0;
        switch (mode)
        {
            case BoardMode.LineTower: start = _tower!.PageStartT; end = _tower.PageEndT; items = _tower.Entries.Count; break;
            case BoardMode.SectorGap: start = _sectorGap!.OpenT; end = _sectorGap.CloseT; items = 2; break;
            case BoardMode.LapComparison: items = _cmp!.Laps.Count; break;
            case BoardMode.DriverPlate: items = 1; break;
        }
        return new BoardState(mode, _rev, now, race, start, end, Math.Max(0, end - now), items, _tower, _sectorGap, _cmp, _plate);
    }

    // ---------------------------------------------------------------- vizinho

    /// <summary>Vizinho mais próximo do jogador pela regra documentada no topo da classe (sem histerese: quem chama guarda).</summary>
    public CarSnapshot? SelectNeighbor(SessionSnapshot s, CarSnapshot me, GapTracker gaps, double now, bool race)
    {
        CarSnapshot? a = null, b = null;
        bool byPos = race && me.Position > 0;
        double aKey = double.PositiveInfinity, bKey = double.PositiveInfinity;
        foreach (var c in s.Cars)
        {
            if (c.Index == me.Index || !Eligible(c)) continue;
            if (byPos)
            {
                if (c.Position <= 0) continue;
                int dp = c.Position - me.Position;
                if (dp < 0 && -dp < aKey) { aKey = -dp; a = c; }
                else if (dp > 0 && dp < bKey) { bKey = dp; b = c; }
            }
            else
            {
                double delta = SignedDistance(me, c, false);
                if (delta >= 0 && delta < aKey) { aKey = delta; a = c; }
                else if (delta < 0 && -delta < bKey) { bKey = -delta; b = c; }
            }
        }
        if (a is null || b is null) return a ?? b;
        double ga = AbsGap(now, me, a, race, gaps), gb = AbsGap(now, me, b, race, gaps);
        return ga <= gb + _o.NeighborTieSeconds ? a : b;
    }

    double AbsGap(double now, CarSnapshot me, CarSnapshot o, bool race, GapTracker gaps)
    {
        double signed = SignedDistance(me, o, race);
        return Math.Abs(gaps.GapSeconds(now, me, o, signed) ?? signed / Math.Max(signed >= 0 ? me.SpeedMps : o.SpeedMps, 5));
    }

    /// <summary>Metros que <paramref name="o"/> está à frente do jogador: em corrida pela distância total; fora, pelo lado
    /// mais curto da pista.</summary>
    double SignedDistance(CarSnapshot me, CarSnapshot o, bool race)
    {
        if (race) return o.TotalDistance(_len) - me.TotalDistance(_len);
        double delta = o.LapDistance - me.LapDistance;
        if (delta > _len / 2) delta -= _len; else if (delta < -_len / 2) delta += _len;
        return delta;
    }

    /// <summary>Conta para "último a passar" e pode ser vizinho: na pista (ou na pit lane andando), não parado no box nem na garagem, não abandonou.</summary>
    static bool Eligible(CarSnapshot c) =>
        !c.InGarage && c.PitState != PitState.InPit && c.RaceState is not (RaceState.Retired or RaceState.Dnf or RaceState.Disqualified);

    static CarSnapshot? Find(SessionSnapshot s, int index)
    {
        if (index < 0) return null;
        foreach (var c in s.Cars) if (c.Index == index) return c;
        return null;
    }

    double Wrap(double d) => d < 0 ? d + _len : d >= _len ? d - _len : d;
}
