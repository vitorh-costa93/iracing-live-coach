namespace IracingLiveCoach.Core.Telemetry;

public readonly record struct RatingDriver(int CarIdx, int ClassId, int IRating, bool PaceCar = false, bool Spectator = false);
/// <summary>OfficialPosition: final classification / starting grid in the "official" mode of
/// <see cref="RaceRatingField.Project"/> (0 = the car has none), CarIdxPosition otherwise (tiebreak only).</summary>
public readonly record struct RatingPosition(int CarIdx, int Laps, double Distance, int OfficialPosition);

/// <summary>Identity of one iRacing event (practice/qualifying/race share it). Online: WeekendInfo
/// SessionID + SubSessionID, which survive an SDK reconnection or a sim restart into the same event.
/// Offline (SubSessionID 0): the telemetry SessionUniqueID of that sim run.</summary>
public readonly record struct RatingEventKey(long SessionId, long SubSessionId, int UniqueId)
{
    public static RatingEventKey From(long sessionId, long subSessionId, int uniqueId) =>
        subSessionId > 0 ? new(sessionId, subSessionId, 0) : new(0, 0, uniqueId);
}

/// <summary>
/// Rating field (SOF / iRating projection) of an event, replicating Kapps standings2 (worker.js, the
/// <c>R.watch(["DriverInfo","WeekendInfo"])</c> handler): every DriverInfo update upserts each
/// non-pace-car, non-spectator driver into a CarIdx map that is NEVER pruned -- a driver who leaves
/// (and drops out of DriverInfo) keeps counting; a late entrant is added. Kapps clears that map only
/// when its SDK connection drops; here it is cleared only when the event itself changes, so an SDK
/// reconnection into the same event cannot shrink the field. Progress (for the live order) belongs
/// to one session of the event and resets when the session number changes.
/// </summary>
public sealed class RaceRatingField
{
    private readonly Dictionary<int, RatingDriver> _drivers = new();
    private readonly Dictionary<int, (int Laps, double Distance)> _progress = new();
    private RatingEventKey? _event;
    private int? _sessionNum;

    public void Reset() { _drivers.Clear(); _progress.Clear(); _event = null; _sessionNum = null; }

    /// <summary>Binds the field to an event/session: a different event clears everything, a different
    /// session of the same event clears only the progress.</summary>
    public void Enter(RatingEventKey evt, int sessionNum)
    {
        if (_event != evt) { _drivers.Clear(); _progress.Clear(); _event = evt; _sessionNum = sessionNum; return; }
        if (_sessionNum != sessionNum) { _progress.Clear(); _sessionNum = sessionNum; }
    }

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

    /// <summary>Projected iRating change per class. <paramref name="officialOrder"/> (final results /
    /// pre-green grid): cars with an official position first, in that order; any car without one comes
    /// after them by progress -- never interleaved with live CarIdxPosition values.</summary>
    public Dictionary<int, double> Project(IEnumerable<RatingPosition> positions, bool officialOrder)
    {
        var current = positions.ToDictionary(p => p.CarIdx);
        foreach (var p in current.Values)
            if (p.Distance >= 0 && double.IsFinite(p.Distance))
                _progress[p.CarIdx] = (p.Laps, p.Distance);
            else if (_progress.TryGetValue(p.CarIdx, out var previous) && p.Laps > previous.Laps)
                _progress[p.CarIdx] = (p.Laps, 0);

        int Official(RatingDriver d) => current.TryGetValue(d.CarIdx, out var p) && p.OfficialPosition > 0 ? p.OfficialPosition : int.MaxValue;
        var result = new Dictionary<int, double>();
        foreach (var group in _drivers.Values.GroupBy(d => d.ClassId))
        {
            var ranked = officialOrder
                ? group.OrderBy(Official)
                    .ThenByDescending(d => _progress.GetValueOrDefault(d.CarIdx).Laps)
                    .ThenByDescending(d => _progress.GetValueOrDefault(d.CarIdx).Distance)
                    .ThenBy(d => d.CarIdx)
                : group.OrderByDescending(d => _progress.GetValueOrDefault(d.CarIdx).Laps)
                    .ThenByDescending(d => _progress.GetValueOrDefault(d.CarIdx).Distance)
                    .ThenBy(Official).ThenBy(d => d.CarIdx);
            foreach (var pair in IRatingProjection.Compute(ranked.Select((d, i) => new IRatingEntry(d.CarIdx, d.IRating, i + 1))))
                result[pair.Key] = pair.Value;
        }
        return result;
    }
}
