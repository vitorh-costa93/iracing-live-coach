using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace IracingLiveCoach.Core.Coach;

/// <summary>Reference lap stored by distance bin (<see cref="BinCount"/> bins over the lap).
/// Elapsed[i] = seconds since the line when the bin was reached.</summary>
public sealed class ReferenceTrace
{
    public const int BinCount = 1000;

    public string TrackId { get; set; } = "";
    public string CarId { get; set; } = "";
    public double TrackLengthM { get; set; }
    public double LapTimeSec { get; set; }
    public int Lap { get; set; }
    public DateTime RecordedUtc { get; set; }
    public double[] Speed { get; set; } = new double[BinCount];
    public double[] Brake { get; set; } = new double[BinCount];
    public double[] Throttle { get; set; } = new double[BinCount];
    public double[] Steering { get; set; } = new double[BinCount];
    public int[] Gear { get; set; } = new int[BinCount];
    public double[] Elapsed { get; set; } = new double[BinCount];

    public double BinLengthM => TrackLengthM / BinCount;

    public static int BinOf(double pct)
    {
        pct -= Math.Floor(pct);
        return Math.Clamp((int)(pct * BinCount), 0, BinCount - 1);
    }

    /// <summary>Maps an unwrapped bin (corners crossing the line use bins &lt; 0 or &gt;= BinCount) to 0..BinCount-1.</summary>
    internal static int Wrap(int bin) => ((bin % BinCount) + BinCount) % BinCount;

    internal static double PctOf(int bin) => (double)Wrap(bin) / BinCount;

    public bool IsWellFormed =>
        TrackLengthM > 0 && LapTimeSec > 0 &&
        new[] { Speed.Length, Brake.Length, Throttle.Length, Steering.Length, Gear.Length, Elapsed.Length }
            .All(l => l == BinCount);

    internal static ReferenceTrace FromLap(RecordedLap lap, CoachContext ctx, DateTime utc) => new()
    {
        TrackId = ctx.TrackId,
        CarId = ctx.CarId,
        TrackLengthM = ctx.TrackLengthM,
        LapTimeSec = lap.LapTimeSec,
        Lap = lap.Lap,
        RecordedUtc = utc,
        Speed = (double[])lap.Speed.Clone(),
        Brake = (double[])lap.Brake.Clone(),
        Throttle = (double[])lap.Throttle.Clone(),
        Steering = (double[])lap.Steering.Clone(),
        Gear = (int[])lap.Gear.Clone(),
        Elapsed = (double[])lap.Elapsed.Clone(),
    };
}

/// <summary>Persists one reference per track/car as <c>refs\&lt;track&gt;_&lt;car&gt;.json</c>.</summary>
public sealed class ReferenceStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    private readonly string _dir;

    public ReferenceStore(string dataDir) => _dir = Path.Combine(dataDir, "refs");

    public string PathFor(string trackId, string carId) =>
        Path.Combine(_dir, $"{Sanitize(trackId)}_{Sanitize(carId)}.json");

    public ReferenceTrace? Load(string trackId, string carId)
    {
        try
        {
            var path = PathFor(trackId, carId);
            if (!File.Exists(path)) return null;
            var r = JsonSerializer.Deserialize<ReferenceTrace>(File.ReadAllText(path), Json);
            return r is { IsWellFormed: true } ? r : null;
        }
        catch (Exception) { return null; }
    }

    public void Save(ReferenceTrace r)
    {
        try
        {
            Directory.CreateDirectory(_dir);
            var path = PathFor(r.TrackId, r.CarId);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(r, Json));
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception) { /* persistence is best-effort; the live coach keeps the in-memory ref */ }
    }

    internal static string Sanitize(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "unknown";
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(s.Length);
        foreach (var c in s.Trim())
            sb.Append(invalid.Contains(c) || c == '_' || char.IsWhiteSpace(c) ? '-' : char.ToLowerInvariant(c));
        return sb.ToString();
    }
}
