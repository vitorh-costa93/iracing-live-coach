namespace IracingLiveCoach.OverlayHost.Ipc;

/// <summary>Seventh typed/versioned IPC message: iRating/Safety Rating/driver-name display format
/// (spec §12), global across Standings/Relative. Fields are strings, matching this codebase's
/// established pattern of never sharing a compiled enum across the two processes.</summary>
public sealed record NumberFormatMessage(int SchemaVersion, string IRatingFormat, string SafetyRatingFormat, string NameFormat, bool ShowIRatingDelta = true)
{
    public const int CurrentSchemaVersion = 1;
}
