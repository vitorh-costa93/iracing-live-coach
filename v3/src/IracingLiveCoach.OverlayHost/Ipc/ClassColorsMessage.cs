namespace IracingLiveCoach.OverlayHost.Ipc;

/// <summary>Class colour configuration (spec §16): the four speed-rank colours (fastest first) and
/// the per-class-NAME overrides. Applied live -- this used to need an overlay restart.</summary>
public sealed record ClassColorsMessage(int SchemaVersion, List<string> RankColors, Dictionary<string, string> NameOverrides)
{
    public const int CurrentSchemaVersion = 1;
}
