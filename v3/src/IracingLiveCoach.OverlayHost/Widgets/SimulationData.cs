using IracingLiveCoach.Core.Telemetry;

namespace IracingLiveCoach.OverlayHost.Widgets;

/// <summary>
/// The one fully simulated situation, shared by the overlay's own simulation mode (T key) and the
/// Control Center's preview so both always show the same thing: a multiclass field (GTP + GT3) where
/// every car has push-to-pass, plus an animated radar, standing start and fuel burn, so every widget
/// and every adaptive feature (class panels, Overtake column) can be judged at once. It reproduces the Spa mockup
/// (GTP + GT3 multiclass, player 3rd in GT3, Ferrari/Aston/BMW/McLaren/Porsche/Mercedes/Ford, FR/GB/
/// DE/BR/US flags) so the real widgets can be compared against the reference image directly.
/// Never used as a stand-in for real telemetry.
/// </summary>
public static class SimulationData
{
    private const string Gtp = "#FF3038";
    private const string Gt3 = "#FFD400";

    public static List<StandingsRow> StandingsRows() =>
    [
        new(1, "Antoine Moreau", 12, 122.416, null, false, "🇫🇷", "A 4.12", null, 6240, 2, "Ferrari", null, 42, -0.648, "GTP", Gtp, 1, null, true, null, 124, false, "L8 24s", "6", 1),
        new(2, "Oliver Wilson", 12, 137.810, null, false, "🇬🇧", "A 4.10", null, 4390, 1, "Aston Martin", 0, 28, -0.216, "GT3", Gt3, 1, null, false, null, 142, true, "L8 24s", "27", 2),
        new(3, "Max Hoffmann", 12, 138.144, null, false, "🇩🇪", "A 3.72", null, 4210, 1, "BMW", 3.816, -8, 0.118, "GT3", Gt3, 2, 3.816, false, null, 167, true, "L8 24s", "18", 2),
        new(4, "Vitor Costa", 12, 138.026, null, true, "🇧🇷", "A 3.49", null, 3694, 1, "McLaren", 5.0, 12, 0.0, "GT3", Gt3, 3, 1.182, true, null, 108, false, "L8 24s", "93", 2),
        new(5, "Daniel Walker", 12, 138.330, null, false, "🇺🇸", "A 3.65", null, 4085, 1, "Porsche", 6.4, 5, 0.304, "GT3", Gt3, 4, 1.368, false, null, 96, false, "L8 24s", "44", 2),
        new(6, "Simon Wagner", 12, 138.420, null, false, "🇩🇪", "A 4.01", null, 4320, 1, "Mercedes", 7.1, -3, 0.394, "GT3", Gt3, 5, 0.715, false, null, 88, false, "L8 24s", "11", 2),
    ];

    public static List<RelativeRow> RelativeRows() =>
    [
        new(-3, "Antoine Moreau", -3.922, null, true, null, 124, false, "🇫🇷", "A 4.12", null, 6240, 2, "Ferrari", false, 1, "GTP", Gtp, "6", 1, 1),
        new(-2, "Oliver Wilson", -2.537, null, false, null, 142, true, "🇬🇧", "A 4.10", null, 4390, 1, "Aston Martin", false, 1, "GT3", Gt3, "27", 2, 2),
        new(-1, "Max Hoffmann", -1.182, null, false, null, 167, true, "🇩🇪", "A 3.72", null, 4210, 1, "BMW", false, 2, "GT3", Gt3, "18", 3, 2),
        new(0, "Vitor Costa", 0, null, true, null, 108, false, "🇧🇷", "A 3.49", null, 3694, 1, "McLaren", true, 3, "GT3", Gt3, "93", 4, 2),
        new(1, "Daniel Walker", 1.386, null, false, null, 96, false, "🇺🇸", "A 3.65", null, 4085, 1, "Porsche", false, 4, "GT3", Gt3, "44", 5, 2),
        new(2, "Simon Wagner", 2.083, null, false, null, 88, false, "🇩🇪", "A 4.01", null, 4320, 1, "Mercedes", false, 5, "GT3", Gt3, "11", 6, 2),
        new(3, "James Carter", 2.611, null, false, null, 150, false, "🇺🇸", "A 3.67", null, 3670, 1, "Ford", false, 6, "GT3", Gt3, "7", 7, 2),
    ];

    // ---- Animated simulation (time-driven, loops) ----------------------------------------------
    // Radar / Start Helper / Fuel only have something to show in a live session (a car alongside, a
    // standing start, laps being burned), so the simulation and the Control Center preview animate them.

    /// <summary>Radar: a car closes in from 60 m behind on the LEFT, passes alongside (blind-spot box
    /// lights up) and pulls away; then another does the same on the RIGHT. 14 s loop.</summary>
    public static RadarStatus Radar(double seconds)
    {
        double u = seconds % 14.0;
        bool left = u < 7.0;
        double phase = (left ? u : u - 7.0) / 7.0;          // 0..1 across the pass
        double distance = -18.0 + phase * 36.0;             // -18 m (behind) .. +18 m (ahead)
        bool alongside = Math.Abs(distance) <= RadarSideAssigner.OverlapMeters;
        // Behind and approaching: in the player's lane; alongside and pulling away: on its side.
        var side = distance < -RadarSideAssigner.OverlapMeters ? RadarSide.Center : left ? RadarSide.Left : RadarSide.Right;
        var blips = new List<RadarBlip>
        {
            new(distance, left ? "HIR" : "MIY", side),
            new(-11.0 + 1.5 * Math.Sin(seconds * 0.7), "IWA", RadarSide.Center), // a car sitting in the slipstream
        };
        return new RadarStatus(left && alongside, !left && alongside, blips, true);
    }

    /// <summary>Start helper: clutch held, throttle and RPM build into the target band, the clutch is
    /// released and the RPM overshoots into the critical zone, then it falls back. 10 s loop.</summary>
    public static RaceStartStatus StartHelper(double seconds)
    {
        double u = seconds % 10.0;
        static double Lerp(double a, double b, double t) => a + (b - a) * Math.Clamp(t, 0, 1);
        double clutch, throttle, rpm;
        if (u < 2.0) { clutch = 100; throttle = 0; rpm = 3200; }
        else if (u < 5.0) { double t = (u - 2.0) / 3.0; clutch = 100; throttle = Lerp(0, 85, t); rpm = Lerp(3200, 6400, t); }
        else if (u < 6.5) { double t = (u - 5.0) / 1.5; clutch = 100; throttle = 85; rpm = Lerp(6400, 6700, t); }
        else if (u < 7.5) { double t = u - 6.5; clutch = Lerp(100, 0, t); throttle = Lerp(85, 100, t); rpm = Lerp(6700, 8800, t); }
        else { double t = (u - 7.5) / 2.5; clutch = 0; throttle = Lerp(100, 0, t); rpm = Lerp(8800, 3500, t); }
        return new RaceStartStatus(clutch, throttle, true, rpm);
    }

    /// <summary>Fuel: burns 2.24 L per lap over a 4-lap loop (each lap = 10 s), so the level, laps left
    /// and margin visibly change.</summary>
    public static FuelStatus Fuel(double seconds)
    {
        const double perLap = 2.24, needed = 35.8;
        double laps = (seconds % 40.0) / 10.0;
        double level = 38.5 - perLap * laps;
        return new FuelStatus(FuelLevelLiters: level, FuelUsePerHourLiters: 62.0, AverageFuelPerLapLiters: perLap,
            LapsRemaining: level / perLap, TimeRemainingSeconds: level / perLap * 138, FuelNeededForFinishLiters: needed - level,
            FuelAtFinishLiters: level - needed, LastLapFuelUsedLiters: 2.21, MaxFuelPerLapLiters: 2.30, AverageLapTimeSeconds: 138,
            RaceLapsRemaining: needed / perLap - laps, RaceTotalLaps: 28);
    }

    public static SessionStatus Session() =>
        new("GT3", "RACE", 12, 28, "", "", 3980, 6, "McLaren 720S GT3 EVO");

    public static PlayerCarStatus Player() =>
        new(54.5, "MODERATE", 137.9, 138.0, 29.0);

    public static WeatherStatus Weather() =>
        new(AirTempC: 22, TrackTempC: 29, PrecipitationPct: 0, TrackWetness: 2, WeatherDeclaredWet: false,
            TrackRubberState: "MODERATE", CarPositions: [], WindSpeedMs: 3.3, WindDirectionDeg: 225, RelativeHumidityPct: 62);

    public static FuelStatus Fuel() =>
        new(FuelLevelLiters: 38.5, FuelUsePerHourLiters: 62.0, AverageFuelPerLapLiters: 2.24,
            LapsRemaining: 17.2, TimeRemainingSeconds: 17.2 * 138, FuelNeededForFinishLiters: -2.7,
            FuelAtFinishLiters: 2.7, LastLapFuelUsedLiters: 2.21, MaxFuelPerLapLiters: 2.30, AverageLapTimeSeconds: 138);
}
