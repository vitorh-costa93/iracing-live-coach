using IracingLiveCoach.Core.Telemetry;

namespace IracingLiveCoach.OverlayHost.Widgets;

/// <summary>
/// The spec §12 simulation preset, shared by the overlay's own simulation mode (T key) and the
/// Control Center's preview so both always show the same thing. It reproduces the Spa mockup
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
        new(1, "Antoine Moreau", 12, 122.416, null, false, "🇫🇷", "A 4.12", null, 6240, 2, "Ferrari", null, 42, -0.648, "GTP", Gtp, 1, null, null, null, null, false, "L8 24s", "6", 1),
        new(2, "Oliver Wilson", 12, 137.810, null, false, "🇬🇧", "A 4.10", null, 4390, 1, "Aston Martin", 0, 28, -0.216, "GT3", Gt3, 1, null, null, null, null, false, "L8 24s", "27", 2),
        new(3, "Max Hoffmann", 12, 138.144, null, false, "🇩🇪", "A 3.72", null, 4210, 1, "BMW", 3.816, -8, 0.118, "GT3", Gt3, 2, 3.816, null, null, null, false, "L8 24s", "18", 2),
        new(4, "Vitor Costa", 12, 138.026, null, true, "🇧🇷", "A 3.49", null, 3694, 1, "McLaren", 5.0, 12, 0.0, "GT3", Gt3, 3, 1.182, null, null, null, false, "L8 24s", "93", 2),
        new(5, "Daniel Walker", 12, 138.330, null, false, "🇺🇸", "A 3.65", null, 4085, 1, "Porsche", 6.4, 5, 0.304, "GT3", Gt3, 4, 1.368, null, null, null, false, "L8 24s", "44", 2),
        new(6, "Simon Wagner", 12, 138.420, null, false, "🇩🇪", "A 4.01", null, 4320, 1, "Mercedes", 7.1, -3, 0.394, "GT3", Gt3, 5, 0.715, null, null, null, false, "L8 24s", "11", 2),
    ];

    public static List<RelativeRow> RelativeRows() =>
    [
        new(-3, "Antoine Moreau", -3.922, null, null, null, null, false, "🇫🇷", "A 4.12", null, 6240, 2, "Ferrari", false, 1, "GTP", Gtp, "6", 1, 1),
        new(-2, "Oliver Wilson", -2.537, null, null, null, null, false, "🇬🇧", "A 4.10", null, 4390, 1, "Aston Martin", false, 1, "GT3", Gt3, "27", 2, 2),
        new(-1, "Max Hoffmann", -1.182, null, null, null, null, false, "🇩🇪", "A 3.72", null, 4210, 1, "BMW", false, 2, "GT3", Gt3, "18", 3, 2),
        new(0, "Vitor Costa", 0, null, null, null, null, false, "🇧🇷", "A 3.49", null, 3694, 1, "McLaren", true, 3, "GT3", Gt3, "93", 4, 2),
        new(1, "Daniel Walker", 1.386, null, null, null, null, false, "🇺🇸", "A 3.65", null, 4085, 1, "Porsche", false, 4, "GT3", Gt3, "44", 5, 2),
        new(2, "Simon Wagner", 2.083, null, null, null, null, false, "🇩🇪", "A 4.01", null, 4320, 1, "Mercedes", false, 5, "GT3", Gt3, "11", 6, 2),
        new(3, "James Carter", 2.611, null, null, null, null, false, "🇺🇸", "A 3.67", null, 3670, 1, "Ford", false, 6, "GT3", Gt3, "7", 7, 2),
    ];

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
