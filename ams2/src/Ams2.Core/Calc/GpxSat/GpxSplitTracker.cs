namespace Ams2.Core.Calc.GpxSat;

/// <summary>
/// Janela de gap do GPxSAT numa passagem de split (4.12 gap a frente / 4.13 gap atras). <see cref="Gap"/> null = carro de tras
/// ainda nao cruzou (o GPx mostra o cronometro correndo desde <see cref="OpenT"/>). <see cref="Laps"/> &gt; 0 = diferenca em voltas.
/// <see cref="Previous"/> = gap medido no setor anterior com o mesmo carro (segunda linha da tela): no fim do S2 o do S1, no fim do S1 o do S2 da volta
/// anterior; null = sem medida (primeira volta, carro diferente). A linha de chegada (passagem 3N) nao abre janela de gap: e so classificacao.
/// </summary>
public sealed record GpxGapWindow(bool Ahead, CarSnapshot Focus, CarSnapshot Other, int Pass, double OpenT, double CloseT,
    double? Gap, int Laps, double? Previous)
{
    public int Split => Pass % 3;   // 0 = linha, 1 = intermediario 1, 2 = intermediario 2
    public bool ActiveAt(double now) => now >= OpenT && now < CloseT;
    /// <summary>Cronometro do gap atras ainda em aberto.</summary>
    public double Running(double now) => Math.Max(0, now - OpenT);
}

/// <summary>
/// Carro mais proximo na linha de chegada do jogador (comparativo de melhor volta, a cada 3 voltas): o mesmo criterio do <see cref="GpxSplitTracker.Nearest"/>
/// (menor gap; lider usa o de tras, ultimo o da frente), com o gap medido no fim do S2 da mesma volta. <see cref="Gap"/> null = sem medida.
/// </summary>
public sealed record GpxLapNeighbor(bool Ahead, int Focus, int Other, int Lap, double OpenT, double? Gap);

/// <summary>Speed trap (4.14) numa passagem do carro em foco: velocidade agora, a melhor dele e a maior do trap na sessao.</summary>
public sealed record GpxTrapWindow(CarSnapshot Focus, int Trap, double SpeedMps, double FocusBestMps, CarSnapshot? Top, double TopMps,
    double OpenT, double CloseT)
{
    public bool ActiveAt(double now) => now >= OpenT && now < CloseT;
}

/// <summary>
/// Passagens pelos 3 marcadores de volta (troca de <see cref="CarSnapshot.Sector"/>; passagem = voltas*3 + setor) de todos os carros,
/// para o gap a frente/atras do carro em foco e o speed trap. Aproximacoes do AMS2 (lacunas): o GPx usa os splits da pista e 3 traps
/// em pontos proprios (SpeedTrapWidth); aqui os splits e os traps sao as trocas de setor, com o tempo do quadro (60 Hz).
/// </summary>
public sealed class GpxSplitTracker
{
    /// <summary>Tempo na tela depois da passagem (gap a frente, trap) ou do congelamento (gap atras).</summary>
    public double HoldSeconds { get; set; } = 8;
    /// <summary>Cronometro do gap atras desiste se o carro de tras nao cruzar neste tempo.</summary>
    public double MaxOpenSeconds { get; set; } = 30;

    sealed class CarTrack
    {
        public int Pass = int.MinValue;
        public int Laps, Sector = -1;
        public readonly Dictionary<int, double> Times = [];
        public readonly double[] BestTrap = new double[3];
        public void Add(int pass, double t)
        {
            Times[pass] = t;
            if (Times.Count > 12) Times.Remove(Times.Keys.Min());
        }
    }

    readonly Dictionary<int, CarTrack> _cars = [];
    readonly (int Car, double Mps)[] _top = [(-1, 0), (-1, 0), (-1, 0)];
    readonly Dictionary<(bool Ahead, int Sector), (int Other, int Pass, double Gap)> _lastGap = [];
    string? _key;
    double _last = double.NegativeInfinity;

    public GpxGapWindow? Ahead { get; private set; }
    public GpxGapWindow? Behind { get; private set; }
    public GpxTrapWindow? Trap { get; private set; }
    /// <summary>Instante em que o lider da corrida fechou a ultima volta (cruzou a linha); abre a janela dos standings. Null = nenhuma ainda.</summary>
    public double? LeaderLapT { get; private set; }

    /// <summary>
    /// Modo "nearest": entre a janela da frente e a de tras (so as abertas em <paramref name="now"/>), a de menor gap em segundos.
    /// Um lado so = ele (lider mostra o de tras, ultimo o da frente). Gap de tras ainda correndo ou diferenca em voltas nao compete:
    /// fica a frente ate o de tras fechar com gap menor.
    /// </summary>
    public GpxGapWindow? Nearest(double now)
    {
        var a = Ahead is { } ga && ga.ActiveAt(now) ? ga : null;
        var b = Behind is { } gb && gb.ActiveAt(now) ? gb : null;
        if (a is null || b is null) return a ?? b;
        return PickBehind(a.Laps == 0 ? a.Gap : null, b.Laps == 0 ? b.Gap : null) ? b : a;
    }

    /// <summary>Regra comum do "mais proximo" com os dois lados presentes: true = o de tras (sem gap da frente, ou gap de tras menor); sem gap de tras = o da frente.</summary>
    static bool PickBehind(double? aheadGap, double? behindGap)
    {
        if (aheadGap is null) return true;
        if (behindGap is null) return false;
        return behindGap < aheadGap;
    }

    /// <summary>Vizinho da ultima linha de chegada do jogador na corrida (null = nenhuma ainda / fora da corrida / nos boxes).</summary>
    public GpxLapNeighbor? LapNeighbor { get; private set; }

    public void Reset()
    {
        _cars.Clear(); _lastGap.Clear(); _key = null; _last = double.NegativeInfinity; Ahead = Behind = null; Trap = null; LeaderLapT = null; LapNeighbor = null;
        for (int i = 0; i < 3; i++) _top[i] = (-1, 0);
    }

    static int PassOf(CarSnapshot c) => c.LapsCompleted * 3 + Math.Clamp(c.Sector, 0, 2);
    static bool Running(CarSnapshot c) => c.RaceState is not (RaceState.Retired or RaceState.Dnf or RaceState.Disqualified) && !c.InGarage;

    public void Update(double now, SessionSnapshot s)
    {
        string key = s.Track + "\n" + s.Kind;
        if (_key != key || now < _last) { Reset(); _key = key; }
        _last = now;
        var focus = s.PlayerCar;
        bool race = s.Kind == SessionKind.Race;

        var crossed = new List<(CarSnapshot Car, int Pass)>();
        foreach (var c in s.Cars)
        {
            if (!_cars.TryGetValue(c.Index, out var tr)) _cars[c.Index] = tr = new CarTrack();
            int pass = PassOf(c);
            // O AMS2 pode subir LapsCompleted um quadro antes do Sector zerar: volta +1 com o setor ainda no ultimo = a linha ja foi cruzada.
            if (tr.Pass != int.MinValue && c.LapsCompleted == tr.Laps + 1 && tr.Sector == 2 && c.Sector == 2) pass = c.LapsCompleted * 3;
            tr.Laps = c.LapsCompleted; tr.Sector = c.Sector;
            if (tr.Pass == int.MinValue) { tr.Pass = pass; continue; }   // primeiro quadro: so referencia
            if (pass <= tr.Pass || pass - tr.Pass > 3) { tr.Pass = pass; continue; }   // sem passagem (ou salto/teleporte)
            tr.Pass = pass;
            tr.Add(pass, now);
            int trap = pass % 3;
            if (!c.InPitLane && c.SpeedMps > 0)
            {
                tr.BestTrap[trap] = Math.Max(tr.BestTrap[trap], c.SpeedMps);
                if (c.SpeedMps > _top[trap].Mps) _top[trap] = (c.Index, c.SpeedMps);
            }
            if (race && pass % 3 == 0 && c.Position == 1 && Running(c)) LeaderLapT = now;
            crossed.Add((c, pass));
        }
        // Record all crossings before resolving pairs: car enumeration order must not change the gap.
        foreach (var (c, pass) in crossed)
            if (focus is not null && c.Index == focus.Index) OnFocusPass(now, s, c, pass, race);
        foreach (var (c, pass) in crossed)
            if (focus is not null && c.Index != focus.Index && Behind is { Gap: null, Laps: 0 } b && b.Other.Index == c.Index && pass == b.Pass)
            {
                double gap = Math.Max(0, now - b.OpenT);
                Behind = b with { Other = c, Gap = gap, CloseT = now + HoldSeconds, Previous = PreviousOf(false, c.Index, b.Pass, gap) };
            }
        if (Behind is { Gap: null, Laps: 0 } open && now - open.OpenT > MaxOpenSeconds) Behind = null;
    }

    /// <summary>Gap do mesmo lado e carro no setor anterior (S2 le o S1; S1 le o S2 da volta anterior) e registra o desta passagem.</summary>
    double? PreviousOf(bool ahead, int other, int pass, double gap)
    {
        int sector = pass % 3, prevPass = sector == 2 ? pass - 1 : pass - 2;
        double? previous = _lastGap.TryGetValue((ahead, prevPass % 3), out var p) && p.Other == other && p.Pass == prevPass ? p.Gap : null;
        _lastGap[(ahead, sector)] = (other, pass, gap);
        return previous;
    }

    /// <summary>Mais proximo na linha: vizinhos P-1 e P+1 (em pista) com o gap do fim do S2 desta volta (o ultimo medido); mesma regra do <see cref="Nearest"/>.</summary>
    GpxLapNeighbor? ResolveLapNeighbor(double now, SessionSnapshot s, CarSnapshot f, int pass)
    {
        var ahead = s.Cars.FirstOrDefault(c => c.Position == f.Position - 1 && Running(c));
        var behind = s.Cars.FirstOrDefault(c => c.Position == f.Position + 1 && Running(c));
        double? GapOf(bool isAhead, CarSnapshot? c) => c is not null && _lastGap.TryGetValue((isAhead, 2), out var g) && g.Other == c.Index && g.Pass == pass - 1 ? g.Gap : null;
        double? ag = GapOf(true, ahead), bg = GapOf(false, behind);
        bool pickBehind = ahead is null ? behind is not null : behind is not null && PickBehind(ag, bg);
        var other = pickBehind ? behind : ahead;
        return other is null ? null : new GpxLapNeighbor(!pickBehind, f.Index, other.Index, pass / 3, now, pickBehind ? bg : ag);
    }

    void OnFocusPass(double now, SessionSnapshot s, CarSnapshot f, int pass, bool race)
    {
        int trap = pass % 3;
        var tr = _cars[f.Index];
        var top = _top[trap].Car >= 0 ? s.Cars.FirstOrDefault(c => c.Index == _top[trap].Car) : null;
        if (!f.InPitLane)
            Trap = new GpxTrapWindow(f, trap, f.SpeedMps, tr.BestTrap[trap], top, _top[trap].Mps, now, now + HoldSeconds);
        if (!race || f.InPitLane || f.Position <= 0) { if (pass % 3 == 0) LapNeighbor = null; return; }

        Ahead = null; Behind = null;
        if (pass % 3 == 0) { LapNeighbor = ResolveLapNeighbor(now, s, f, pass); return; }   // linha de chegada = so classificacao; o gap sai no fim do S1 e do S2
        var ahead = s.Cars.FirstOrDefault(c => c.Position == f.Position - 1 && Running(c));
        if (ahead is not null && _cars.TryGetValue(ahead.Index, out var at))
        {
            int laps = Math.Max(0, (at.Pass - pass) / 3);
            if (laps >= 1) Ahead = new GpxGapWindow(true, f, ahead, pass, now, now + HoldSeconds, null, laps, null);
            else if (at.Times.TryGetValue(pass, out var t))
            {
                double gap = now - t;
                Ahead = new GpxGapWindow(true, f, ahead, pass, now, now + HoldSeconds, gap, 0, PreviousOf(true, ahead.Index, pass, gap));
            }
        }
        var behind = s.Cars.FirstOrDefault(c => c.Position == f.Position + 1 && Running(c));
        if (behind is not null && _cars.TryGetValue(behind.Index, out var bt))
        {
            int laps = Math.Max(0, (pass - bt.Pass) / 3);
            Behind = laps >= 1
                ? new GpxGapWindow(false, f, behind, pass, now, now + HoldSeconds, null, laps, null)
                : new GpxGapWindow(false, f, behind, pass, now, now + MaxOpenSeconds + HoldSeconds, null, 0, null);
        }
    }
}
