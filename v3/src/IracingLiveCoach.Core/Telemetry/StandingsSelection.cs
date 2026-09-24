namespace IracingLiveCoach.Core.Telemetry;

/// <summary>
/// Pure selection policy for the Standings presentation. The SDK remains the source of truth for
/// positions; this class merely selects which already-ranked rows are visible. It deliberately
/// keeps Top N and the player window separate, then de-duplicates by car/driver identity.
/// </summary>
/// Defaults = Kapps (live 24/09/2026): the class's top 2 PLUS a 5-row window around the player (player P6 ->
/// P1 P2 P4..P8; player P14 of 14 -> P1 P2 P10..P14) and 1 row for every other class.
public sealed record StandingsPresentationOptions(
    int TopNPerClass = 2,
    int OwnClassRows = 5,
    int OtherClassRows = 1,
    bool KeepPlayerWindow = true,
    bool TopNCountsTowardTotal = false)
{
    public static StandingsPresentationOptions Default { get; } = new();
}

public sealed record StandingsClassGroup(int ClassId, string ClassShortName, string? ClassColorHex, IReadOnlyList<StandingsRow> Rows, int ClassRank = 0);

public static class StandingsSelection
{
    public static IReadOnlyList<StandingsClassGroup> GroupAndSelect(
        IEnumerable<StandingsRow> source,
        StandingsPresentationOptions? options = null)
    {
        var settings = options ?? StandingsPresentationOptions.Default;
        // Single-class sessions (AI races, many official series) report CarClassID 0 for every car --
        // that is a real class, not a missing one. The pace car is already excluded by the reader.
        var rows = source.OrderBy(r => r.Position).ToList();
        var playerClassId = rows.FirstOrDefault(r => r.IsPlayer)?.CarClassId;
        bool hasPlayer = playerClassId is not null;

        return rows
            .GroupBy(r => r.CarClassId)
            .Select(group =>
            {
                var ranked = group.OrderBy(r => r.ClassPosition > 0 ? r.ClassPosition : r.Position).ToList();
                bool isPlayerClass = hasPlayer && group.Key == playerClassId;
                int topN = Math.Max(0, settings.TopNPerClass);
                // "Top N incluído no total" (spec §12): when off, the Top N rows are ADDED to the class budget.
                int budget = (isPlayerClass ? Math.Max(1, settings.OwnClassRows) : Math.Max(0, settings.OtherClassRows))
                             + (settings.TopNCountsTowardTotal ? 0 : topN);
                var picked = new List<StandingsRow>();

                // The Top-N requirement is class-local, not the table index/global grid rank.
                picked.AddRange(ranked.Take(Math.Min(topN, budget)));

                if (isPlayerClass && settings.KeepPlayerWindow)
                {
                    int playerIndex = ranked.FindIndex(r => r.IsPlayer);
                    if (playerIndex >= 0)
                    {
                        int remaining = Math.Max(1, budget - picked.Count);
                        int start = Math.Max(0, playerIndex - remaining / 2);
                        start = Math.Min(start, Math.Max(0, ranked.Count - remaining));
                        picked.AddRange(ranked.Skip(start).Take(remaining));
                    }
                }

                // Fill only the remaining class budget in true class rank order.
                foreach (var row in ranked)
                {
                    if (picked.Distinct().Count() >= budget) break;
                    if (!picked.Contains(row)) picked.Add(row);
                }

                var visible = picked.Distinct().OrderBy(r => r.ClassPosition > 0 ? r.ClassPosition : r.Position).ToList();
                var first = ranked.FirstOrDefault();
                return new StandingsClassGroup(group.Key, first?.ClassShortName ?? "", first?.ClassColorHex, visible, first?.ClassRank ?? 0);
            })
            .Where(group => group.Rows.Count > 0)
            // Kapps keeps the class panels in a FIXED order, fastest class first (ClassRank 1 = fastest,
            // from CarClassRelSpeed/CarClassEstLapTime) -- never by who leads overall or by the player's
            // class. Unranked classes (single-class sessions report rank 0) keep their running order.
            .Select((group, index) => (group, index))
            .OrderBy(t => t.group.ClassRank > 0 ? t.group.ClassRank : int.MaxValue)
            .ThenBy(t => t.index)
            .Select(t => t.group)
            .ToList();
    }
}
