namespace IracingLiveCoach.Core.Telemetry;

/// <summary>One car of the race's official classification (SessionInfo Sessions[n].ResultsPositions).</summary>
/// <param name="ClassPosition">0-based, as iRacing publishes it.</param>
/// <param name="Time">Race: time behind the overall leader (0 for the leader).</param>
public readonly record struct FinalResultEntry(int CarIdx, int Position, int ClassPosition, double Time, double LastTime, int LapsComplete);

/// <summary>
/// After the chequered flag the cars that finish leave the world (CarIdxLapDistPct -1, CarIdxLapCompleted -1),
/// so the live on-track order collapses to the player alone. Kapps keeps the full field from iRacing's
/// official classification instead -- verified live at Watkins Glen 24/09/2026 (cool-down, SessionState 6):
/// its P4..P7 intervals 4.4 / 0.6 / 0.0 / 5.7 are the differences of ResultsPositions.Time truncated to one
/// decimal, and its last laps are ResultsPositions.LastTime. Pure so it is unit-tested.
/// </summary>
public static class FinalResults
{
    /// <summary>SessionState 5 = checkered, 6 = cool-down.</summary>
    public static bool Applies(bool isRace, int sessionState) => isRace && sessionState >= 5;

    public static (Dictionary<int, int> Overall, Dictionary<int, int> ByClass) Positions(IEnumerable<FinalResultEntry> results)
    {
        var overall = new Dictionary<int, int>();
        var byClass = new Dictionary<int, int>();
        foreach (var r in results)
        {
            if (r.Position <= 0 || overall.ContainsKey(r.CarIdx)) continue;
            overall[r.CarIdx] = r.Position;
            byClass[r.CarIdx] = r.ClassPosition + 1;
        }
        return (overall, byClass);
    }

    /// <summary>Race length in laps once it is over: the winner's completed laps.</summary>
    public static int? TotalLaps(IEnumerable<FinalResultEntry> results) =>
        results.Where(r => r.Position == 1).Select(r => (int?)r.LapsComplete).FirstOrDefault() is int laps && laps > 0 ? laps : null;
}
