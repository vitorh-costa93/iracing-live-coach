namespace Ams2.Core.Calc;

/// <summary>
/// Instante (relógio do provider) do último evento de cada tipo da Exibição (ids em Ams2.Shared.Profiles.ShowEvents). Infinito negativo = ainda não houve.
/// Os widgets com gatilho "por evento" aparecem por N s a partir destes instantes (ShowPlan.GateAlpha).
/// </summary>
public sealed record ShowEventTimes(
    double LeaderLap, double PlayerLap, double BestLap, double Position, double Pit, double PlayerPit,
    double Flag, double Start, double Sector, double Finish, double SessionEnd)
{
    const double Never = double.NegativeInfinity;

    /// <summary>Voltas completas do líder e do jogador (gatilho "a cada N voltas"; vale desde o último evento de volta correspondente).</summary>
    public int LeaderLaps { get; init; }
    public int PlayerLaps { get; init; }

    /// <summary>Voltas completas de <paramref name="of"/> ("leader" ou "player").</summary>
    public int Laps(string of) => string.Equals(of, "leader", StringComparison.OrdinalIgnoreCase) ? LeaderLaps : PlayerLaps;
    public static readonly ShowEventTimes Empty = new(Never, Never, Never, Never, Never, Never, Never, Never, Never, Never, Never);

    /// <summary>Instante do evento <paramref name="id"/>; null = nunca houve ou id desconhecido.</summary>
    public double? Last(string id)
    {
        double t = id switch
        {
            "leaderLap" => LeaderLap, "playerLap" => PlayerLap, "bestLap" => BestLap, "position" => Position, "pit" => Pit,
            "playerPit" => PlayerPit, "flag" => Flag, "start" => Start, "sector" => Sector, "finish" => Finish, "sessionEnd" => SessionEnd,
            _ => Never,
        };
        return double.IsFinite(t) ? t : null;
    }
}

/// <summary>
/// Junta os eventos que já existem nos outros trackers (BroadcastTracker, QualiLapTracker, QualiEndTracker) e detecta os que faltavam:
/// líder fecha a volta, nova volta mais rápida da sessão, bandeira (amarela, azul, vermelha, xadrez) e largada (NotStarted -> Racing na corrida).
/// A primeira leitura de uma sessão só registra o estado (não é evento).
/// </summary>
public sealed class ShowEventTracker
{
    /// <summary>FlagColour do SharedMemory.h que disparam "flag": 2 azul, 5 vermelha, 6 amarela, 7 dupla amarela, 11 xadrez.</summary>
    public static bool IsEventFlag(uint flag) => flag is 2 or 5 or 6 or 7 or 11;

    bool _primed;
    SessionKind _kind;
    string _track = "";
    int _leaderLaps;
    double _best;
    uint _flag;
    RaceState _playerRace;
    double _leaderLapT = double.NegativeInfinity, _bestT = double.NegativeInfinity, _flagT = double.NegativeInfinity, _startT = double.NegativeInfinity;

    public void Reset()
    {
        _primed = false;
        _leaderLapT = _bestT = _flagT = _startT = double.NegativeInfinity;
    }

    public ShowEventTimes Update(double now, SessionSnapshot s, BroadcastState? bc = null, QualiLapState? lap = null, QualiEndState? end = null)
    {
        if (_primed && (s.Kind != _kind || !string.Equals(s.Track, _track, StringComparison.Ordinal))) Reset();
        var leader = s.Cars.FirstOrDefault(c => c.Position == 1);
        int leaderLaps = leader?.LapsCompleted ?? 0;
        double best = 0;
        foreach (var c in s.Cars) if (c.BestLapTime > 0 && (best <= 0 || c.BestLapTime < best)) best = c.BestLapTime;
        var playerRace = s.PlayerCar?.RaceState ?? RaceState.Invalid;
        if (_primed)
        {
            if (leader is not null && leaderLaps > _leaderLaps) _leaderLapT = now;
            if (best > 0 && (_best <= 0 || best < _best - 1e-4)) _bestT = now;
            if (s.FlagColour != _flag && IsEventFlag(s.FlagColour)) _flagT = now;
            if (s.Kind == SessionKind.Race && _playerRace == RaceState.NotStarted && playerRace == RaceState.Racing) _startT = now;
        }
        _primed = true; _kind = s.Kind; _track = s.Track;
        _leaderLaps = leaderLaps; _flag = s.FlagColour; _playerRace = playerRace;
        if (best > 0) _best = best;

        bc ??= BroadcastState.Empty;
        double sector = Math.Max(lap?.LastSplit?.At ?? double.NegativeInfinity, lap?.LastResult?.At ?? double.NegativeInfinity);
        return new ShowEventTimes(
            _leaderLapT, bc.PlayerLapChangedT, _bestT, bc.PlayerPositionChangedT, bc.LastPitEntryT,
            Math.Max(bc.PlayerStopStartT, bc.PlayerPitLaneStartT), _flagT, _startT, sector,
            bc.Winner?.FinishedT ?? double.NegativeInfinity, end?.EndedAt ?? double.NegativeInfinity)
        { LeaderLaps = leaderLaps, PlayerLaps = s.PlayerCar?.LapsCompleted ?? 0 };
    }
}
