using System.Globalization;

namespace IracingLiveCoach.Core.Telemetry;

/// <summary>One class's driver count as Kapps shows it in the Standings class header.</summary>
public readonly record struct ClassDriverCount(int Total, int WithTime)
{
    /// <summary>Kapps: "2/14" while only some of the class have a lap time, the plain total ("13") when
    /// nobody (or everybody) has one. Verified live 24/09/2026 (AI session, lone qualifying after
    /// practice): class headers "2/14", "13" and "3/13" = 2, 0 and 3 cars with a practice time out of
    /// 14/13/13 entrants.</summary>
    public string Text => WithTime > 0 && WithTime < Total
        ? WithTime.ToString(CultureInfo.InvariantCulture) + "/" + Total.ToString(CultureInfo.InvariantCulture)
        : Total.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// Per-class driver counts for the Standings/Relative headers: every entrant of the class (the
/// session's driver list, pace car and spectators excluded), not the whole field and not only the
/// cars currently in the world. Pure, so unit-tested.
/// </summary>
public static class ClassDriverCounts
{
    public static Dictionary<int, ClassDriverCount> Compute(IEnumerable<(int CarIdx, int ClassId)> entrants, IEnumerable<int> carsWithTime)
    {
        var timed = new HashSet<int>(carsWithTime);
        return entrants
            .GroupBy(e => e.CarIdx).Select(g => g.First())
            .GroupBy(e => e.ClassId)
            .ToDictionary(g => g.Key, g => new ClassDriverCount(g.Count(), g.Count(e => timed.Contains(e.CarIdx))));
    }
}
