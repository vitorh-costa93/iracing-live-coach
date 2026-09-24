namespace IracingLiveCoach.Core.Telemetry;

/// <summary>
/// Kapps' "Laps Remain": fuel autonomy counted from the START of the lap in progress, i.e.
/// FuelLevel / litersPerLap + LapDistPct -- the lap number (from the line) at which the tank runs dry.
/// Verified on three Kapps prints paired with SDK reads (Watkins Glen 24/09/2026):
///   qualy  46.34 / 3.26 = 14.21 + pct ~0.09  = 14.31 (Kapps 14.31)
///   race   45.40 / 3.26 = 13.93 + pct 0.215 = 14.14 (Kapps 14.14)
///   race   20.34 / 3.26 =  6.24 + pct 0.912 =  7.16 (Kapps 7.16)
/// Pure and unit-tested.
/// </summary>
public static class FuelLapsRemain
{
    /// <param name="lapDistPct">Player's LapDistPct; outside [0, 1) (not on track) counts as 0.</param>
    public static double? Compute(double fuelLevel, double? litersPerLap, double? lapDistPct) =>
        litersPerLap is double perLap && perLap > 0
            ? Math.Max(0, fuelLevel) / perLap + (lapDistPct is double pct && pct >= 0 && pct < 1 ? pct : 0)
            : null;
}
