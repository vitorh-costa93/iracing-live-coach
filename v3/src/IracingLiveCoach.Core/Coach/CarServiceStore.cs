using System.Text.Json;

namespace IracingLiveCoach.Core.Coach;

/// <summary>Learned pit service of one car: refuel rate (L/s) and tyre-change time (s), last samples (oldest first).</summary>
public sealed record CarServiceProfile(List<double> FillRateLps, List<double> TireSec);

/// <summary>Persists the learned refuel rate / tyre time per car across sessions, in
/// %APPDATA%\iracing-live-coach\pit-service.json: { "version":1, "cars": { "&lt;carKey&gt;": { "fillRateLps":[..], "tireSec":[..] } } }.
/// Never throws: a missing/corrupt file reads as empty, writes are atomic (temp file + replace).</summary>
public sealed class CarServiceStore
{
    public const int MaxSamples = 10;
    public const double MinFillLps = 0.2, MaxFillLps = 60;
    public const double MinTireSec = 1, MaxTireSec = 60;

    private readonly string _path;
    private readonly object _lock = new();
    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };

    public CarServiceStore(string? path = null)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "iracing-live-coach", "pit-service.json");
    }

    public static bool PlausibleFill(double v) => !double.IsNaN(v) && v >= MinFillLps && v <= MaxFillLps;
    public static bool PlausibleTire(double v) => !double.IsNaN(v) && v >= MinTireSec && v <= MaxTireSec;

    /// <summary>Stable key of the player's car: CarPath, else CarScreenName (lower-case); null when neither exists.</summary>
    public static string? CarKey(string? carPath, string? carScreenName)
    {
        var k = !string.IsNullOrWhiteSpace(carPath) ? carPath : carScreenName;
        return string.IsNullOrWhiteSpace(k) ? null : k.Trim().ToLowerInvariant();
    }

    public CarServiceProfile? Load(string carKey)
    {
        lock (_lock)
        {
            var cars = ReadAll();
            return cars.TryGetValue(carKey, out var p) ? p : null;
        }
    }

    public void Save(string carKey, CarServiceProfile profile)
    {
        try
        {
            lock (_lock)
            {
                var cars = ReadAll();
                cars[carKey] = new CarServiceProfile(Clean(profile.FillRateLps, PlausibleFill), Clean(profile.TireSec, PlausibleTire));
                var dto = new FileDto { Version = 1, Cars = cars.ToDictionary(c => c.Key, c => new CarDto { FillRateLps = c.Value.FillRateLps, TireSec = c.Value.TireSec }) };
                var dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var tmp = _path + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(dto, Opts));
                if (File.Exists(_path)) File.Replace(tmp, _path, null);
                else File.Move(tmp, _path);
            }
        }
        catch { /* persistence is best effort */ }
    }

    private Dictionary<string, CarServiceProfile> ReadAll()
    {
        var result = new Dictionary<string, CarServiceProfile>();
        try
        {
            if (!File.Exists(_path)) return result;
            var dto = JsonSerializer.Deserialize<FileDto>(File.ReadAllText(_path), Opts);
            if (dto?.Cars is null) return result;
            foreach (var (key, c) in dto.Cars)
            {
                if (c is null || string.IsNullOrWhiteSpace(key)) continue;
                result[key] = new CarServiceProfile(Clean(c.FillRateLps, PlausibleFill), Clean(c.TireSec, PlausibleTire));
            }
        }
        catch { result.Clear(); }
        return result;
    }

    private static List<double> Clean(List<double>? src, Func<double, bool> ok)
    {
        var l = (src ?? new()).Where(ok).ToList();
        return l.Count > MaxSamples ? l.GetRange(l.Count - MaxSamples, MaxSamples) : l;
    }

    private sealed class FileDto
    {
        public int Version { get; set; }
        public Dictionary<string, CarDto?>? Cars { get; set; }
    }

    private sealed class CarDto
    {
        public List<double>? FillRateLps { get; set; }
        public List<double>? TireSec { get; set; }
    }
}
