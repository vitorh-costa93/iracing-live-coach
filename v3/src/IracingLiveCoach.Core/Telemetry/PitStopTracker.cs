namespace IracingLiveCoach.Core.Telemetry;

/// <summary>
/// Pit column data. A pit STOP is a car driving INTO pit road (CarIdxOnPitRoad false -> true) while a
/// race is green (Race session, SessionState = Racing). Everything else is not a stop and leaves the
/// column empty: sitting in the pit stall before the start, starting from the pit lane, practice and
/// qualifying runs, the cool-down lap after the flag. That is what keeps the Standings pit column hidden
/// for a whole race without pit stops (the widget shows the column only when some row has a status).
///
/// Status text (Kapps): "PIT 12" while in the pit lane on a stop ("TOW 28m" for the player's tow), then
/// "L8 24.0" / "L1 1:26" (lap of the stop + pit-lane time) for the rest of the race. <see cref="Reset"/> on every session change. Pure and unit-tested.
/// </summary>
public sealed class PitStopTracker
{
    private readonly Dictionary<int, bool> _lastOnPitRoad = new();
    private readonly Dictionary<int, DateTime> _enteredUtc = new();
    private readonly Dictionary<int, string> _lastStop = new();

    private readonly HashSet<int> _towing = new();

    /// <param name="stopsCount">True only in a Race session while SessionState == Racing.</param>
    /// <param name="towed">The player's own car is being towed (PlayerCarTowTime &gt; 0): that visit to the
    /// pits is a tow, shown as Kapps' "TOW 28m" while the car is still there.</param>
    public string Update(int carIdx, bool onPitRoad, int lap, bool stopsCount, DateTime nowUtc, bool towed = false)
    {
        bool known = _lastOnPitRoad.TryGetValue(carIdx, out var wasOnPitRoad);
        _lastOnPitRoad[carIdx] = onPitRoad;

        if (towed && stopsCount)
        {
            _towing.Add(carIdx);
            _enteredUtc.TryAdd(carIdx, nowUtc);
        }

        if (onPitRoad || towed)
        {
            if (!_enteredUtc.ContainsKey(carIdx) && stopsCount && known && !wasOnPitRoad)
                _enteredUtc[carIdx] = nowUtc; // a real entry during the race
            if (_enteredUtc.TryGetValue(carIdx, out var entered))
                return (_towing.Contains(carIdx) ? "TOW " : "PIT ") + LiveDuration((nowUtc - entered).TotalSeconds);
        }
        else if (_enteredUtc.Remove(carIdx, out var entry))
        {
            _towing.Remove(carIdx);
            _lastStop[carIdx] = $"L{Math.Max(0, lap)} {StopDuration((nowUtc - entry).TotalSeconds)}";
        }
        return _lastStop.TryGetValue(carIdx, out var last) ? last : "";
    }

    /// <summary>Kapps' running pit timer: "PIT 35" (seconds) and, past a minute, whole minutes ("TOW 28m").</summary>
    public static string LiveDuration(double seconds)
    {
        seconds = Math.Max(0, seconds) + 1e-6;
        return seconds < 60
            ? Math.Floor(seconds).ToString("0", System.Globalization.CultureInfo.InvariantCulture)
            : Math.Floor(seconds / 60).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "m";
    }

    /// <summary>Kapps' finished stop: "58.8" under a minute, "1:26" from a minute on (live print 24/09/2026:
    /// "L1 55.9", "L5 59.4", "L1 1:26").</summary>
    public static string StopDuration(double seconds)
    {
        seconds = Math.Max(0, seconds) + 1e-6;
        if (seconds < 60) return (Math.Floor(seconds * 10) / 10).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        int whole = (int)Math.Floor(seconds);
        return $"{whole / 60}:{whole % 60:00}";
    }

    public void Reset()
    {
        _lastOnPitRoad.Clear();
        _enteredUtc.Clear();
        _lastStop.Clear();
        _towing.Clear();
    }
}
