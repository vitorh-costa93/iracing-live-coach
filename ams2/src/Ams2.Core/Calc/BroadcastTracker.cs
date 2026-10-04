namespace Ams2.Core.Calc;

/// <summary>Vencedor da corrida: tempo total (soma das voltas do lider), distancia e media.</summary>
public sealed record WinnerInfo(CarSnapshot Car, double TotalSeconds, double DistanceKm, double AvgKmh, double FinishedT);

/// <summary>
/// Estado dos eventos de "transmissao" (paradas por piloto, cronometro de box do jogador, mudanca de posicao,
/// linha de chegada, bandeirada). Tempos em segundos do relogio do provider; widgets decidem a janela de exibicao
/// com <c>now - T</c>. <see cref="double.NegativeInfinity"/> = nunca aconteceu.
/// </summary>
public sealed record BroadcastState(
    IReadOnlyDictionary<int, int> Stops,
    int LastPitCarIndex,
    double LastPitEntryT,
    double SessionSeenT,
    double PlayerPositionChangedT,
    double PlayerLapChangedT,
    bool PlayerStopped,
    double PlayerStopStartT,
    double PlayerStopSeconds,
    double PlayerStopEndT,
    WinnerInfo? Winner,
    bool PlayerInPitLane = false,
    double PlayerPitLaneStartT = double.NegativeInfinity)
{
    public static readonly BroadcastState Empty = new(new Dictionary<int, int>(), -1, double.NegativeInfinity, double.NegativeInfinity,
        double.NegativeInfinity, double.NegativeInfinity, false, double.NegativeInfinity, 0, double.NegativeInfinity, null);

    public int StopsOf(int carIndex) => Stops.TryGetValue(carIndex, out var n) ? n : 0;
    /// <summary>Tempo parado do jogador: ao vivo enquanto parado, senao a ultima parada.</summary>
    public double PlayerStopNow(double now) => PlayerStopped ? Math.Max(0, now - PlayerStopStartT) : PlayerStopSeconds;
    /// <summary>Tempo do jogador na pit lane (entrada ate agora); 0 fora dela.</summary>
    public double PlayerPitLaneNow(double now) => PlayerInPitLane && !double.IsNegativeInfinity(PlayerPitLaneStartT) ? Math.Max(0, now - PlayerPitLaneStartT) : 0;
}

public sealed class BroadcastTracker
{
    readonly Dictionary<int, PitState> _pit = [];
    readonly Dictionary<int, int> _stops = [];
    readonly Dictionary<int, double> _lapSum = [];
    readonly Dictionary<int, int> _laps = [];
    SessionKind _kind;
    string _track = "";
    int _lastPitCar = -1, _playerPos, _playerLaps = -1, _leaderLaps;
    double _lastPitT = double.NegativeInfinity, _seenT = double.NegativeInfinity, _posT = double.NegativeInfinity, _lapT = double.NegativeInfinity;
    bool _stopped, _inLane;
    double _laneStart = double.NegativeInfinity;
    double _stopStart = double.NegativeInfinity, _stopSecs, _stopEnd = double.NegativeInfinity;
    WinnerInfo? _winner;
    BroadcastState _state = BroadcastState.Empty;

    public BroadcastState State => _state;

    public void Reset()
    {
        _pit.Clear(); _stops.Clear(); _lapSum.Clear(); _laps.Clear();
        _kind = SessionKind.Invalid; _track = "";
        _lastPitCar = -1; _playerPos = 0; _playerLaps = -1; _leaderLaps = 0;
        _lastPitT = _seenT = _posT = _lapT = _stopStart = _stopEnd = _laneStart = double.NegativeInfinity;
        _stopped = _inLane = false; _stopSecs = 0; _winner = null;
        _state = BroadcastState.Empty;
    }

    public BroadcastState Update(double now, SessionSnapshot s)
    {
        int leaderLaps = s.Cars.Count == 0 ? 0 : s.Cars.Max(c => c.LapsCompleted);
        if (s.Kind != _kind || s.Track != _track || leaderLaps < _leaderLaps) { Reset(); _kind = s.Kind; _track = s.Track; }
        _leaderLaps = leaderLaps;
        if (double.IsNegativeInfinity(_seenT)) _seenT = now;
        bool race = s.Kind == SessionKind.Race;

        foreach (var c in s.Cars)
        {
            // Parada = entrada na pit lane vindo da pista (nao conta saida da garagem nem largada nos boxes).
            if (_pit.TryGetValue(c.Index, out var prev) && prev == PitState.None && c.PitState is PitState.DrivingIntoPits or PitState.InPit && race && c.RaceState == RaceState.Racing)
            {
                _stops[c.Index] = _stops.GetValueOrDefault(c.Index) + 1;
                _lastPitCar = c.Index; _lastPitT = now;
            }
            _pit[c.Index] = c.PitState;

            // Soma dos tempos de volta (para o tempo total do vencedor).
            if (_laps.TryGetValue(c.Index, out var l) && c.LapsCompleted > l && c.LastLapTime > 0)
                _lapSum[c.Index] = _lapSum.GetValueOrDefault(c.Index) + c.LastLapTime;
            _laps[c.Index] = c.LapsCompleted;
        }

        var me = s.PlayerCar;
        if (me is not null)
        {
            if (_playerPos > 0 && me.Position != _playerPos) _posT = now;
            _playerPos = me.Position;
            if (_playerLaps >= 0 && me.LapsCompleted > _playerLaps) _lapT = now;
            _playerLaps = me.LapsCompleted;

            // Pit lane (entrada, box, saida): cronometro proprio para o "PIT 23.8" do 2018.
            if (me.InPitLane && !_inLane) _laneStart = now;
            _inLane = me.InPitLane;

            bool inBox = me.PitState == PitState.InPit;
            if (inBox && !_stopped) { _stopped = true; _stopStart = now; }
            if (_stopped)
            {
                _stopSecs = Math.Max(0, now - _stopStart);
                if (!inBox) { _stopped = false; _stopEnd = now; }
            }
        }

        if (_winner is null && race && s.Cars.FirstOrDefault(c => c.Position == 1) is { RaceState: RaceState.Finished } w)
        {
            double len = s.TrackLength, laps = s.LapsInEvent > 0 ? s.LapsInEvent : w.LapsCompleted;
            double total = _lapSum.GetValueOrDefault(w.Index);
            double km = laps * len / 1000;
            _winner = new WinnerInfo(w, total, km, total > 0 ? km / (total / 3600) : 0, now);
        }

        return _state = new BroadcastState(new Dictionary<int, int>(_stops), _lastPitCar, _lastPitT, _seenT, _posT, _lapT,
            _stopped, _stopStart, _stopSecs, _stopEnd, _winner, _inLane, _laneStart);
    }
}
