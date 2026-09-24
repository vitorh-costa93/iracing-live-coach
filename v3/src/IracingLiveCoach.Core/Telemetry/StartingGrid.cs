namespace IracingLiveCoach.Core.Telemetry;

/// <summary>
/// Starting grid of a race, the reference for Kapps' "positions gained/lost" arrow (green ▲ n = gained,
/// red ▼ n = lost). Kapps' own print of the SF23 Interlagos race (player gridded P1, missed the start in
/// the pit lane, running P26) shows "▼ 25", i.e. start position - current position, pole included even
/// though the car left from the pits -- so the reference is the GRID, not the order at the first line.
///
/// Sources, first available wins (see TelemetryReader.RefreshStartingGrid):
///  1. the race session's own SessionInfo.Sessions[race].QualifyPositions (the grid iRacing lines up);
///  2. the weekend's QualifyResultsInfo.Results (qualifying classification);
///  3. CarIdxPosition sampled while the race had not gone green yet (SessionState &lt; Racing).
/// SessionInfo positions are 0-based in the YAML; <see cref="Normalize"/> accepts either base.
/// Positions are per CLASS (a multiclass grid is compared inside the class, like the Standings panels).
/// Pure and unit-tested.
/// </summary>
public static class StartingGrid
{
    /// <summary>CarIdx -> 1-based overall grid slot. 0-based input (any entry at 0) is shifted by one;
    /// negative entries and duplicates of a CarIdx are ignored.</summary>
    public static Dictionary<int, int> Normalize(IEnumerable<(int CarIdx, int Position)> entries)
    {
        var list = entries.Where(e => e.CarIdx >= 0 && e.Position >= 0).GroupBy(e => e.CarIdx).Select(g => g.First()).ToList();
        int shift = list.Any(e => e.Position == 0) ? 1 : 0;
        return list.ToDictionary(e => e.CarIdx, e => e.Position + shift);
    }

    /// <summary>CarIdx -> 1-based grid slot INSIDE its class, ranked by the overall grid.</summary>
    public static Dictionary<int, int> ByClass(IReadOnlyDictionary<int, int> overallGrid, Func<int, int> classOf) =>
        overallGrid.GroupBy(kv => classOf(kv.Key))
            .SelectMany(g => g.OrderBy(kv => kv.Value).Select((kv, i) => (kv.Key, Slot: i + 1)))
            .ToDictionary(x => x.Key, x => x.Slot);

    /// <summary>Positive = places gained since the start, negative = lost, 0 = same, null = unknown.</summary>
    public static int? Change(int? startPosition, int currentPosition) =>
        startPosition is int start && start > 0 && currentPosition > 0 ? start - currentPosition : null;
}
