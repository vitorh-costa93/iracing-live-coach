namespace IracingLiveCoach.Core.Telemetry;

/// <summary>
/// Point-in-time capability/validity summary for the current telemetry state -- per spec section 1
/// ("documente capacidades reais por carro e sessão... inclusive dados ausentes, desatualizados e
/// derivados"). This does NOT replace <see cref="TelemetryReader"/>'s existing per-widget events
/// (StandingsUpdated, FuelUpdated, etc.) -- those remain the source of truth for widget data. This
/// snapshot exists so the render layer (Phase 2+) can decide, independent of any single widget's
/// last event, whether the data it is about to draw is fresh, stale, or simply not available yet,
/// without guessing from a widget-specific null.
/// </summary>
/// <param name="CapturedAtUtc">When this snapshot was taken.</param>
/// <param name="HasRecentTelemetry">Mirrors <see cref="TelemetryReader.HasRecentTelemetry"/> -- true while the sim is actively supplying frames.</param>
/// <param name="SessionDetected">Whether a session (of any type) has been identified yet.</param>
/// <param name="IsRaceSession">Whether the current session is specifically a Race (affects e.g. Start Helper visibility rules).</param>
/// <param name="PlayerCarIdx">The player's own CarIdx, or null if not yet detected.</param>
public sealed record TelemetrySnapshot(
    DateTime CapturedAtUtc,
    bool HasRecentTelemetry,
    bool SessionDetected,
    bool IsRaceSession,
    int? PlayerCarIdx)
{
    /// <summary>A snapshot representing "no telemetry has ever been captured" -- distinct from a stale
    /// snapshot, so callers can tell "never connected" apart from "was connected, now disconnected".</summary>
    public static TelemetrySnapshot NeverCaptured { get; } = new(DateTime.MinValue, false, false, false, null);
}
