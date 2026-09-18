namespace IracingLiveCoach.Core.Telemetry;

/// <summary>Spec §9: "qual consumo que vai ditar a margem" -- which figure drives the displayed
/// Autonomy/Time Left. "TO FINISH" always uses <see cref="FuelStatus.FuelNeededForFinishLiters"/>
/// (the moving-average-based estimate computed alongside the rest of <see cref="FuelStatus"/>)
/// regardless of this choice -- recomputing race-distance fuel need from an arbitrary per-lap figure
/// would need the remaining-laps count the presentation layer doesn't have, so that field stays
/// honestly scoped to what's actually computed here. Lives alongside <see cref="StandingsSelection"/>'s
/// own presentation-config records (same precedent: a UI-facing config record for a Core-computed
/// figure, not itself something the widget layer's unsafe/GPU code should have to define).</summary>
public enum FuelConsumptionSource { LastLap, Average, Max, Manual }

/// <param name="ReserveLaps">Subtracted from the raw Autonomy figure -- spec §9's "margem em...
/// voltas" (liters-based margin isn't offered separately since laps is the more legible unit here).</param>
/// <param name="ExcludePitLaps">If the most recent lap was pit-affected and Source is LastLap, fall
/// back to the average instead of showing a wildly-off in/out-lap number.</param>
public sealed record FuelConfig(FuelConsumptionSource Source, double ManualLitersPerLap, double ReserveLaps, bool ExcludePitLaps)
{
    public static FuelConfig Default { get; } = new(FuelConsumptionSource.Average, 0, 1.0, true);
}
