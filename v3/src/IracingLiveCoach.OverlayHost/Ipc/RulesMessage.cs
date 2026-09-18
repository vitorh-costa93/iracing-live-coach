namespace IracingLiveCoach.OverlayHost.Ipc;

/// <summary>Fourth typed/versioned IPC message: Standings' selection rules (spec §6/§12 "Top N,
/// linhas por classe e janela do jogador"). Scoped to Standings only -- Relative's row window comes
/// pre-shaped from TelemetryReader itself, not a widget-side selection policy like this one.</summary>
public sealed record RulesMessage(int SchemaVersion, int TopNPerClass, int OwnClassRows, int OtherClassRows, bool KeepPlayerWindow, int RelativeAhead = 3, int RelativeBehind = 3, bool TopNCountsTowardTotal = true)
{
    public const int CurrentSchemaVersion = 1;
}
