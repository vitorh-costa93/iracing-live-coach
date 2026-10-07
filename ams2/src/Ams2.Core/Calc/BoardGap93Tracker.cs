using System.Globalization;

namespace Ams2.Core.Calc;

/// <summary>Resultado fechado de duas passagens pelo mesmo ponto; nunca um contador ou estimativa de velocidade.</summary>
public sealed record BoardGap93(BoardDriver Player, BoardDriver Neighbor, bool NeighborAhead,
    double GapSeconds, string GapText, double FirstCrossedT, double CompletedT, double CloseT)
{
    public BoardDriver Ahead => NeighborAhead ? Neighbor : Player;
    public BoardDriver Behind => NeighborAhead ? Player : Neighbor;
}

/// <summary>1993: escolhe o vizinho físico na primeira passagem do par e mede um único ponto por volta.</summary>
public sealed class BoardGap93Tracker
{
    sealed record Settings(double PointPercent, double HoldSeconds, bool Enabled);
    readonly record struct Sample(CarSnapshot Car, double T);
    readonly record struct Crossing(int Car, double T, int Cycle);
    sealed record Pair(CarSnapshot Player, CarSnapshot Neighbor, bool Ahead, double FirstT, int Cycle);

    readonly Dictionary<int, Sample> _previous = new();
    readonly List<Crossing> _crossings = new();
    Settings _settings, _active;
    Pair? _pair, _heldPair;
    int _lastAttempt = int.MinValue, _tieNeighbor = -1, _playerIndex = -1, _maxLaps;
    string _playerIdentity = "", _track = "";
    double _length, _lastNow = double.NegativeInfinity;
    uint _sequence;
    double? _remaining;

    public BoardGap93Tracker(double pointPercent = 0, double holdSeconds = 7, bool enabled = true)
    {
        _settings = _active = Normalize(pointPercent, holdSeconds, enabled);
    }

    public BoardGap93? State { get; private set; }

    /// <summary>Percentual da pista (0 = chegada), retenção em segundos. Aplicado no próximo quadro, zerando a medição.</summary>
    public void SetOptions(double pointPercent, double holdSeconds, bool enabled = true) =>
        Volatile.Write(ref _settings, Normalize(pointPercent, holdSeconds, enabled));

    static Settings Normalize(double pointPercent, double holdSeconds, bool enabled) => new(
        double.IsFinite(pointPercent) ? Math.Clamp(pointPercent, 0, 99.9) : 0,
        double.IsFinite(holdSeconds) ? Math.Clamp(holdSeconds, 3, 15) : 7, enabled);

    public void Reset()
    {
        _previous.Clear(); _crossings.Clear(); _pair = _heldPair = null; State = null;
        _lastAttempt = int.MinValue; _tieNeighbor = _playerIndex = -1; _playerIdentity = _track = "";
        _length = 0; _maxLaps = 0; _lastNow = double.NegativeInfinity; _sequence = 0; _remaining = null;
    }

    public BoardGap93? Update(double now, SessionSnapshot s, bool playerDriving = true)
    {
        var settings = Volatile.Read(ref _settings);
        if (settings != _active) { Reset(); _active = settings; }
        if (!settings.Enabled) return null;
        var me = s.PlayerCar;
        if (!double.IsFinite(now) || !s.InSession || s.GameState != 2 || s.Kind != SessionKind.Race ||
            !playerDriving || me is null || !Eligible(me) || !double.IsFinite(s.TrackLength) || s.TrackLength <= 0)
        { Reset(); return null; }
        int laps = s.Cars.Select(c => c.LapsCompleted).DefaultIfEmpty().Max();
        if (_playerIndex != me.Index || _playerIdentity != Identity(me) || _track != s.Track + "\0" + s.TrackVariation ||
            Math.Abs(_length - s.TrackLength) > .5 || laps < _maxLaps || now < _lastNow ||
            s.TimeRemainingSeconds is { } remaining && _remaining is { } previousRemaining && remaining > previousRemaining + 5 ||
            _previous.TryGetValue(me.Index, out var prior) && prior.Car.RaceState == RaceState.Racing && me.RaceState == RaceState.NotStarted)
            Reset();
        _playerIndex = me.Index; _playerIdentity = Identity(me); _track = s.Track + "\0" + s.TrackVariation;
        _length = s.TrackLength; _maxLaps = laps;
        _remaining = s.TimeRemainingSeconds;
        if (State is { } result && now >= result.CloseT) State = null;
        if (now <= _lastNow || (s.Sequence != 0 && s.Sequence == _sequence)) return State;
        _lastNow = now; _sequence = s.Sequence;

        if (_pair is { } pair && !ValidPair(pair, me, s, now)) { _pair = null; State = null; }
        // A completed result keeps its original drivers/positions throughout the hold.
        if (State is not null && _heldPair is { } held &&
            !ValidPair(held, me, s, now)) State = null;

        _crossings.Clear();
        double marker = _active.PointPercent / 100 * _length;
        foreach (var c in s.Cars)
        {
            if (!Continuous(c, now)) continue;
            var p = _previous[c.Index];
            double d0 = p.Car.TotalDistance(_length), d1 = c.TotalDistance(_length);
            int cycle = (int)Math.Floor((d0 - marker) / _length) + 1;
            double at = cycle * _length + marker;
            if (d1 >= at && d1 > d0)
                _crossings.Add(new Crossing(c.Index, p.T + (at - d0) / (d1 - d0) * (now - p.T), cycle));
        }
        _crossings.Sort((a, b) => a.T != b.T ? a.T.CompareTo(b.T) :
            (a.Car == me.Index ? 1 : 0).CompareTo(b.Car == me.Index ? 1 : 0));
        foreach (var cross in _crossings)
        {
            if (_pair is { } pending)
            {
                int second = pending.Ahead ? pending.Player.Index : pending.Neighbor.Index;
                if (cross.Car == second && cross.T >= pending.FirstT)
                {
                    var nb = s.Cars.First(c => c.Index == pending.Neighbor.Index);
                    double gap = cross.T - pending.FirstT;
                    State = new BoardGap93(BoardText.Driver(me, s.Cars), BoardText.Driver(nb, s.Cars), pending.Ahead,
                        gap, gap.ToString("0.000", CultureInfo.InvariantCulture), pending.FirstT, cross.T,
                        cross.T + _active.HoldSeconds);
                    _heldPair = pending; _pair = null;
                }
                continue;
            }
            if (!Continuous(me, now)) continue;
            var neighbor = Nearest(s, me, cross.T, now);
            if (neighbor is null) continue;
            double mine = PositionAt(me, cross.T, now);
            bool ahead = SignedDistance(mine, PositionAt(neighbor, cross.T, now)) >= 0;
            if (cross.Car != (ahead ? neighbor.Index : me.Index)) continue;
            int playerCycle = ahead ? (int)Math.Floor((mine - marker - 1e-7) / _length) + 1 : cross.Cycle;
            if (playerCycle <= _lastAttempt) continue;
            _lastAttempt = playerCycle; _tieNeighbor = neighbor.Index;
            _pair = new Pair(me, neighbor, ahead, cross.T, playerCycle);
        }
        _previous.Clear();
        foreach (var c in s.Cars) _previous[c.Index] = new Sample(c, now);
        return State;
    }

    bool ValidPair(Pair pair, CarSnapshot me, SessionSnapshot s, double now)
    {
        var neighbor = s.Cars.FirstOrDefault(c => c.Index == pair.Neighbor.Index);
        if (neighbor is null || Identity(me) != Identity(pair.Player) || Identity(neighbor) != Identity(pair.Neighbor) ||
            !Continuous(me, now) || !Continuous(neighbor, now) || now - pair.FirstT > 120) return false;
        double signed = SignedDistance(me.TotalDistance(_length), neighbor.TotalDistance(_length));
        return Math.Abs(signed) < .001 || (signed >= 0) == pair.Ahead;
    }

    CarSnapshot? Nearest(SessionSnapshot s, CarSnapshot me, double t, double now)
    {
        CarSnapshot? best = null;
        double distance = double.PositiveInfinity, bestSigned = double.NegativeInfinity;
        double mine = PositionAt(me, t, now);
        foreach (var c in s.Cars)
        {
            if (c.Index == me.Index || !Continuous(c, now)) continue;
            double signed = SignedDistance(mine, PositionAt(c, t, now));
            double abs = Math.Abs(signed);
            bool tie = Math.Abs(abs - distance) < .001;
            if (abs < distance - .001 || (tie && (c.Index == _tieNeighbor ||
                best?.Index != _tieNeighbor && (signed > bestSigned || signed == bestSigned && c.Index < best!.Index))))
            { best = c; distance = abs; bestSigned = signed; }
        }
        return best;
    }

    bool Continuous(CarSnapshot c, double now)
    {
        if (!Eligible(c) || c.LapDistance < 0 || c.LapDistance >= _length ||
            !_previous.TryGetValue(c.Index, out var p) || !Eligible(p.Car) || p.Car.LapDistance < 0 || p.Car.LapDistance >= _length ||
            Identity(c) != Identity(p.Car) || now <= p.T || now - p.T > 2) return false;
        double delta = c.TotalDistance(_length) - p.Car.TotalDistance(_length);
        return delta >= 0 && delta <= 500;
    }

    double PositionAt(CarSnapshot c, double t, double now)
    {
        var p = _previous[c.Index];
        return p.Car.TotalDistance(_length) + (c.TotalDistance(_length) - p.Car.TotalDistance(_length)) *
            Math.Clamp((t - p.T) / (now - p.T), 0, 1);
    }

    double SignedDistance(double me, double other)
    {
        double d = (other - me) % _length;
        if (d > _length / 2) d -= _length; else if (d < -_length / 2) d += _length;
        return d;
    }

    static bool Eligible(CarSnapshot c) => !c.InPitLane && !c.InGarage &&
        double.IsFinite(c.LapDistance) && c.LapsCompleted >= 0 &&
        c.RaceState is not (RaceState.Invalid or RaceState.Retired or RaceState.Dnf or RaceState.Disqualified);
    static string Identity(CarSnapshot c) => $"{c.OriginalName}\0{c.Name}\0{c.CarName}\0{c.ClassName}";
}
