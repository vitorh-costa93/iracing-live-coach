namespace IracingLiveCoach.Core.Telemetry;

public readonly record struct RosterDriver<T>(int CarIdx, int ClassId, T Identity, bool PaceCar = false, bool Spectator = false);
public readonly record struct RosterTiming(int Position, double? BestLap, double? LastLap, int Laps)
{
    public bool HasValidLap => Position > 0 && BestLap is > 0;
}

/// <summary>All observed entrants of one event, independent of their on-track presence. Identity survives
/// session changes/reconnections; timed laps belong only to the current session. A new event clears both.</summary>
public sealed class StandingsRoster<T>
{
    private readonly Dictionary<int, RosterDriver<T>> _drivers = new();
    private readonly Dictionary<int, RosterTiming> _timing = new();
    private RatingEventKey? _event;
    private int? _sessionNum;

    public IEnumerable<RosterDriver<T>> Drivers => _drivers.Values;
    public int Count => _drivers.Count;

    public void Enter(RatingEventKey evt, int sessionNum)
    {
        if (_event != evt) { _drivers.Clear(); _timing.Clear(); _event = evt; _sessionNum = sessionNum; }
        else if (_sessionNum != sessionNum) { _timing.Clear(); _sessionNum = sessionNum; }
    }

    public void Observe(IEnumerable<RosterDriver<T>> drivers)
    {
        foreach (var driver in drivers)
        {
            if (driver.CarIdx < 0) continue;
            if (driver.PaceCar || driver.Spectator) { _drivers.Remove(driver.CarIdx); _timing.Remove(driver.CarIdx); }
            else _drivers[driver.CarIdx] = driver;
        }
    }

    public bool TryGetDriver(int carIdx, out RosterDriver<T> driver) => _drivers.TryGetValue(carIdx, out driver);
    public RosterTiming Timing(int carIdx) => _timing.GetValueOrDefault(carIdx);

    public void ObserveTiming(int carIdx, int position, double? bestLap, double? lastLap, int laps)
    {
        if (!_drivers.ContainsKey(carIdx)) return;
        var previous = Timing(carIdx);
        bool validBest = position > 0 && bestLap is > 0 and double best && double.IsFinite(best);
        // An official lap can be deleted/revised. Preserve a time only while the current source is
        // absent; a supplied valid best lap must replace the cache even when it is slower.
        double? fastest = validBest ? bestLap : previous.BestLap;
        _timing[carIdx] = new(position > 0 ? position : previous.Position, fastest,
            lastLap is > 0 and double last && double.IsFinite(last) ? last : previous.LastLap, Math.Max(previous.Laps, laps));
    }

    /// <summary>An explicit current-session result is authoritative, including a deleted only lap.
    /// A result row without a classified position or a valid best lap clears both; a missing result
    /// row never calls this method, so a disconnected entrant's classification remains cached.</summary>
    public void ObserveOfficialTiming(int carIdx, int position, double bestLap, double lastLap, int laps)
    {
        if (!_drivers.ContainsKey(carIdx)) return;
        var previous = Timing(carIdx);
        bool validBest = position > 0 && bestLap > 0 && double.IsFinite(bestLap);
        _timing[carIdx] = new(validBest ? position : 0, validBest ? bestLap : null,
            lastLap > 0 && double.IsFinite(lastLap) ? lastLap : previous.LastLap, Math.Max(previous.Laps, laps));
    }

    /// <summary>Timed standings include even entrants without a lap, then keep disconnected drivers'
    /// best laps in the classification. Sorting the saved times lets a new faster lap pass an absent driver.</summary>
    public (Dictionary<int, int> Overall, Dictionary<int, int> ByClass) TimedPositions()
    {
        var ranked = _drivers.Values.OrderBy(d => Timing(d.CarIdx).BestLap ?? double.MaxValue)
            .ThenBy(d => Timing(d.CarIdx).Position > 0 ? Timing(d.CarIdx).Position : int.MaxValue)
            .ThenBy(d => d.CarIdx).ToList();
        var overall = ranked.Select((d, i) => (d.CarIdx, Position: i + 1)).ToDictionary(x => x.CarIdx, x => x.Position);
        var byClass = ranked.GroupBy(d => d.ClassId).SelectMany(g => g.Select((d, i) => (d.CarIdx, Position: i + 1)))
            .ToDictionary(x => x.CarIdx, x => x.Position);
        return (overall, byClass);
    }
}
