namespace IracingLiveCoach.OverlayHost.Ipc;

public sealed record HeaderFieldEntry(string Key, bool Visible);

/// <summary>Configurable header fields for one widget (spec §12). List order is display order.</summary>
public sealed record HeaderConfigMessage(int SchemaVersion, string Widget, List<HeaderFieldEntry> Fields)
{
    public const int CurrentSchemaVersion = 1;
}
