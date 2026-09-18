namespace IracingLiveCoach.OverlayHost.Ipc;

/// <summary>Seventh typed/versioned IPC message: spec §4's "desfazer/refazer mudanças de layout"
/// exposed from the Control Center. Action is "undo" or "redo" -- a string, matching this
/// codebase's established pattern of never sharing a compiled enum across the two processes.</summary>
public sealed record UndoRedoMessage(int SchemaVersion, string Action)
{
    public const int CurrentSchemaVersion = 1;
}
