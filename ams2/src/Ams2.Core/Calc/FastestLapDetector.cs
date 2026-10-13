namespace Ams2.Core.Calc;

/// <summary>Nova volta mais rapida da sessao: o carro (foto do momento), o tempo e quando foi observada.</summary>
public sealed record FastestLapEvent(CarSnapshot Car, double LapTime, double At)
{
    /// <summary>true enquanto a janela [inicio, inicio + hold) cobre <paramref name="now"/>; <paramref name="notBefore"/> adia o inicio.</summary>
    public bool ActiveAt(double now, double hold = FastestLapDetector.DefaultHold, double notBefore = double.NegativeInfinity)
    {
        double start = Math.Max(At, notBefore);
        return now - start is >= 0 && now - start < hold;
    }
}

/// <summary>
/// Detecta quando a melhor volta da sessao (de qualquer carro, inclusive o jogador) e superada. O primeiro quadro observado so
/// fixa a referencia (nenhum evento antigo reaparece ao anexar no meio da sessao); mudanca de sessao, de jogador, relogio que
/// volta ou menos voltas do jogador reiniciam a referencia.
/// </summary>
public sealed class FastestLapDetector
{
    public const double DefaultHold = 7, Epsilon = .0005;
    bool _observed;
    double _best = double.PositiveInfinity, _lastNow = double.NegativeInfinity;
    string? _key;
    int _player = -1, _lastLaps = -1, _generation = -1;

    /// <summary>Ultimo evento detectado desde o ultimo reinicio (null = nenhum).</summary>
    public FastestLapEvent? Latest { get; private set; }

    public void Reset()
    {
        _observed = false; _best = double.PositiveInfinity; _lastNow = double.NegativeInfinity;
        _key = null; _player = _lastLaps = _generation = -1; Latest = null;
    }

    /// <summary>Processa um quadro; devolve o evento novo deste quadro ou null.</summary>
    public FastestLapEvent? Update(double now, SessionSnapshot session, int generation = 0)
    {
        var player = session.PlayerCar;
        string key = session.Track + "\n" + session.TrackVariation + "\n" + session.Kind;
        int playerIndex = player?.Index ?? -1, laps = player?.LapsCompleted ?? -1;
        if (_key != key || _generation != generation || _player != playerIndex || now < _lastNow || laps < _lastLaps)
        { Reset(); _key = key; _generation = generation; _player = playerIndex; }
        _lastNow = now; _lastLaps = laps;

        CarSnapshot? bestCar = null;
        foreach (var car in session.Cars)
            if (double.IsFinite(car.BestLapTime) && car.BestLapTime > 0 && (bestCar is null || car.BestLapTime < bestCar.BestLapTime)) bestCar = car;
        FastestLapEvent? created = null;
        if (bestCar is not null && bestCar.BestLapTime < _best - Epsilon)
        {
            if (_observed) Latest = created = new FastestLapEvent(bestCar, bestCar.BestLapTime, now);
            _best = bestCar.BestLapTime;
        }
        _observed = true;
        return created;
    }
}

/// <summary>Regras de gatilho das telas da placa 1993 (testaveis fora do widget).</summary>
public static class Broadcast93Rules
{
    public const int GapEveryLaps = 3, LapsToGoEvery = 5;
    public const double LapsToGoHold = 6, PitResultHold = 3;

    /// <summary>Voltas restantes do jogador em corrida por voltas (LapsInEvent - voltas completadas); null por tempo ou fora de corrida.</summary>
    public static int? LapsRemaining(SessionSnapshot s)
        => s.Kind == SessionKind.Race && s.LapsInEvent > 0 && s.PlayerCar is { } me ? Math.Max(0, s.LapsInEvent - me.LapsCompleted) : null;

    /// <summary>true a cada <paramref name="every"/> voltas restantes (&gt; 0).</summary>
    public static bool IsLapsToGo(int? remaining, int every = LapsToGoEvery) => remaining is > 0 && remaining % every == 0;

    /// <summary>true uma vez a cada <paramref name="every"/> voltas completadas (&gt; 0).</summary>
    public static bool IsGapLap(int lapsCompleted, int every = GapEveryLaps) => lapsCompleted > 0 && lapsCompleted % Math.Max(1, every) == 0;
}
