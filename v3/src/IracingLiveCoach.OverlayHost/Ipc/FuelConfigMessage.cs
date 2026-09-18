namespace IracingLiveCoach.OverlayHost.Ipc;

/// <summary>Fifth typed/versioned IPC message: Fuel's consumption-source/reserve/pit-exclusion
/// config (spec §9). Source is a string ("LastLap"/"Average"/"Max"/"Manual"), matching this
/// codebase's established pattern of never sharing a compiled enum type across the two processes.</summary>
public sealed record FuelConfigMessage(int SchemaVersion, string Source, double ManualLitersPerLap, double ReserveLaps, bool ExcludePitLaps)
{
    public const int CurrentSchemaVersion = 1;
}
