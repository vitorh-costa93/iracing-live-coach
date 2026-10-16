namespace Ams2.Core.Calc.GpxSat;

/// <summary>Sky messages do GPxSAT (flags 0x9c35124..28) que o AMS2 permite detectar. Drive-through nao: a memoria nao informa penalidade.</summary>
public enum GpxSkyKind { FastestLap, OutOfRace, InPits, RidingWith }

/// <summary>Uma sky message: carro (foto do momento do evento), quando e, na volta mais rapida, o tempo.</summary>
public sealed record GpxSkyEvent(GpxSkyKind Kind, CarSnapshot Car, double At, double LapTime = 0)
{
    /// <summary>Chave estavel para o animador (tipo + carro + instante).</summary>
    public string Key => $"sky:{Kind}:{Car.Index}:{At:0.000}";
}

/// <summary>
/// Gatilhos das sky messages a partir dos quadros do jogo (equivalentes aos hooks do GP3): nova volta mais rapida da corrida,
/// abandono (RaceState vira Retired/Dnf/Disqualified), entrada nos boxes de outro carro na corrida e troca do carro em foco
/// ("riding with", qualquer sessao). O primeiro quadro so fixa a referencia. A mensagem fica <see cref="HoldSeconds"/> na tela;
/// a mais recente vence.
/// </summary>
public sealed class GpxSkyTracker
{
    public double HoldSeconds { get; set; } = 8;
    readonly FastestLapDetector _fastest = new();
    readonly Dictionary<int, (RaceState Race, PitState Pit)> _prev = [];
    string? _key;
    int _focus = -1;
    double _last = double.NegativeInfinity;
    bool _observed;

    public GpxSkyEvent? Latest { get; private set; }

    public void Reset()
    {
        _fastest.Reset(); _prev.Clear(); _key = null; _focus = -1; _last = double.NegativeInfinity; _observed = false; Latest = null;
    }

    /// <summary>Mensagem ativa em <paramref name="now"/> (null = nenhuma).</summary>
    public GpxSkyEvent? Active(double now) => Latest is { } e && now - e.At >= 0 && now - e.At < HoldSeconds ? e : null;

    public GpxSkyEvent? Update(double now, SessionSnapshot s)
    {
        string key = s.Track + "\n" + s.Kind;
        if (_key != key || now < _last) { Reset(); _key = key; }
        _last = now;
        bool race = s.Kind == SessionKind.Race;
        GpxSkyEvent? created = null;
        void Emit(GpxSkyEvent e) { if (_observed) Latest = created = e; }

        var focus = s.PlayerCar;
        int focusIndex = focus?.Index ?? -1;
        if (focusIndex != _focus)
        {
            if (_observed && focus is not null && _focus >= 0) Emit(new GpxSkyEvent(GpxSkyKind.RidingWith, focus, now));
            _focus = focusIndex;
        }

        var fl = _fastest.Update(now, s);
        if (fl is not null && race) Emit(new GpxSkyEvent(GpxSkyKind.FastestLap, fl.Car, now, fl.LapTime));

        foreach (var c in s.Cars)
        {
            if (_prev.TryGetValue(c.Index, out var p) && race)
            {
                bool outNow = c.RaceState is RaceState.Retired or RaceState.Dnf or RaceState.Disqualified;
                bool outBefore = p.Race is RaceState.Retired or RaceState.Dnf or RaceState.Disqualified;
                if (outNow && !outBefore) Emit(new GpxSkyEvent(GpxSkyKind.OutOfRace, c, now));
                else if (!outNow && c.Index != focusIndex && c.PitState == PitState.DrivingIntoPits && p.Pit == PitState.None)
                    Emit(new GpxSkyEvent(GpxSkyKind.InPits, c, now));
            }
            _prev[c.Index] = (c.RaceState, c.PitState);
        }
        _observed = true;
        return created;
    }
}
