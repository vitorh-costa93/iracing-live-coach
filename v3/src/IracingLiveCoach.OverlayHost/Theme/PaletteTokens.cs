using System.Globalization;
using Vortice.Win32.Numerics;

namespace IracingLiveCoach.OverlayHost.Theme;

/// <summary>
/// Every HEX value from spec §16, as named constants. Nothing in this codebase should have a
/// hardcoded color literal outside this file (Phase 0's inline white/cyan `Color4` values in
/// <c>DeviceResources.RenderFrame</c> predate this file and are Phase 3's job to replace once a
/// real widget consumes them, not deleted here — Phase 0 was throwaway-labeled proof code).
/// Alpha is a separate parameter from the HEX (spec §16: "alpha se aplica somente à superfície
/// indicada; não reduza a opacidade do texto por herança") — every token below takes the spec's
/// own default alpha for that token, not a blanket 100%.
/// </summary>
public static class PaletteTokens
{
    private static Color4 Hex(string hex, float alpha = 1f)
    {
        var span = hex.AsSpan().TrimStart('#');
        byte r = byte.Parse(span[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        byte g = byte.Parse(span.Slice(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        byte b = byte.Parse(span.Slice(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new Color4(r / 255f, g / 255f, b / 255f, alpha);
    }

    // --- Superfícies, texto e controles (spec §16, first table) ---

    /// <summary>Fundo dos overlays.</summary>
    public static readonly Color4 OverlayBackground = Hex("#101820", 0.88f);

    /// <summary>Faixa do cabeçalho de sessão.</summary>
    public static readonly Color4 SessionHeaderBand = Hex("#0B1219", 0.94f);

    /// <summary>Fundo de badge iRating.</summary>
    public static readonly Color4 IRatingBadgeBackground = Hex("#17232E");

    /// <summary>Borda do badge neutro.</summary>
    public static readonly Color4 NeutralBadgeBorder = Hex("#536777");

    /// <summary>Borda externa do widget.</summary>
    public static readonly Color4 WidgetOuterBorder = Hex("#405A6B", 0.85f);

    /// <summary>Grade horizontal/vertical.</summary>
    public static readonly Color4 Grid = Hex("#2A3D4B", 0.70f);

    /// <summary>Texto principal / números.</summary>
    public static readonly Color4 TextPrimary = Hex("#F2F5F7");

    /// <summary>Texto secundário / unidades / número do carro.</summary>
    public static readonly Color4 TextSecondary = Hex("#A6B0BB");

    /// <summary>Texto desabilitado / dados ausentes.</summary>
    public static readonly Color4 TextDisabled = Hex("#73808C");

    /// <summary>Texto sobre fundo claro/amarelo.</summary>
    public static readonly Color4 TextOnLight = Hex("#081018");

    /// <summary>Destaque do jogador: texto/borda.</summary>
    public static readonly Color4 PlayerHighlight = Hex("#00C9E8");

    /// <summary>Fundo da linha do jogador.</summary>
    public static readonly Color4 PlayerRowBackground = Hex("#00C9E8", 0.18f);

    /// <summary>Fundo do Control Center.</summary>
    public static readonly Color4 ControlCenterBackground = Hex("#0B1118");

    /// <summary>Superfície do painel / sidebar.</summary>
    public static readonly Color4 PanelSurface = Hex("#121D27");

    /// <summary>Campo de formulário.</summary>
    public static readonly Color4 FormFieldBackground = Hex("#17232E");

    /// <summary>Borda de campo.</summary>
    public static readonly Color4 FieldBorder = Hex("#405A6B");

    /// <summary>Hover de controle.</summary>
    public static readonly Color4 ControlHover = Hex("#203342");

    /// <summary>Seleção de menu / aba.</summary>
    public static readonly Color4 MenuTabSelection = Hex("#173C52");

    /// <summary>Ação primária / switch ligado.</summary>
    public static readonly Color4 PrimaryAction = Hex("#008BFF");

    /// <summary>Hover de ação primária.</summary>
    public static readonly Color4 PrimaryActionHover = Hex("#0075D6");

    /// <summary>Switch desligado.</summary>
    public static readonly Color4 SwitchOff = Hex("#40515F");

    /// <summary>Botão circular do switch.</summary>
    public static readonly Color4 SwitchKnob = Hex("#F2F5F7");

    /// <summary>Foco de teclado / alças de edição.</summary>
    public static readonly Color4 FocusOutline = Hex("#00C9E8");

    /// <summary>Fundo de tooltip / mensagem.</summary>
    public static readonly Color4 TooltipBackground = Hex("#17232E");

    // Structural constants from the same section (not colors, but centralized here per the same
    // "don't scatter values" rule):
    public const float WidgetCornerRadiusPx = 4f;
    public const float BadgeCornerRadiusPx = 3f;
    public const float BorderAndGridThicknessPx = 1f;

    // --- Classes, licenças e semântica (spec §16, second table) ---

    /// <summary>Classe GTP — cabeçalho e TODAS as faixas GTP.</summary>
    public static readonly Color4 ClassGtp = Hex("#FF3038");

    /// <summary>Classe GT3 — cabeçalho e TODAS as faixas GT3.</summary>
    public static readonly Color4 ClassGt3 = Hex("#FFD400");

    /// <summary>Classe SF23 — cabeçalho e TODAS as faixas SF23 (shares GTP's red by design; see spec §16).</summary>
    public static readonly Color4 ClassSf23 = Hex("#FF3038");

    /// <summary>Classe LMP2, se presente.</summary>
    public static readonly Color4 ClassLmp2 = Hex("#5B8CFF");

    /// <summary>Cores adicionais do catálogo para outras classes, em ordem de atribuição estável.</summary>
    public static readonly Color4[] OtherClassColors =
    [
        Hex("#A78BFA"), Hex("#FF8A3D"), Hex("#2DD4BF"), Hex("#F472B6")
    ];

    /// <summary>Classe não identificada — nunca inferir pela marca.</summary>
    public static readonly Color4 ClassUnidentified = Hex("#73808C");

    /// <summary>Licença Rookie (texto #F2F5F7 — ver <see cref="TextPrimary"/>).</summary>
    public static readonly Color4 LicenseRookie = Hex("#B91C1C");

    /// <summary>Licença D (texto #081018 — ver <see cref="TextOnLight"/>).</summary>
    public static readonly Color4 LicenseD = Hex("#F28C28");

    /// <summary>Licença C (texto #081018 — ver <see cref="TextOnLight"/>).</summary>
    public static readonly Color4 LicenseC = Hex("#FFD400");

    /// <summary>Licença B (texto #F2F5F7 — ver <see cref="TextPrimary"/>).</summary>
    public static readonly Color4 LicenseB = Hex("#168A45");

    /// <summary>Licença A (texto #F2F5F7 — ver <see cref="TextPrimary"/>).</summary>
    public static readonly Color4 LicenseA = Hex("#0057D9");

    /// <summary>Licença Pro, quando aplicável (texto #F2F5F7, borda #A6B0BB — ver <see cref="TextPrimary"/>/<see cref="TextSecondary"/>).</summary>
    public static readonly Color4 LicensePro = Hex("#1B1B1B");

    /// <summary>Licença desconhecida (texto #F2F5F7 — ver <see cref="TextPrimary"/>).</summary>
    public static readonly Color4 LicenseUnknown = Hex("#40515F");

    /// <summary>Ganho de iRating / margem positiva — sempre acompanhado de sinal/valor.</summary>
    public static readonly Color4 PositiveDelta = Hex("#32D583");

    /// <summary>Perda de iRating / margem negativa — sempre acompanhado de sinal/valor.</summary>
    public static readonly Color4 NegativeDelta = Hex("#FF5252");

    /// <summary>Delta de última volta mais rápido (delta negativo contra o jogador).</summary>
    public static readonly Color4 LapDeltaFaster = Hex("#32D583");

    /// <summary>Delta de última volta mais lento (delta positivo contra o jogador).</summary>
    public static readonly Color4 LapDeltaSlower = Hex("#FF5252");

    /// <summary>Delta zero / gap normal — cor neutra.</summary>
    public static readonly Color4 NeutralDeltaOrGap = Hex("#F2F5F7");

    /// <summary>Melhor volta da sessão — apenas com referência válida.</summary>
    public static readonly Color4 SessionBestLap = Hex("#C084FC");

    /// <summary>Alerta / baixo combustível / radar ocupado.</summary>
    public static readonly Color4 Warning = Hex("#FFCC00");

    /// <summary>Crítico / radar em situação crítica validada — não inferir severidade sem dados.</summary>
    public static readonly Color4 Critical = Hex("#FF5252");

    /// <summary>Informação / referência do Start Helper — não confundir com <see cref="Warning"/>.</summary>
    public static readonly Color4 Info = Hex("#00C9E8");

    /// <summary>Start Helper dentro da faixa (conforme alvo calibrado).</summary>
    public static readonly Color4 StartHelperInRange = Hex("#32D583");

    /// <summary>Start Helper fora da faixa (limites configurados).</summary>
    public static readonly Color4 StartHelperOutOfRange = Hex("#FFCC00");

    /// <summary>Start Helper limite crítico (somente regra validada).</summary>
    public static readonly Color4 StartHelperCritical = Hex("#FF5252");

    /// <summary>Trilho vazio de barras (OT etc.) — não implica saldo válido.</summary>
    public static readonly Color4 BarTrackEmpty = Hex("#253440");

    /// <summary>Borda das barras — grade externa continua uniforme.</summary>
    public static readonly Color4 BarBorder = Hex("#536777");

    /// <summary>OT disponível — saldo em segundos + barra prata.</summary>
    public static readonly Color4 OvertakeAvailable = Hex("#B8C4D0");

    /// <summary>OT acionado — saldo em segundos + barra verde.</summary>
    public static readonly Color4 OvertakeActive = Hex("#22E66B");

    /// <summary>OT bloqueado/cooldown — saldo em segundos + barra âmbar.</summary>
    public static readonly Color4 OvertakeCooldown = Hex("#FFBF00");

    /// <summary>OT esgotado — 0 s, trilho vazio.</summary>
    public static readonly Color4 OvertakeDepleted = Hex("#73808C");

    /// <summary>OT desconhecido/não suportado — travessão, sem saldo fictício.</summary>
    public static readonly Color4 OvertakeUnknown = Hex("#73808C");

    /// <summary>Tempo seco / ícone de sol — track wetness mantém rótulo explícito.</summary>
    public static readonly Color4 WeatherDry = Hex("#FFD400");

    /// <summary>Chuva / molhado / ícone de água — não transformar condição em previsão.</summary>
    public static readonly Color4 WeatherWet = Hex("#38BDF8");

    /// <summary>Bandeira verde — mostrar estado verdadeiro.</summary>
    public static readonly Color4 FlagGreen = Hex("#22C55E");

    /// <summary>Bandeira amarela — mostrar estado verdadeiro.</summary>
    public static readonly Color4 FlagYellow = Hex("#FFD400");

    /// <summary>Bandeira vermelha — mostrar estado verdadeiro.</summary>
    public static readonly Color4 FlagRed = Hex("#FF3038");

    /// <summary>Bandeira azul — mostrar estado verdadeiro.</summary>
    public static readonly Color4 FlagBlue = Hex("#3B82F6");

    /// <summary>Bandeira branca — mostrar estado verdadeiro.</summary>
    public static readonly Color4 FlagWhite = Hex("#F2F5F7");

    /// <summary>Bandeira preta — borda clara para contraste.</summary>
    public static readonly Color4 FlagBlack = Hex("#101010");

    /// <summary>Quadriculada — padrão bicolor (#F2F5F7 + #101010), nunca uma cor única.</summary>
    public static readonly (Color4 Light, Color4 Dark) FlagCheckered = (Hex("#F2F5F7"), Hex("#101010"));
}
