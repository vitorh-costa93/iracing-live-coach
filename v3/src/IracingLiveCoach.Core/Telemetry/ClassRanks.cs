namespace IracingLiveCoach.Core.Telemetry;

/// <summary>One car class as published in the session's driver list.</summary>
public readonly record struct ClassSpeedInfo(int ClassId, int RelativeSpeed, double EstimatedLapTimeSeconds);

/// <summary>
/// Ranks the classes present in a session from fastest (1) to slowest, which drives the class colour
/// scheme (fastest = yellow, then light blue, pink, green). Uses iRacing's own class relative speed
/// (higher = faster); the estimated lap time (lower = faster) breaks ties. Pure, so unit-tested.
/// </summary>
public static class ClassRanks
{
    public static Dictionary<int, int> Compute(IEnumerable<ClassSpeedInfo> classes)
    {
        var distinct = classes
            .Where(c => c.ClassId > 0)
            .GroupBy(c => c.ClassId)
            .Select(g => g.First())
            .OrderByDescending(c => c.RelativeSpeed)
            .ThenBy(c => c.EstimatedLapTimeSeconds > 0 ? c.EstimatedLapTimeSeconds : double.MaxValue)
            .ThenBy(c => c.ClassId)
            .ToList();

        var ranks = new Dictionary<int, int>();
        for (int i = 0; i < distinct.Count; i++) ranks[distinct[i].ClassId] = i + 1;
        return ranks;
    }
}
