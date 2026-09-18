namespace IracingLiveCoach.OverlayHost.Ipc;

/// <summary>Ninth typed/versioned IPC message, three actions on one channel (spec §12: "perfis por
/// carro/classe e visibilidade por tipo de sessão"): "setSessionVisibility" (HiddenIn = widget key
/// -> session kinds it is hidden in), "saveClassProfile" (snapshot the overlay's own LIVE layout
/// under Key -- the overlay owns the truth, so the Control Center never ships placements), and
/// "deleteClassProfile".</summary>
public sealed record ProfilesMessage(int SchemaVersion, string Action, string Key, Dictionary<string, List<string>>? HiddenIn)
{
    public const int CurrentSchemaVersion = 1;
}
