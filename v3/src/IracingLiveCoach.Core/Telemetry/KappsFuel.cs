namespace IracingLiveCoach.Core.Telemetry;

/// <summary>One consumption basis of Kapps' fuel panel (Average / Qualify / Last).</summary>
public sealed record KappsFuelRow(string Label, double? PerLap, double? LapsRemain, double? Refuel, double? FuelAtEnd);

/// <summary>Kapps' fuel panel: Fuel Level, Laps in Race and the three basis rows.</summary>
public sealed record KappsFuelPanel(double FuelLevel, double? LapsInRace, IReadOnlyList<KappsFuelRow> Rows);

/// <summary>
/// Kapps' fuel calculator, reverse-engineered from synchronized print + SDK samples (24/09/2026, Road Atlanta,
/// 45-min AI race; 4 consecutive laps, Average and Qualify rows):
///  * the numbers are computed when the player crosses the line and stay FROZEN for the lap (two prints of the same
///    lap with 0.8 L of difference in Fuel Level showed identical Laps Remain / Refuel);
///  * Laps Remain = F / rate, F = fuel at the crossing (lap 3: 53.30 / 2.1862 = 24.38, Kapps 24.38);
///  * laps still to drive = ceil(Laps in Race) + <see cref="FinishExtraLaps"/> - laps completed: every sample gave
///    lc + (Refuel + F) / rate = 39.24 (LIR 38.34/38.21/38.33) and 38.24 (LIR 37.87), on both rows;
///  * Refuel = max(0, rate x laps to go - F) -- the pit black box does NOT enter it;
///  * Fuel at End = max(0, F + black-box fuel - rate x laps to go), i.e. what is left at the flag after the planned
///    stop (black box +18.5 .. +25 L gave 0.00 in every sample: all negative).
/// Pure, unit-tested.
/// </summary>
public static class KappsFuel
{
    /// <summary>Empirical: Kapps budgets 0.24 lap beyond the last full lap (39.24 vs ceil 39, four laps, both rows).</summary>
    public const double FinishExtraLaps = 0.24;

    /// <param name="playerFinalLaps">Whole laps the PLAYER will have completed at the flag (lapped-aware, RaceLapEstimator);
    /// null = on the lead lap, i.e. ceil(Laps in Race). Kapps after a pit stop that cost laps: 27.24 to go, not ceil - lc.</param>
    public static double? LapsToGo(double? lapsInRace, int lapCompleted, int? playerFinalLaps = null)
    {
        double? final = playerFinalLaps is int f && f > 0 ? f : lapsInRace is double l && l > 0 ? Math.Ceiling(l - 1e-9) : null;
        return final is double fin ? Math.Max(0, fin + FinishExtraLaps - Math.Max(0, lapCompleted)) : null;
    }

    public static KappsFuelRow Row(string label, double? perLap, double fuelAtLapStart, double? lapsToGo, double plannedAdd)
    {
        if (perLap is not double rate || rate <= 0) return new KappsFuelRow(label, null, null, null, null);
        double lapsRemain = Math.Max(0, fuelAtLapStart) / rate;
        if (lapsToGo is not double togo) return new KappsFuelRow(label, rate, lapsRemain, null, null);
        double need = rate * togo;
        return new KappsFuelRow(label, rate, lapsRemain,
            Math.Max(0, need - fuelAtLapStart),
            Math.Max(0, fuelAtLapStart + Math.Max(0, plannedAdd) - need));
    }
}

/// <summary>Latches Kapps' per-lap values at the player's line crossing; Fuel at End follows the black box live
/// (the driver's requirement: changing the F4 fuel amount must move it).</summary>
public sealed class KappsFuelLatch
{
    private int _lap = int.MinValue;
    private double _fuelAtStart;
    private double? _lapsInRace;
    private int? _playerFinal;
    private double _lastFuel = double.NaN;
    private double? _avg, _qualify, _last;

    /// <param name="lapCompleted">Player's LapCompleted.</param>
    /// <param name="lapsInRace">Current race-length projection (only read when the lap changes, or while none is latched).</param>
    /// <param name="plannedAdd">PitSvFuel when the black box fuel fill is on, else 0.</param>
    public KappsFuelPanel Update(int lapCompleted, double fuelLevel, double? lapsInRace, double? averagePerLap, double? qualifyPerLap, double? lastPerLap, double plannedAdd, int? playerFinalLaps = null)
    {
        // A new lap -- or fuel going UP (refuelled in the pits: Kapps showed 57.84 / 2.199 = 26.30 right after the stop).
        bool refuelled = !double.IsNaN(_lastFuel) && fuelLevel > _lastFuel + 0.3;
        _lastFuel = fuelLevel;
        if (lapCompleted != _lap || refuelled)
        {
            _lap = lapCompleted; _fuelAtStart = fuelLevel; _lapsInRace = lapsInRace; _playerFinal = playerFinalLaps;
            _avg = averagePerLap; _qualify = qualifyPerLap; _last = lastPerLap;
        }
        _playerFinal ??= playerFinalLaps;
        // Values that were not known at the crossing are taken as soon as they appear.
        _lapsInRace ??= lapsInRace; _avg ??= averagePerLap; _qualify ??= qualifyPerLap; _last ??= lastPerLap;
        var togo = KappsFuel.LapsToGo(_lapsInRace, _lap, _playerFinal);
        return new KappsFuelPanel(fuelLevel, _lapsInRace,
        [
            KappsFuel.Row("Average", _avg, _fuelAtStart, togo, plannedAdd),
            KappsFuel.Row("Qualify", _qualify, _fuelAtStart, togo, plannedAdd),
            KappsFuel.Row("Last", _last, _fuelAtStart, togo, plannedAdd),
        ]);
    }

    public void Reset() { _lap = int.MinValue; _lapsInRace = null; _playerFinal = null; _lastFuel = double.NaN; _avg = _qualify = _last = null; }
}
