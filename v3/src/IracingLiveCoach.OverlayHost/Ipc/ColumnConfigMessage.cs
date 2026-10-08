namespace IracingLiveCoach.OverlayHost.Ipc;

/// <summary>One column's user-editable fields (spec §12: "ativar/ocultar, reordenar... largura
/// individual, largura mínima, modo fixo/flexível... alinhamento... casas decimais"). Deliberately a
/// flat DTO, independent of <see cref="Layout.ColumnDefinition"/>, matching this codebase's existing
/// pattern of never sharing a compiled wire-contract type between the two processes.</summary>
public sealed record ColumnConfigEntry(
    string Key, bool Visible, int Order, float WidthPx, float MinWidthPx,
    string WidthMode, string Alignment, int? DecimalPlaces,
    float PaddingLeftPx, float PaddingRightPx,
    string? FontFamily = null, int? FontWeight = null, int? LapWindow = null, bool ShowInPractice = true, bool ShowInQualify = true, bool ShowInRace = true);

/// <summary>Third typed/versioned IPC message (alongside Placement and EditMode), scoped to one
/// widget's full column set at a time -- sent whenever the Control Center's Colunas tab applies a
/// change, spec §3's "aplicação de configurações sem reiniciar a corrida".</summary>
public sealed record ColumnConfigMessage(int SchemaVersion, string Widget, List<ColumnConfigEntry> Columns)
{
    public const int CurrentSchemaVersion = 2; // v2: optional per-column FontFamily/FontWeight; v1 messages are still accepted
}
