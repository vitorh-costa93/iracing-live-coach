namespace IracingLiveCoach.OverlayHost.Ipc;

/// <summary>Eighth typed/versioned IPC message: iRating/Safety Rating display format (spec §12),
/// global across Standings/Relative. IRatingFormat/SafetyRatingFormat are strings, matching this
/// codebase's established pattern of never sharing a compiled enum across the two processes.</summary>
public sealed record NumberFormatMessage(int SchemaVersion, string IRatingFormat, string SafetyRatingFormat)
{
    public const int CurrentSchemaVersion = 1;
}
