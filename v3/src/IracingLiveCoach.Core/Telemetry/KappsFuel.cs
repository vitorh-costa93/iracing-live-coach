using System.Text.Json;

namespace IracingLiveCoach.Core.Telemetry;

/// <summary>One consumption basis of Kapps' fuel panel (Average / Qualify / Last). All null = "--.--".</summary>
public sealed record KappsFuelRow(string Label, double? PerLap, double? LapsRemain, double? Refuel, double? FuelAtEnd);

/// <summary>Kapps' fuel panel: Fuel Level, Laps in Race and the three basis rows.</summary>
public sealed record KappsFuelPanel(double FuelLevel, double? LapsInRace, IReadOnlyList<KappsFuelRow> Rows);

/// <summary>What <see cref="FuelLapTracker"/> reads from one telemetry tick of the player's car.</summary>
public readonly record struct FuelLapTick(
    bool IsRace,
    bool IsTestEvent,
    int SessionState,
    int SessionFlags,
    double FuelLevel,
    double LapDistPct,
    bool IsOnTrack,
    bool OnPitRoad,
    int TrackSurface,
    double LFwearR,
    int PlayerLap);

/// <summary>A lap the player just completed: its fuel usage and whether it counts (spec B.2).</summary>
[Flags]
public enum FuelLapInvalidReason
{
    None = 0, Refuel = 1, Tyres = 2, Surface = 4, Flags = 8,
    SessionState = 16, PitRoad = 32, NoConsumption = 64, FirstRaceLap = 128,
}

public readonly record struct FuelLap(double Usage, bool Valid, int LapNumber, FuelLapInvalidReason InvalidReason = FuelLapInvalidReason.None);

/// <summary>
/// Kapps' fuel lap measurement (spec B.1-B.2). A lap is the LapDistPct wrap (&gt; 0.9 to &lt; 0.1) while on
/// track; its usage is the fuel at its start minus the fuel at its end. It only counts when:
///  * it did not start on pit road (the out-lap never counts, the next one does);
///  * fuel did not exceed the lap-start level, LFwearR did not change, the surface never was -1 and never
///    switched between on-track (3) and pit stall (1);
///  * no flag bit invalidated it: one-lap-to-green (0x200) without chequered and outside a Test event, or any of
///    caution / caution waving / green held / furled (0x4000 | 0x8000 | 0x400 | 0x80000);
///  * at its end: racing (SessionState 4), off pit road, no caution (0xC000), fuel went down;
///  * in a race: it is lap 2 or later (lap 1 never counts).
/// Pure; unit-tested.
/// </summary>
public sealed class FuelLapTracker
{
    private const int Racing = 4;
    private const int FlagCheckered = 0x1;
    private const int FlagOneLapToGreen = 0x200;
    private const int FlagGreenHeld = 0x400;
    private const int FlagCaution = 0x4000;
    private const int FlagCautionWaving = 0x8000;
    private const int FlagFurled = 0x80000;
    private const int SurfaceInPitStall = 1;
    private const int SurfaceOnTrack = 3;

    private double _prevPct = double.NaN;
    private double _prevWear = double.NaN;
    private int? _prevSurface;
    private double? _lapStartFuel;
    private int _lapNumber;
    private bool _valid;
    private FuelLapInvalidReason _invalidReason;

    public void Reset()
    {
        _prevPct = _prevWear = double.NaN;
        _prevSurface = null;
        _lapStartFuel = null;
        _valid = false;
        _invalidReason = FuelLapInvalidReason.None;
    }

    /// <summary>One tick; returns the lap just completed, if any.</summary>
    public FuelLap? Update(in FuelLapTick t)
    {
        // Kapps fuel-calc.js compares with lastFuelLevel, latched at the line, not the previous tick.
        if (_lapStartFuel is double lapStart && t.FuelLevel > lapStart) Invalidate(FuelLapInvalidReason.Refuel);
        if (!double.IsNaN(_prevWear) && t.LFwearR != _prevWear) Invalidate(FuelLapInvalidReason.Tyres);
        if (t.TrackSurface == -1) Invalidate(FuelLapInvalidReason.Surface);
        if (_prevSurface is int ps && ((ps == SurfaceOnTrack && t.TrackSurface == SurfaceInPitStall) || (ps == SurfaceInPitStall && t.TrackSurface == SurfaceOnTrack))) Invalidate(FuelLapInvalidReason.Surface);
        if ((t.SessionFlags & FlagOneLapToGreen) != 0 && (t.SessionFlags & FlagCheckered) == 0 && !t.IsTestEvent) Invalidate(FuelLapInvalidReason.Flags);
        if ((t.SessionFlags & (FlagCaution | FlagCautionWaving | FlagGreenHeld | FlagFurled)) != 0) Invalidate(FuelLapInvalidReason.Flags);

        FuelLap? completed = null;
        bool crossing = t.IsOnTrack && _prevPct > 0.9 && t.LapDistPct >= 0 && t.LapDistPct < 0.1;
        if (crossing)
        {
            if (_lapStartFuel is double start)
            {
                int lapNumber = Math.Max(_lapNumber, t.PlayerLap - 1);
                if (t.SessionState != Racing) Invalidate(FuelLapInvalidReason.SessionState);
                if (t.OnPitRoad) Invalidate(FuelLapInvalidReason.PitRoad);
                if (!(t.FuelLevel < start)) Invalidate(FuelLapInvalidReason.NoConsumption);
                if (t.IsRace && lapNumber < 2) Invalidate(FuelLapInvalidReason.FirstRaceLap);
                bool ok = _valid && t.SessionState == Racing && !t.OnPitRoad && (t.SessionFlags & (FlagCaution | FlagCautionWaving)) == 0
                          && t.FuelLevel < start && (!t.IsRace || lapNumber >= 2);
                completed = new FuelLap(start - t.FuelLevel, ok, lapNumber, _invalidReason);
            }
            _lapStartFuel = t.FuelLevel;
            _lapNumber = t.PlayerLap;
            _valid = !t.OnPitRoad;
            _invalidReason = t.OnPitRoad ? FuelLapInvalidReason.PitRoad : FuelLapInvalidReason.None;
        }

        _prevPct = t.LapDistPct;
        _prevWear = t.LFwearR;
        _prevSurface = t.TrackSurface;
        return completed;
    }

    private void Invalidate(FuelLapInvalidReason reason)
    {
        _valid = false;
        _invalidReason |= reason;
    }
}

/// <summary>
/// Kapps' consumption figures (spec B.3-B.6): the last 5 valid laps; Average = their mean without the lowest and
/// the highest when there are 3 or more, the plain mean with 1-2, the stored history (ceil to 0.001) with none;
/// Last = the last lap's usage, empty after an invalid lap; Qualify = the usage of the qualifying lap that
/// became the player's fastest (confirmed by ResultsPositions), stored ceil to 0.001. Pure; unit-tested.
/// </summary>
public sealed class FuelConsumption
{
    public const int Window = 5;
    private readonly List<double> _laps = new();
    private (double Usage, int Lap)? _pendingQualify;

    public double? HistoryAverage { get; set; }
    public double? Qualify { get; set; }
    public double? Last { get; private set; }
    public IReadOnlyList<double> Laps => _laps;

    public double? Average
    {
        get
        {
            if (_laps.Count == 0) return HistoryAverage;
            if (_laps.Count < 3) return _laps.Average();
            var sorted = _laps.OrderBy(v => v).ToList();
            return sorted.Skip(1).Take(sorted.Count - 2).Average();
        }
    }

    public static double Store(double value) => Math.Ceiling(value * 1000 - 1e-9) / 1000;

    /// <summary>Adds a completed lap. Returns true when it was valid (the history should then be saved).</summary>
    public bool Add(FuelLap lap, bool inQualifying)
    {
        if (!lap.Valid) { Last = null; return false; }
        Last = lap.Usage;
        _laps.Add(lap.Usage);
        if (_laps.Count > Window) _laps.RemoveAt(0);
        if (inQualifying) _pendingQualify = (lap.Usage, lap.LapNumber);
        return true;
    }

    /// <summary>Qualifying: confirms the pending lap once ResultsPositions shows it is the player's fastest.
    /// Returns true when Qualify changed.</summary>
    public bool ConfirmQualify(int lapsComplete, int fastestLap)
    {
        if (_pendingQualify is not { } pending || lapsComplete != fastestLap || lapsComplete != pending.Lap) return false;
        Qualify = Store(pending.Usage);
        _pendingQualify = null;
        return true;
    }

    /// <summary>Another car/track: forget the laps (history is re-seeded by the caller).</summary>
    public void Clear()
    {
        _laps.Clear();
        _pendingQualify = null;
        Last = null;
        HistoryAverage = null;
        Qualify = null;
    }
}

/// <summary>
/// Kapps' fuel panel arithmetic (spec B.7-B.10), pure:
///  * Laps Remain = fuel / usage;
///  * laps left: race = ceil(class estimate) - max(0, class leader's CarIdxLap - 1, player's LapsComplete + 1),
///    0 after the flag; other sessions = ceil(session laps);
///  * Refuel = (laps left - Laps Remain) x usage, + 0.5 L when that is 1 L or more, never negative;
///  * Fuel at End = (Laps Remain - laps left) x usage + black-box fuel (PitSvFuel when its fuel box is ticked);
///    on the Average row the black box is ignored when the car already makes it to within 2 L. Never negative.
/// </summary>
public static class KappsFuel
{
    public const double RefuelReserve = 0.5;

    public static int? RaceLapsLeft(double? classEstimate, int classLeaderLap, int playerLapsComplete, bool finished)
    {
        if (finished) return 0;
        if (classEstimate is not double est || est <= 0) return null;
        int done = Math.Max(0, Math.Max(classLeaderLap - 1, playerLapsComplete + 1));
        return (int)Math.Ceiling(est - 1e-9) - done;
    }

    public static double? LapsRemain(double fuel, double? usage) => usage is double u && u > 0 ? Math.Max(0, fuel) / u : null;

    public static double? Refuel(double? lapsRemain, double? usage, int? lapsLeft)
    {
        if (lapsRemain is not double lr || usage is not double u || lapsLeft is not int left) return null;
        double r = (left - lr) * u;
        if (r >= 1) r += RefuelReserve;
        return Math.Max(0, r);
    }

    public static double? FuelAtEnd(double? lapsRemain, double? usage, int? lapsLeft, double blackBoxFuel, bool averageRow)
    {
        if (lapsRemain is not double lr || usage is not double u || lapsLeft is not int left) return null;
        double e = (lr - left) * u;
        double pit = averageRow && e >= -2 ? 0 : blackBoxFuel;
        return Math.Max(0, e + pit);
    }
}

/// <summary>
/// The live panel: rows are recomputed on the events Kapps recomputes them (a completed lap, the laps-left
/// recalculation at the player's crossing / the class leader's new lap, fuel going up in the pits) and hold in
/// between; Fuel at End follows the black box live except while the car sits in its pit stall.
/// </summary>
public sealed class KappsFuelPanelState
{
    private sealed record Latched(double? PerLap, double? LapsRemain, double? Refuel, double? FuelAtEnd);
    private readonly Latched?[] _rows = new Latched?[3];
    private static readonly string[] Labels = ["Average", "Qualify", "Last"];
    private int? _lapsLeft;
    private double? _lapsInRace;

    public int? LapsLeft => _lapsLeft;

    public void Reset() { Array.Clear(_rows); _lapsLeft = null; _lapsInRace = null; }

    /// <summary>Laps-left recalculation (player's crossing, class leader's new lap).</summary>
    public void SetLapsLeft(int? lapsLeft, double? lapsInRace) { _lapsLeft = lapsLeft; _lapsInRace = lapsInRace; }

    /// <summary>Recomputes Laps Remain / Refuel of every row from the current fuel and consumption.</summary>
    public void Recompute(double fuel, double? average, double? qualify, double? last)
    {
        double?[] rates = [average, qualify, last];
        for (int i = 0; i < 3; i++)
        {
            var lr = KappsFuel.LapsRemain(fuel, rates[i]);
            _rows[i] = new Latched(rates[i], lr, KappsFuel.Refuel(lr, rates[i], _lapsLeft), _rows[i]?.FuelAtEnd);
        }
    }

    /// <param name="inPitStall">PlayerTrackSurface == 1: Fuel at End keeps its last value.</param>
    public KappsFuelPanel Snapshot(double fuel, double blackBoxFuel, bool inPitStall)
    {
        var rows = new List<KappsFuelRow>(3);
        for (int i = 0; i < 3; i++)
        {
            var r = _rows[i];
            if (r is null || r.PerLap is null) { rows.Add(new KappsFuelRow(Labels[i], null, null, null, null)); continue; }
            double? fae = r.FuelAtEnd;
            if (!inPitStall) fae = KappsFuel.FuelAtEnd(r.LapsRemain, r.PerLap, _lapsLeft, blackBoxFuel, averageRow: i == 0);
            _rows[i] = r with { FuelAtEnd = fae };
            rows.Add(new KappsFuelRow(Labels[i], r.PerLap, r.LapsRemain, r.Refuel, fae));
        }
        return new KappsFuelPanel(fuel, _lapsInRace, rows);
    }
}

/// <summary>Remembers Kapps' stored consumption per car + track: the average and the qualifying lap (ceil to 0.001).</summary>
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

    public void Put(int carId, int trackId, double? fuelPerLap, double? qualifyPerLap)
    {
        var old = Get(carId, trackId);
        _entries[Key(carId, trackId)] = new FuelHistoryEntry(fuelPerLap ?? old?.FuelPerLap ?? 0, null, qualifyPerLap ?? old?.QualifyPerLap);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_entries));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* best effort */ }
    }
}

public sealed record FuelHistoryEntry(double FuelPerLap, double? LapTime, double? QualifyPerLap = null);
