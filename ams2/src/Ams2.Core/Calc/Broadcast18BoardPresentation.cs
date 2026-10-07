namespace Ams2.Core.Calc;

/// <summary>Presentation-only retention of the live sector comparison. Crossing results remain immediate.</summary>
public sealed class Broadcast18BoardPresentation
{
    readonly Broadcast18ValueHold _hold = new();
    public void Reset() => _hold.Reset();

    public BoardState Project(BoardState state, double now, object session, double seconds = 1)
    {
        var gap = state.Mode == BoardMode.SectorGap ? state.SectorGap : null;
        _hold.BeginFrame(now, (session, state.Mode, gap?.WindowStartedT ?? gap?.OpenT,
            gap?.Sector, gap?.Player.CarIndex, gap?.Neighbor.CarIndex, gap?.NeighborAhead));
        if (gap is null) return state;
        // A completed split is authoritative, never delayed behind the last live estimate.
        if (gap.IsSplit) { _hold.Reset(); return state; }
        var key = new Broadcast18ValueKey(gap.Player.CarIndex, gap.Neighbor.CarIndex, "sector",
            gap.Player.Position, 0, 0, PitState.None, RaceState.Racing, PitState.None, RaceState.Racing);
        var value = _hold.Sample(key, Math.Abs(gap.GapSeconds), now, seconds);
        if (value is null) return state;
        double displayed = Math.CopySign(value.Value, gap.GapSeconds);
        return state with { SectorGap = gap with { GapSeconds = displayed, GapText = BoardText.Gap(displayed) } };
    }
}
