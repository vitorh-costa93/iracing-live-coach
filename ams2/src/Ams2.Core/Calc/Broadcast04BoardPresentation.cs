namespace Ams2.Core.Calc;

/// <summary>2004 board priority and physical sector window, independent of rendering.</summary>
public static class Broadcast04BoardPresentation
{
    public static BoardState Project(BoardState state, double now)
    {
        if (state.Mode == BoardMode.LineTower && state.Tower is not null)
            return state;

        // The shared legacy gap can be distance-estimated; this presentation uses only
        // the pair selected at the previous physical sector reference.
        if (state.SectorGap98 is { } gap && gap.Sector is >= 1 and <= 3
            && double.IsFinite(gap.GapSeconds) && now >= gap.OpenT && now < gap.CloseT)
            return state with { Mode = BoardMode.SectorGap, SectorGap = gap,
                WindowStartT = gap.OpenT, WindowEndT = gap.CloseT,
                RemainingSeconds = gap.CloseT - now, ItemCount = 2 };

        var mode = state.LapComparison is not null ? BoardMode.LapComparison
            : state.Plate is not null ? BoardMode.DriverPlate : BoardMode.None;
        return state with { Mode = mode, SectorGap = null,
            WindowStartT = double.NegativeInfinity, WindowEndT = double.PositiveInfinity,
            RemainingSeconds = double.PositiveInfinity };
    }

    public static Broadcast04BoardKey Key(BoardState state) => state.Mode switch
    {
        BoardMode.LineTower when state.Tower is { } tower => new(state.Mode, tower.LeaderLap, tower.PageIndex, 0, 0),
        BoardMode.SectorGap when state.SectorGap is { } gap => new(state.Mode, gap.Sector, gap.Player.CarIndex, gap.Neighbor.CarIndex, gap.WindowStartedT ?? gap.OpenT),
        BoardMode.LapComparison when state.LapComparison is { } laps => new(state.Mode, laps.PlayerLapsCompleted, laps.Player.CarIndex, laps.Neighbor.CarIndex, 0),
        BoardMode.DriverPlate when state.Plate is { } plate => new(state.Mode, plate.CarIndex, 0, 0, 0),
        _ => new(BoardMode.None, 0, 0, 0, 0),
    };
}

public readonly record struct Broadcast04BoardKey(BoardMode Mode, int A, int B, int C, double OpenT);
public sealed record Broadcast04BoardFrame(BoardState Current, float Entry, BoardState? Outgoing, float Exit);

/// <summary>Sequential removal then reveal. Durations are visual approximations, not measured footage.</summary>
public sealed class Broadcast04BoardMotion
{
    public const double ExitSeconds = .12, EntrySeconds = .16;
    Broadcast04BoardKey? _key;
    BoardState? _visible, _outgoing;
    double _lastNow = double.NegativeInfinity, _start;
    bool _settled;
    float _outgoingAlpha, _visibleAlpha, _lastExit;

    public void Reset()
    {
        _key = null;
        _visible = _outgoing = null;
        _lastNow = double.NegativeInfinity;
        _settled = false;
        _outgoingAlpha = _visibleAlpha = _lastExit = 0;
    }

    public Broadcast04BoardFrame Advance(BoardState current, double now)
    {
        if (!double.IsFinite(now)) { Reset(); return new(current, 0, null, 0); }
        if (now < _lastNow) Reset();
        var key = Broadcast04BoardPresentation.Key(current);
        if (_key is null) { _key = key; _settled = true; }
        else if (_key != key)
        {
            // During an interrupted exit, retain only the already visible outgoing plate.
            if (_visible is not null) { _outgoing = _visible; _outgoingAlpha = _visibleAlpha; }
            else if (_outgoing is not null) _outgoingAlpha = _lastExit;
            _visible = null;
            _key = key;
            _start = now;
            _settled = false;
        }
        double age = Math.Max(0, now - _start);
        double delay = _outgoing is not null ? ExitSeconds : 0;
        float exit = _outgoing is null ? 0 : _outgoingAlpha * Clamp(1 - age / ExitSeconds);
        float entry = _settled ? 1 : Clamp((age - delay) / EntrySeconds);
        var outgoing = exit > 0 ? _outgoing : null;
        if (entry > 0 && current.Mode != BoardMode.None) { _visible = current; _visibleAlpha = entry; }
        if (age >= delay + EntrySeconds) { _outgoing = null; _settled = true; }
        _lastNow = now;
        _lastExit = exit;
        return new(current, entry, outgoing, exit);
    }

    static float Clamp(double value) => (float)Math.Clamp(value, 0, 1);
}
