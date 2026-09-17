namespace IracingLiveCoach.OverlayHost.Ipc;

/// <summary>
/// The wire contract between the Control Center and the OverlayHost (spec §3: "comunicação local
/// tipada e versionada entre processos"). Deliberately a plain, independently-defined DTO on each
/// side (this file's twin lives in the Control Center project) rather than a shared assembly
/// reference -- the two processes stay decoupled, and <see cref="SchemaVersion"/> is the explicit
/// versioning contract: a receiver that sees a version it doesn't understand should ignore the
/// message rather than guess at unknown fields.
/// </summary>
public sealed record PlacementMessage(
    int SchemaVersion,
    string Widget,
    float X,
    float Y,
    float WidthDip,
    float HeightDip,
    float Scale,
    bool Locked,
    bool Visible,
    float Opacity)
{
    public const int CurrentSchemaVersion = 1;
}
