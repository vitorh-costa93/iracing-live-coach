namespace IracingLiveCoach.Core.Telemetry;

/// <summary>
/// Pit column data. A pit STOP is a car driving INTO pit road (CarIdxOnPitRoad false -> true) while a
/// race is green (Race session, SessionState = Racing). Everything else is not a stop and leaves the
/// column empty: sitting in the pit stall before the start, starting from the pit lane, practice and
/// qualifying runs, the cool-down lap after the flag. That is what keeps the Standings pit column hidden
/// for a whole race without pit stops (the widget shows the column only when some row has a status).
///
/// Status text: "PIT 12s" while in the pit lane on a stop, then "L8 24s" (lap of the stop + pit-lane
/// time) for the rest of the race. <see cref="Reset"/> on every session change. Pure and unit-tested.
/// </summary>
public sealed class PitStopTracker
{
    private readonly Dictionary<int, bool> _lastOnPitRoad = new();
    private readonly Dictionary<int, DateTime> _enteredUtc = new();
    private readonly Dictionary<int, string> _lastStop = new();

    /// <param name="stopsCount">True only in a Race session while SessionState == Racing.</param>
    public string Update(int carIdx, bool onPitRoad, int lap, bool stopsCount, DateTime nowUtc)
    {
        bool known = _lastOnPitRoad.TryGetValue(carIdx, out var wasOnPitRoad);
        _lastOnPitRoad[carIdx] = onPitRoad;

        if (onPitRoad)
        {
            if (!_enteredUtc.ContainsKey(carIdx) && stopsCount && known && !wasOnPitRoad)
                _enteredUtc[carIdx] = nowUtc; // a real entry during the race
            if (_enteredUtc.TryGetValue(carIdx, out var entered))
                return $"PIT {Math.Max(0, (nowUtc - entered).TotalSeconds):0}s";
        }
        else if (_enteredUtc.Remove(carIdx, out var entry))
        {
            _lastStop[carIdx] = $"L{Math.Max(0, lap)} {Math.Max(0, (nowUtc - entry).TotalSeconds):0}s";
        }
        return _lastStop.TryGetValue(carIdx, out var last) ? last : "";
    }

    public void Reset()
    {
        _lastOnPitRoad.Clear();
        _enteredUtc.Clear();
        _lastStop.Clear();
    }
}
