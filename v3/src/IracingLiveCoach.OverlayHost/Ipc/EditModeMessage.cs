namespace IracingLiveCoach.OverlayHost.Ipc;

/// <summary>
/// Second typed/versioned message on its own named pipe (spec §3), separate from
/// <see cref="PlacementMessage"/> because it's a different concern: a global lock/unlock toggle, not
/// a per-widget placement edit. Lets the Control Center remotely flip the same edit mode the "E" key
/// already toggles on the overlay itself, so positioning widgets doesn't require alt-tabbing into the
/// game window to press a key.
/// </summary>
public sealed record EditModeMessage(int SchemaVersion, bool Enabled)
{
    public const int CurrentSchemaVersion = 1;
}
