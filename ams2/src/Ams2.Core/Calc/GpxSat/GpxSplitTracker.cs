namespace Ams2.Core.Calc.GpxSat;

/// <summary>
/// Janela de gap do GPxSAT numa passagem de split (4.12 gap a frente / 4.13 gap atras). <see cref="Gap"/> null = carro de tras
/// ainda nao cruzou (o GPx mostra o cronometro correndo desde <see cref="OpenT"/>). <see cref="Laps"/> &gt; 0 = diferenca em voltas.
/// <see cref="Change"/> = gap desta passagem menos o da passagem anterior com o mesmo carro (linha amarela "(+-x)").
/// </summary>
public sealed record GpxGapWindow(bool Ahead, CarSnapshot Focus, CarSnapshot Other, int Pass, double OpenT, double CloseT,
    double? Gap, int Laps, double? Change)
{
    public int Split => Pass % 3;   // 0 = linha, 1 = intermediario 1, 2 = intermediario 2
    public bool ActiveAt(double now) => now >= OpenT && now < CloseT;
    /// <summary>Cronometro do gap atras ainda em aberto.</summary>
    public double Running(double now) => Math.Max(0, now - OpenT);
}

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
    readonly Dictionary<(bool Ahead, int Other), double> _lastGap = [];
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
        double? ag = a.Laps == 0 ? a.Gap : null, bg = b.Laps == 0 ? b.Gap : null;
        if (ag is null) return b;
        if (bg is null) return a;
        return bg < ag ? b : a;
    }

    public void Reset()
    {
        _cars.Clear(); _lastGap.Clear(); _key = null; _last = double.NegativeInfinity; Ahead = Behind = null; Trap = null; LeaderLapT = null;
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
                Behind = b with { Other = c, Gap = gap, CloseT = now + HoldSeconds, Change = ChangeOf(false, c.Index, gap) };
            }
        if (Behind is { Gap: null, Laps: 0 } open && now - open.OpenT > MaxOpenSeconds) Behind = null;
    }

    double? ChangeOf(bool ahead, int other, double gap)
    {
        double? change = _lastGap.TryGetValue((ahead, other), out var prev) ? gap - prev : null;
        _lastGap[(ahead, other)] = gap;
        return change;
    }

    void OnFocusPass(double now, SessionSnapshot s, CarSnapshot f, int pass, bool race)
    {
        int trap = pass % 3;
        var tr = _cars[f.Index];
        var top = _top[trap].Car >= 0 ? s.Cars.FirstOrDefault(c => c.Index == _top[trap].Car) : null;
        if (!f.InPitLane)
            Trap = new GpxTrapWindow(f, trap, f.SpeedMps, tr.BestTrap[trap], top, _top[trap].Mps, now, now + HoldSeconds);
        if (!race || f.InPitLane || f.Position <= 0) return;

        Ahead = null; Behind = null;
        var ahead = s.Cars.FirstOrDefault(c => c.Position == f.Position - 1 && Running(c));
        if (ahead is not null && _cars.TryGetValue(ahead.Index, out var at))
        {
            int laps = Math.Max(0, (at.Pass - pass) / 3);
            if (laps >= 1) Ahead = new GpxGapWindow(true, f, ahead, pass, now, now + HoldSeconds, null, laps, null);
            else if (at.Times.TryGetValue(pass, out var t))
            {
                double gap = now - t;
                Ahead = new GpxGapWindow(true, f, ahead, pass, now, now + HoldSeconds, gap, 0, ChangeOf(true, ahead.Index, gap));
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
