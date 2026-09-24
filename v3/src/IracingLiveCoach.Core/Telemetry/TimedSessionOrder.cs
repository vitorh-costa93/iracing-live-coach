namespace IracingLiveCoach.Core.Telemetry;

/// <summary>One car as iRacing classifies it in a practice/qualifying session.</summary>
/// <param name="Position">CarIdxPosition: 1.. by best lap; 0 = no timed lap yet.</param>
/// <param name="ClassPosition">CarIdxClassPosition, same convention inside the class.</param>
/// <param name="InWorld">CarIdxLapDistPct >= 0 (in the car right now, on track or in the pits).</param>
public readonly record struct TimedCar(int CarIdx, int ClassId, int Position, int ClassPosition, bool InWorld);

/// <summary>
/// Standings order for practice / qualifying (and test drive): by BEST LAP, like Kapps and iRacing's own
/// timing screen -- not the live order on track, which is meaningless outside a race. iRacing already
/// ranks by best lap in CarIdxPosition/CarIdxClassPosition (verified live, Watkins Glen GT3 official
/// practice 24/09/2026: CarIdxF2Time == each car's ResultsPositions FastestTime, ascending with
/// CarIdxPosition). Timed cars keep that order -- including drivers who already left the session
/// (iRacing keeps their time in the classification); cars in the world without a time follow, by
/// CarIdx. Returns CarIdx -> (overall, class) 1-based positions. Pure and unit-tested.
/// </summary>
public static class TimedSessionOrder
{
    public static (Dictionary<int, int> Overall, Dictionary<int, int> ByClass) Compute(IEnumerable<TimedCar> cars)
    {
        var included = cars.Where(c => c.Position > 0 || c.InWorld).ToList();
        var overall = included
            .OrderBy(c => c.Position > 0 ? c.Position : int.MaxValue).ThenBy(c => c.CarIdx)
            .Select((c, i) => (c.CarIdx, Pos: i + 1))
            .ToDictionary(x => x.CarIdx, x => x.Pos);
        var byClass = included.GroupBy(c => c.ClassId)
            .SelectMany(g => g
                .OrderBy(c => c.ClassPosition > 0 ? c.ClassPosition : c.Position > 0 ? c.Position : int.MaxValue)
                .ThenBy(c => overall[c.CarIdx])
                .Select((c, i) => (c.CarIdx, Pos: i + 1)))
            .ToDictionary(x => x.CarIdx, x => x.Pos);
        return (overall, byClass);
    }
}
