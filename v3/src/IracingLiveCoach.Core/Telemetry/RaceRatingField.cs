namespace IracingLiveCoach.Core.Telemetry;

public readonly record struct RatingDriver(int CarIdx, int ClassId, int IRating, bool PaceCar = false, bool Spectator = false);
public readonly record struct RatingPosition(int CarIdx, int Laps, double Distance, int OfficialPosition);

/// <summary>Race entrants are independent of the cars currently present in the world.
/// Keep entrants and their last valid progress until the session changes.</summary>
public sealed class RaceRatingField
{
    private readonly Dictionary<int, RatingDriver> _drivers = new();
    private readonly Dictionary<int, (int Laps, double Distance)> _progress = new();

    public void Reset() { _drivers.Clear(); _progress.Clear(); }

    public void Observe(IEnumerable<RatingDriver> drivers)
    {
        foreach (var driver in drivers)
            if (!driver.PaceCar && !driver.Spectator && driver.CarIdx >= 0)
                if (!_drivers.TryGetValue(driver.CarIdx, out var existing) || existing.IRating <= 1 && driver.IRating > 1)
                    _drivers[driver.CarIdx] = driver;
    }

    public double? Sof => global::IracingLiveCoach.Core.Telemetry.Sof.Compute(_drivers.Values.Where(d => d.IRating > 1).Select(d => d.IRating));
    public Dictionary<int, double?> ClassSof => _drivers.Values.GroupBy(d => d.ClassId)
        .ToDictionary(g => g.Key, g => global::IracingLiveCoach.Core.Telemetry.Sof.Compute(g.Where(d => d.IRating > 1).Select(d => d.IRating)));
    public int Count => _drivers.Count;
    public IEnumerable<int> CarIndices => _drivers.Keys;

    public Dictionary<int, double> Project(IEnumerable<RatingPosition> positions, bool finalResults)
    {
        var current = positions.ToDictionary(p => p.CarIdx);
        foreach (var p in current.Values)
            if (p.Distance >= 0 && double.IsFinite(p.Distance))
                _progress[p.CarIdx] = (p.Laps, p.Distance);
            else if (_progress.TryGetValue(p.CarIdx, out var previous) && p.Laps > previous.Laps)
                _progress[p.CarIdx] = (p.Laps, 0);

        var result = new Dictionary<int, double>();
        foreach (var group in _drivers.Values.GroupBy(d => d.ClassId))
        {
            var ranked = finalResults
                ? group.OrderBy(d => current.TryGetValue(d.CarIdx, out var p) && p.OfficialPosition > 0 ? p.OfficialPosition : int.MaxValue).ThenBy(d => d.CarIdx)
                : group.OrderByDescending(d => _progress.GetValueOrDefault(d.CarIdx).Laps)
                    .ThenByDescending(d => _progress.GetValueOrDefault(d.CarIdx).Distance)
                    .ThenBy(d => current.TryGetValue(d.CarIdx, out var p) && p.OfficialPosition > 0 ? p.OfficialPosition : int.MaxValue).ThenBy(d => d.CarIdx);
            foreach (var pair in IRatingProjection.Compute(ranked.Select((d, i) => new IRatingEntry(d.CarIdx, d.IRating, i + 1))))
                result[pair.Key] = pair.Value;
        }
        return result;
    }
}
