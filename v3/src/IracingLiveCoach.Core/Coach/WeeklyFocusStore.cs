using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace IracingLiveCoach.Core.Coach;

/// <summary>One row of history.json: technique sums for a track/car in one ISO week.</summary>
public sealed class WeeklyHistoryEntry
{
    public string TrackId { get; set; } = "";
    public string CarId { get; set; } = "";
    public string Week { get; set; } = "";
    public int Laps { get; set; }
    public double BestLapSec { get; set; }
    public double BrakingSum { get; set; }
    public double TrailBrakingSum { get; set; }
    public double ThrottleSum { get; set; }
    public double ConsistencySum { get; set; }
    public DateTime LastUtc { get; set; }

    internal WeeklyHistoryEntry Clone() => (WeeklyHistoryEntry)MemberwiseClone();
}

/// <summary>Per track/car/week history (history.json). Focus = worst average technique category.</summary>
public sealed class WeeklyFocusStore
{
    public const int MinLapsForFocus = 3;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string _path;
    private readonly CoachWriteQueue? _writer;
    private List<WeeklyHistoryEntry> _entries;

    public WeeklyFocusStore(string dataDir) : this(dataDir, null) { }

    /// <summary>With a writer, Record only enqueues the save (a copy of the entries) on its thread.</summary>
    public WeeklyFocusStore(string dataDir, CoachWriteQueue? writer)
    {
        _path = Path.Combine(dataDir, "history.json");
        _writer = writer;
        _entries = Load();
    }

    public IReadOnlyList<WeeklyHistoryEntry> Entries => _entries;

    public static string WeekKey(DateTime date)
    {
        int week = ISOWeek.GetWeekOfYear(date);
        int year = ISOWeek.GetYear(date);
        return $"{year}-W{week:00}";
    }

    public void Record(string trackId, string carId, DateTime now, double lapTimeSec, TechniqueBreakdown b)
    {
        var week = WeekKey(now);
        var e = _entries.FirstOrDefault(x => x.TrackId == trackId && x.CarId == carId && x.Week == week);
        if (e == null)
        {
            e = new WeeklyHistoryEntry { TrackId = trackId, CarId = carId, Week = week };
            _entries.Add(e);
        }
        e.Laps++;
        if (lapTimeSec > 0 && (e.BestLapSec <= 0 || lapTimeSec < e.BestLapSec)) e.BestLapSec = lapTimeSec;
        e.BrakingSum += b.Braking;
        e.TrailBrakingSum += b.TrailBraking;
        e.ThrottleSum += b.Throttle;
        e.ConsistencySum += b.Consistency;
        e.LastUtc = now.ToUniversalTime();
        Save();
    }

    /// <summary>Worst category ("Braking", "TrailBraking", "Throttle", "Consistency") for this week on
    /// the track/car; falls back to all weeks of the track/car. Null if not enough laps.</summary>
    public string? GetFocus(string trackId, string carId, DateTime now)
    {
        var week = WeekKey(now);
        var mine = _entries.Where(x => x.TrackId == trackId && x.CarId == carId).ToList();
        var thisWeek = mine.Where(x => x.Week == week).ToList();
        var pick = thisWeek.Sum(x => x.Laps) >= MinLapsForFocus ? thisWeek : mine;
        int laps = pick.Sum(x => x.Laps);
        if (laps < MinLapsForFocus) return null;
        var avg = new TechniqueBreakdown(
            (int)Math.Round(pick.Sum(x => x.BrakingSum) / laps),
            (int)Math.Round(pick.Sum(x => x.TrailBrakingSum) / laps),
            (int)Math.Round(pick.Sum(x => x.ThrottleSum) / laps),
            (int)Math.Round(pick.Sum(x => x.ConsistencySum) / laps));
        return avg.WorstCategory;
    }

    private List<WeeklyHistoryEntry> Load()
    {
        try
        {
            if (!File.Exists(_path)) return new();
            return JsonSerializer.Deserialize<List<WeeklyHistoryEntry>>(File.ReadAllText(_path), Json) ?? new();
        }
        catch (Exception) { return new(); }
    }

    private void Save()
    {
        if (_writer == null) { Write(_path, _entries); return; }
        // Copy now (cheap, few rows): the engine keeps mutating _entries while the writer serializes.
        var copy = _entries.Select(e => e.Clone()).ToList();
        var path = _path;
        _writer.Enqueue(path, () => Write(path, copy));
    }

    private static void Write(string path, List<WeeklyHistoryEntry> entries)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(entries, Json));
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception) { /* best-effort */ }
    }
}
