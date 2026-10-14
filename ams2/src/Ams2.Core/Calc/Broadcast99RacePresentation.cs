namespace Ams2.Core.Calc;

public enum Broadcast99RaceMode { None, Caption, Remaining, Gap, Table, Fastest, Pit }
public sealed record Broadcast99RaceRow(BoardDriver Driver, double? GapSeconds, int LapsBehind);
public sealed record Broadcast99RaceState(Broadcast99RaceMode Mode, int Lap, double StartedT,
    IReadOnlyList<Broadcast99RaceRow> Rows, BoardSectorGap? Gap, int? LapsRemaining,
    BoardDriver? Leader, FastestLapEvent? Fastest)
{
    public int PageSize { get; init; } = 8;
    public double PageHoldSeconds { get; init; } = 4;
    public double ShownSeconds { get; init; }
}

/// <summary>1999: scheduled events retain their display time while obscured. A lap-15 gap precedes remaining.
/// Cadence uses the player's current lap, never the leader or LastLapTime. Attach/resume only seeds a baseline.</summary>
public sealed class Broadcast99RacePresentation(BoardOptions options)
{
    public const double GapHold = 6, RemainingHold = 6;
    readonly FastestLapDetector _fastest = new();
    readonly List<Pending> _pending = [];
    sealed class Pending(Broadcast99RaceMode mode, int lap)
    {
        public readonly Broadcast99RaceMode Mode = mode;
        public readonly int Lap = lap;
        public double Shown;
        public IReadOnlyList<Broadcast99RaceRow> Rows = [];
        public BoardSectorGap? Gap;
        public int? Remaining;
        public BoardDriver? Leader;
    }
    int _lap = -1, _player = -1;
    string? _identity;
    double _lastNow = double.NegativeInfinity, _started;
    Pending? _showing;
    FastestLapEvent? _fast;
    double _fastShown;
    bool _showingFast;
    public Broadcast99RaceState? State { get; private set; }

    public void Reset()
    {
        _pending.Clear(); _fastest.Reset(); _lap = _player = -1; _identity = null;
        _lastNow = double.NegativeInfinity; _showing = null; _fast = null; _fastShown = 0; _showingFast = false; State = null;
    }

    public void Update(double now, SessionSnapshot s, GapTracker gaps, BoardSectorGap? completed, bool driving)
    {
        var me = s.PlayerCar;
        if (!driving || !s.InSession || s.GameState != 2 || me is null || me.InGarage)
        { Reset(); return; }
        if (s.Kind != SessionKind.Race)
        {
            Reset();
            State = new Broadcast99RaceState(Broadcast99RaceMode.Caption, me.CurrentLap, now, [], null, null, null, null);
            return;
        }
        int lap = me.CurrentLap > 0 ? me.CurrentLap : me.LapsCompleted + 1;
        string identity = $"{me.OriginalName}\0{me.Name}\0{me.CarName}\0{me.ClassName}";
        if (now < _lastNow || me.Index != _player || _identity != identity || lap < _lap || now - _lastNow > 2) Reset();
        double dt = double.IsFinite(_lastNow) ? Math.Max(0, now - _lastNow) : 0;
        _lastNow = now; _player = me.Index; _identity = identity;
        if (_showingFast) _fastShown += dt;
        else if (_showing is { } shown) shown.Shown += dt;
        if (_fastest.Update(now, s) is { } fastest) { _fast = fastest; _fastShown = 0; }
        if (_fastShown >= FastestLapDetector.DefaultHold) _fast = null;

        bool changed = _lap >= 0 && lap > _lap;
        _lap = lap;
        if (changed)
        {
            // A new lap replaces only the old table and an unresolved gap. Measured comparisons
            // and remaining survive a long pit interruption; lap 15 never silently loses remaining.
            _pending.RemoveAll(p => p.Mode == Broadcast99RaceMode.Table || p.Mode == Broadcast99RaceMode.Gap && p.Gap is null);
            _showing = null;
            var rows = s.Cars.Where(c => c.Position > 0 && !c.InGarage).OrderBy(c => c.Position).Select(c =>
            {
                var leader = s.Cars.Where(x => x.Position > 0 && !x.InGarage).MinBy(x => x.Position)!;
                double delta = leader.LapDistance - c.LapDistance;
                if (delta > s.TrackLength / 2) delta -= s.TrackLength;
                else if (delta < -s.TrackLength / 2) delta += s.TrackLength;
                int behind = Math.Max(0, (int)Math.Round((leader.TotalDistance(s.TrackLength) - c.TotalDistance(s.TrackLength) - delta) / s.TrackLength));
                return new Broadcast99RaceRow(BoardText.Driver(c, s.Cars), c.Index == leader.Index ? 0 : gaps.GapSeconds(now, c, leader, 1), behind);
            }).ToArray();
            _pending.Add(new Pending(Broadcast99RaceMode.Table, lap) { Rows = rows });
            if (lap % Math.Max(1, options.LapComparisonEvery) == 0) _pending.Add(new Pending(Broadcast99RaceMode.Gap, lap));
            if (lap % 5 == 0 && Broadcast93Rules.LapsRemaining(s) is { } remaining)
                _pending.Add(new Pending(Broadcast99RaceMode.Remaining, lap) { Remaining = remaining, Leader = rows.FirstOrDefault()?.Driver });
        }
        foreach (var p in _pending.Where(p => p.Mode == Broadcast99RaceMode.Gap && p.Gap is null && p.Lap == lap))
            if (completed is { Sector: 3, IsSplit: true, PreviousGapSeconds: not null } gap
                && gap.PlayerLapsCompleted == me.LapsCompleted
                && gap.Player.CarIndex == me.Index && gap.OpenT >= now - options.SectorMaxWindowSeconds
                && me.LapDistance < s.TrackLength / 3) p.Gap = gap;
        _pending.RemoveAll(p => p.Gap is { } frozen && (completed is null
            || completed.Player.CarIndex != frozen.Player.CarIndex || completed.Neighbor.CarIndex != frozen.Neighbor.CarIndex
            || completed.Player.Name != frozen.Player.Name || completed.Neighbor.Name != frozen.Neighbor.Name
            || completed.Player.Team != frozen.Player.Team || completed.Neighbor.Team != frozen.Neighbor.Team));

        _pending.RemoveAll(p => p.Shown >= Hold(p));
        bool stopped = me.PitState == PitState.InPit && me.SpeedMps < 1;
        Pending? next = stopped || _fast is not null ? null : _pending.OrderByDescending(p => p.Mode).ThenBy(p => p.Lap).FirstOrDefault();
        // Missing real crossings do not invent a gap or block lower priority data indefinitely.
        if (next is { Mode: Broadcast99RaceMode.Gap, Gap: null })
            next = _pending.Where(p => p.Mode != Broadcast99RaceMode.Gap).OrderByDescending(p => p.Mode).FirstOrDefault();
        bool fastMode = !stopped && _fast is not null;
        var mode = stopped ? Broadcast99RaceMode.Pit : fastMode ? Broadcast99RaceMode.Fastest : next?.Mode ?? Broadcast99RaceMode.Caption;
        if (State?.Mode != mode || next != _showing) _started = now;
        _showing = next; _showingFast = fastMode;
        State = new(mode, lap, _started, next?.Rows ?? [], next?.Gap, next?.Remaining, next?.Leader, _fast)
        { PageSize = Math.Max(1, options.PageSize), PageHoldSeconds = Math.Max(1, options.PageHoldSeconds), ShownSeconds = next?.Shown ?? 0 };
    }
    double Hold(Pending p) => p.Mode == Broadcast99RaceMode.Table
        ? Math.Max(1, options.PageHoldSeconds) * Math.Max(1, (p.Rows.Count + Math.Max(1, options.PageSize) - 1) / Math.Max(1, options.PageSize))
        : p.Mode == Broadcast99RaceMode.Gap ? GapHold : RemainingHold;
}
