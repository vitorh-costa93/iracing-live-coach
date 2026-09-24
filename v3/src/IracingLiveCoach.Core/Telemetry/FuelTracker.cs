using System.Text.Json;

namespace IracingLiveCoach.Core.Telemetry;

/// <summary>
/// Per-lap fuel measurement, pure and unit-tested (no SDK). A lap's burn is the fuel level at one
/// start/finish crossing minus the level at the next one. Only CLEAN green-flag laps enter the
/// average: a lap is dirty if at any moment of it the car was on pit road, the race was not green
/// (formation / pace / safety-car), the car was towed/reset, or fuel went UP (refuel). A clean lap
/// that burns far less than the recent average (e.g. a lap cut short by a reset the flags missed) is
/// also rejected. Until the first clean lap of this stint, a seeded average from earlier sessions with
/// the same car and track is used -- the way Kapps shows a consumption figure from the first lap.
/// </summary>
public sealed class FuelTracker
{
    public const int WindowSize = 5;
    private const double OutlierShare = 0.6;

    private readonly Queue<double> _fuel = new();
    private readonly Queue<double> _lapTimes = new();
    private int _lastLap = -1;
    private double? _lapStartFuel;
    private double? _previousFuel;
    private bool _lapDirty;
    private double? _seedFuel;
    private double? _seedLapTime;

    public double? LastLapUsed { get; private set; }
    public bool LastLapDirty { get; private set; }
    public int CleanLaps => _fuel.Count;

    /// <summary>History from earlier sessions (same car + track): used only until real laps exist.</summary>
    public void Seed(double? fuelPerLap, double? lapTime)
    {
        _seedFuel = fuelPerLap is > 0 ? fuelPerLap : null;
        _seedLapTime = lapTime is > 0 ? lapTime : null;
    }

    public double? AverageFuel => _fuel.Count > 0 ? _fuel.Average() : _seedFuel;
    public double? MaxFuel => _fuel.Count > 0 ? _fuel.Max() : _seedFuel;
    public double? AverageLapTime => _lapTimes.Count > 0 ? _lapTimes.Average() : _seedLapTime;
    /// <summary>Average of laps actually driven this connection (no history seed) -- the only lap time
    /// allowed into the race-length projection (a seeded 204.9 s history lap gave "≈13.17" where Kapps
    /// showed 34.53, 24/09/2026).</summary>
    public double? MeasuredAverageLapTime => _lapTimes.Count > 0 ? _lapTimes.Average() : null;
    public bool AverageIsFromHistory => _fuel.Count == 0 && _seedFuel is not null;

    /// <summary>One telemetry tick.</summary>
    /// <param name="dirtyNow">Pit road, not green, caution, or towed at this instant.</param>
    /// <param name="lastLapTime">LapLastLapTime (seconds) -- read when a lap completes.</param>
    /// <returns>True when a clean lap was just added to the average.</returns>
    public bool Update(double fuelLevel, int lapCompleted, bool dirtyNow, double lastLapTime)
    {
        if (_previousFuel is double before && fuelLevel > before + 0.05) _lapDirty = true; // refuelled
        _previousFuel = fuelLevel;
        if (dirtyNow) _lapDirty = true;

        if (_lastLap < 0 || lapCompleted < _lastLap)
        {
            // First tick, or a new session segment: we are not on a lap boundary, so no baseline yet.
            _lastLap = lapCompleted;
            _lapStartFuel = null;
            _lapDirty = false;
            return false;
        }
        if (lapCompleted == _lastLap) return false;

        bool added = false;
        bool skippedLaps = lapCompleted - _lastLap > 1; // missed crossings (reset/tow): not one lap's burn
        if (_lapStartFuel is double start && !skippedLaps)
        {
            double used = start - fuelLevel;
            LastLapUsed = used > 0 ? used : null;
            LastLapDirty = _lapDirty || used <= 0;
            if (!LastLapDirty && !IsOutlier(used))
            {
                _fuel.Enqueue(used);
                if (_fuel.Count > WindowSize) _fuel.Dequeue();
                if (lastLapTime > 0)
                {
                    _lapTimes.Enqueue(lastLapTime);
                    if (_lapTimes.Count > WindowSize) _lapTimes.Dequeue();
                }
                added = true;
            }
        }

        _lastLap = lapCompleted;
        _lapStartFuel = fuelLevel;
        _lapDirty = dirtyNow;
        return added;
    }

    private bool IsOutlier(double used)
    {
        double? reference = _fuel.Count >= 2 ? Median(_fuel) : _seedFuel;
        return reference is double r && used < r * OutlierShare;
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        return sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
    }
}

/// <summary>Remembers the clean per-lap average and lap time per car + track across sessions.</summary>
public sealed class FuelHistoryStore
{
    private readonly string _path;
    private Dictionary<string, FuelHistoryEntry> _entries;

    public FuelHistoryStore(string? path = null)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "iracing-live-coach", "fuel-history.json");
        try { _entries = File.Exists(_path) ? JsonSerializer.Deserialize<Dictionary<string, FuelHistoryEntry>>(File.ReadAllText(_path)) ?? new() : new(); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { _entries = new(); }
    }

    private static string Key(int carId, int trackId) => $"{carId}:{trackId}";

    public FuelHistoryEntry? Get(int carId, int trackId) => _entries.TryGetValue(Key(carId, trackId), out var e) ? e : null;

    public void Put(int carId, int trackId, double fuelPerLap, double? lapTime)
    {
        _entries[Key(carId, trackId)] = new FuelHistoryEntry(fuelPerLap, lapTime);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_entries));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* best effort */ }
    }
}

public sealed record FuelHistoryEntry(double FuelPerLap, double? LapTime);
