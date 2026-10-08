namespace Ams2.Core.Calc;

/// <summary>Contrato isolado de 1998: referência física no marcador anterior, sem gaps estimados.</summary>
internal sealed class BoardSector98Tracker(BoardOptions options)
{
    readonly Dictionary<int, Sample> _previous = new();
    Pair? _reference, _window;
    string? _playerIdentity;
    int _playerIndex = -1;
    double _lastNow = double.NegativeInfinity;
    public BoardSectorGap? State { get; private set; }

    readonly record struct Sample(CarSnapshot Car, double T);
    readonly record struct Crossing(double T, double D0, double D1, double T0, double T1)
    {
        public double At(double mark) => double.IsFinite(mark) && D1 > D0 && D1 - D0 <= 500 && T1 > T0
            ? T0 + Math.Clamp((mark - D0) / (D1 - D0), 0, 1) * (T1 - T0) : T;
    }

    sealed class Pair(int marker, CarSnapshot player, CarSnapshot neighbor, bool ahead)
    {
        public readonly int Marker = marker;
        public readonly int Player = player.Index, Neighbor = neighbor.Index;
        public readonly string PlayerIdentity = Identity(player), NeighborIdentity = Identity(neighbor);
        public readonly bool Ahead = ahead;
        public Crossing? First, Second;
        public double OpenT, StartedT, Gap, CloseT;
        public bool Invalid;
    }

    public void Reset()
    {
        _previous.Clear(); _reference = _window = null; State = null;
        _playerIndex = -1; _playerIdentity = null; _lastNow = double.NegativeInfinity;
    }

    public void Update(double now, SessionSnapshot s, IEnumerable<(int Car, int Marker, double T)> events,
        Func<int, double> boundary)
    {
        var me = s.PlayerCar;
        if (me is null || !Eligible(me)) { Reset(); return; }
        if (me.Index != _playerIndex || Identity(me) != _playerIdentity) Reset();
        if (now <= _lastNow) return; // repeated timestamps cannot produce a new crossing
        _playerIndex = me.Index; _playerIdentity = Identity(me); _lastNow = now;

        bool Valid(Pair? p)
        {
            if (p is null || p.Invalid || Identity(me) != p.PlayerIdentity || !Continuous(me, now, s.TrackLength)) return false;
            var nb = s.Cars.FirstOrDefault(c => c.Index == p.Neighbor);
            if (nb is null || Identity(nb) != p.NeighborIdentity || !Continuous(nb, now, s.TrackLength)) return false;
            double signed = SignedDistance(me.TotalDistance(s.TrackLength), nb.TotalDistance(s.TrackLength), s.TrackLength);
            return p.Second is not null || Math.Abs(signed) < .5 || (signed >= 0) == p.Ahead;
        }
        if (!Valid(_reference)) _reference = null;
        if (!Valid(_window) || now >= _window!.CloseT) { _window = null; State = null; }

        int firstCar = _reference is { } current ? (current.Ahead ? current.Neighbor : current.Player) : -1;
        foreach (var e in events.OrderBy(e => e.T).ThenBy(e => e.Car == firstCar ? 0 : 1).ThenBy(e => e.Car))
        {
            var car = s.Cars.FirstOrDefault(c => c.Index == e.Car);
            if (car is null || !Continuous(car, now, s.TrackLength)) continue;
            if (_window is { } w) Cross(w, car, e.Marker, e.T, now, s.TrackLength, boundary);
            if (_reference is { } r && !ReferenceEquals(r, _window))
                Cross(r, car, e.Marker, e.T, now, s.TrackLength, boundary);

            if (car.Index == me.Index)
            {
                // The next choice is independent of the window still waiting for its second car.
                var neighbor = Nearest(s, me, e.T, now);
                _reference = neighbor is null ? null : new Pair(e.Marker % 3 + 1, me, neighbor,
                    SignedDistance(PositionAt(me, e.T, now, s.TrackLength), PositionAt(neighbor, e.T, now, s.TrackLength), s.TrackLength) >= 0);
            }
        }

        if (_window is { Invalid: false } active && now < active.CloseT)
        {
            var nb = s.Cars.First(c => c.Index == active.Neighbor);
            bool split = active.Second is not null;
            double gap = split ? active.Gap : (active.Ahead ? 1 : -1) * Math.Max(0, now - active.OpenT);
            State = new BoardSectorGap(active.Marker, BoardText.Driver(me, s.Cars), BoardText.Driver(nb, s.Cars),
                active.Ahead, gap, split, BoardText.Gap(gap), active.OpenT, active.CloseT) { WindowStartedT = active.StartedT };
        }
        else { _window = null; State = null; }

        _previous.Clear();
        foreach (var c in s.Cars) _previous[c.Index] = new Sample(c, now);
    }

    void Cross(Pair pair, CarSnapshot car, int marker, double t, double now, double len, Func<int, double> boundary)
    {
        if (pair.Invalid || pair.Second is not null || marker != pair.Marker || (car.Index != pair.Player && car.Index != pair.Neighbor)) return;
        int first = pair.Ahead ? pair.Neighbor : pair.Player;
        var previous = _previous[car.Index];
        double d0 = previous.Car.LapDistance, d1 = car.LapDistance;
        if (marker == 3 && d1 < d0) d1 += len;
        var crossing = new Crossing(t, d0, d1, previous.T, now);
        if (pair.First is null)
        {
            // Overtaking invalidates this reference; never substitute another car mid-comparison.
            if (car.Index != first) { pair.Invalid = true; return; }
            pair.First = crossing; pair.StartedT = pair.OpenT = t; pair.CloseT = t + options.SectorMaxWindowSeconds;
            // A real crossing at the next mark starts the newer window even if the older pair is incomplete.
            _window = pair;
        }
        else if (car.Index != first)
        {
            pair.Second = crossing;
            double mark = boundary(marker);
            pair.OpenT = pair.First.Value.At(mark);
            double secondT = crossing.At(mark);
            pair.Gap = (pair.Ahead ? 1 : -1) * Math.Max(0, secondT - pair.OpenT);
            pair.CloseT = Math.Min(secondT + options.SectorCloseDelaySeconds, pair.OpenT + options.SectorMaxWindowSeconds);
        }
    }

    CarSnapshot? Nearest(SessionSnapshot s, CarSnapshot me, double t, double now)
    {
        CarSnapshot? best = null;
        double distance = double.PositiveInfinity, bestSigned = double.NegativeInfinity;
        double mine = PositionAt(me, t, now, s.TrackLength);
        foreach (var c in s.Cars)
        {
            if (c.Index == me.Index || !Continuous(c, now, s.TrackLength)) continue;
            double signed = SignedDistance(mine, PositionAt(c, t, now, s.TrackLength), s.TrackLength);
            double abs = Math.Abs(signed);
            if (abs < distance || (abs == distance && (signed > bestSigned || signed == bestSigned && c.Index < best!.Index)))
            { best = c; distance = abs; bestSigned = signed; }
        }
        return best;
    }

    bool Continuous(CarSnapshot c, double now, double len)
    {
        if (!Eligible(c) || !_previous.TryGetValue(c.Index, out var p) || !Eligible(p.Car) || Identity(c) != Identity(p.Car) || now <= p.T
            || now - p.T > 2) return false;   // pausa/menu/travada: amostra velha geraria cruzamento interpolado falso
        double delta = c.TotalDistance(len) - p.Car.TotalDistance(len);
        return delta >= -0.5 && delta <= 500 && (c.Sector == p.Car.Sector || c.Sector == (p.Car.Sector + 1) % 3);
    }

    double PositionAt(CarSnapshot c, double t, double now, double len)
    {
        var p = _previous[c.Index];
        double d0 = p.Car.TotalDistance(len), d1 = c.TotalDistance(len);
        return d0 + (d1 - d0) * Math.Clamp((t - p.T) / (now - p.T), 0, 1);
    }

    static double SignedDistance(double me, double other, double len)
    {
        double d = (other - me) % len;
        if (d > len / 2) d -= len; else if (d < -len / 2) d += len;
        return d;
    }

    static bool Eligible(CarSnapshot c) => !c.InGarage && c.PitState != PitState.InPit
        && c.RaceState is not (RaceState.Retired or RaceState.Dnf or RaceState.Disqualified);
    static string Identity(CarSnapshot c) => $"{c.OriginalName}\0{c.Name}\0{c.CarName}\0{c.ClassName}";
}
