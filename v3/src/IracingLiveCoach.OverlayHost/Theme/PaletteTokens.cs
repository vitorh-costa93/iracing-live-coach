using System.Globalization;
using System.Collections.Concurrent;
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
    private static readonly ConcurrentDictionary<(int ClassId, int Rank), Color4> SessionClassColors = new();

    /// <summary>User overrides of the normative per-class palette (spec §16: "Permita
    /// personalização... por token, paleta de classe"), keyed by class SHORT NAME (stable across
    /// sessions, unlike <c>classId</c> which <see cref="SessionClassColors"/> uses). Populated from
    /// <c>PlacementPersistence.Load</c>/<c>TryImport</c> -- never guessed at, never applied
    /// speculatively.</summary>
    private static readonly ConcurrentDictionary<string, Color4> NameOverrides = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Registers (or replaces) the color used for every class whose short name matches
    /// <paramref name="className"/>, across every already-resolved <see cref="SessionClassColors"/>
    /// entry for that name too -- spec §16: "Alterar a cor de uma classe atualiza os dois widgets e
    /// todos os seus pilotos atomicamente."</summary>
    public static void SetNameOverride(string className, Color4 color)
    {
        if (string.IsNullOrWhiteSpace(className)) return;
        NameOverrides[className.Trim()] = color;
        InvalidateSessionCache();
    }

    /// <summary>Removes a previously-set override -- the class falls back to the spec §16 normative
    /// palette again on the next resolve.</summary>
    public static void ClearNameOverride(string className)
    {
        NameOverrides.TryRemove(className.Trim(), out _);
        InvalidateSessionCache();
    }

    public static void ClearAllNameOverrides()
    {
        NameOverrides.Clear();
        InvalidateSessionCache();
    }

    /// <summary>Any classId already resolved (and cached) under an overridden name must pick up the
    /// change too, not just future/unresolved classIds. SessionClassColors doesn't retain the name
    /// it was resolved from, so the safest atomic update is to clear the whole cache -- the next
    /// Draw() call re-resolves every classId, which is cheap (a handful of classes per session) and
    /// never wrong -- spec §16: "Alterar a cor de uma classe atualiza os dois widgets e todos os
    /// seus pilotos atomicamente."</summary>
    private static void InvalidateSessionCache()
    {
        foreach (var key in SessionClassColors.Keys.ToList())
            SessionClassColors.TryRemove(key, out _);
    }

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

    // --- Mockup panel look (rounded navy panels, cyan-tinted player row) ---

    /// <summary>Panel body behind the rows.</summary>
    public static readonly Color4 PanelBackground = Hex("#0A1520", 0.90f);

    /// <summary>Panel header band (a shade lighter than the body).</summary>
    public static readonly Color4 PanelHeaderBand = Hex("#10202F", 0.96f);

    /// <summary>Thin panel outline.</summary>
    public static readonly Color4 PanelBorder = Hex("#36586D", 0.95f);

    /// <summary>Vertical dividers between header fields / faint row separators.</summary>
    public static readonly Color4 PanelDivider = Hex("#2F4B5E", 0.85f);

    /// <summary>Player row fill.</summary>
    public static readonly Color4 PlayerRowFill = Hex("#0E4D5A", 0.62f);

    /// <summary>Player row outline.</summary>
    public static readonly Color4 PlayerRowBorder = Hex("#2AD4E8", 0.95f);

    /// <summary>iRating pill outline.</summary>
    public static readonly Color4 PillBorder = Hex("#4A6577", 0.95f);

    /// <summary>iRating pill fill.</summary>
    public static readonly Color4 PillFill = Hex("#08111A", 0.92f);

    /// <summary>Fallback safety-rating pill blue (used when the SDK gives no licence colour).</summary>
    public static readonly Color4 SrPillBlue = Hex("#1E6BFF");

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

    /// <summary>Class colours by SPEED rank within the session (user rule): fastest class yellow,
    /// second light blue, third pink, fourth green. Two classes can therefore never share a colour
    /// (previously a class was coloured by NAME, so e.g. LMP2 and GT3 collided).</summary>
    public static Color4[] ClassRankColors { get; private set; } =
    [
        Hex("#FFD400"), Hex("#5CC8FF"), Hex("#FF6EB4"), Hex("#3DDC84")
    ];

    /// <summary>Replaces the rank colours (fastest class first) from "#RRGGBB" strings; entries that
    /// don't parse keep the current colour. Live: the cache is cleared so the next frame repaints.</summary>
    public static void SetRankColors(IReadOnlyList<string> hexes)
    {
        var next = (Color4[])ClassRankColors.Clone();
        for (int i = 0; i < Math.Min(hexes.Count, next.Length); i++)
            if (TryParseHex(hexes[i], out var color)) next[i] = color;
        ClassRankColors = next;
        InvalidateSessionCache();
    }

    /// <summary>Cores adicionais do catálogo para outras classes, em ordem de atribuição estável.</summary>
    public static readonly Color4[] OtherClassColors =
    [
        Hex("#A78BFA"), Hex("#FF8A3D"), Hex("#2DD4BF"), Hex("#F472B6")
    ];

    /// <summary>Classe não identificada — nunca inferir pela marca.</summary>
    public static readonly Color4 ClassUnidentified = Hex("#73808C");

    /// <summary>Single per-session map used by every table surface. It is keyed first by the
    /// actual SDK class identifier, never by car make or driver order; recognised class names
    /// supply the normative palette, while an SDK colour is accepted only as the stable fallback
    /// for an otherwise unknown class.</summary>
    public static Color4 ResolveClassColor(int classId, string? classShortName, string? sdkColorHex, int classSpeedRank = 0)
    {
        if (classId <= 0) return ClassUnidentified;
        return SessionClassColors.GetOrAdd((classId, classSpeedRank), _ =>
        {
            string rawName = classShortName?.Trim() ?? string.Empty;
            // 1. The user's own colour for this class name always wins (Control Center, "Cores por classe").
            if (rawName.Length > 0 && NameOverrides.TryGetValue(rawName, out var overridden)) return overridden;

            // 2. Otherwise the class's speed rank in THIS session decides (yellow / light blue / pink / green).
            if (classSpeedRank >= 1 && classSpeedRank <= ClassRankColors.Length) return ClassRankColors[classSpeedRank - 1];
            if (classSpeedRank > ClassRankColors.Length) return OtherClassColors[(classSpeedRank - ClassRankColors.Length - 1) % OtherClassColors.Length];

            // 3. Rank unknown (e.g. driver-list not read yet): the SDK's own class colour, else a stable fallback.
            return TryParseHex(sdkColorHex, out var color)
                ? color
                : OtherClassColors[(classId & int.MaxValue) % OtherClassColors.Length];
        });
    }

    private static bool TryParseHex(string? hex, out Color4 color)
    {
        color = ClassUnidentified;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        var value = hex.AsSpan().Trim().TrimStart('#');
        if (value.Length != 6) return false;
        try
        {
            color = new Color4(
                byte.Parse(value[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255f,
                byte.Parse(value.Slice(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255f,
                byte.Parse(value.Slice(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255f,
                1f);
            return true;
        }
        catch (FormatException) { return false; }
    }

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
    public static readonly Color4 BarTrackEmpty = Hex("#1A3141");

    /// <summary>Borda das barras — grade externa continua uniforme.</summary>
    public static readonly Color4 BarBorder = Hex("#536777");

    /// <summary>OT disponível — saldo em segundos + barra azul-clara (mockup Suzuka).</summary>
    public static readonly Color4 OvertakeAvailable = Hex("#9AD3FE");

    /// <summary>OT acionado — saldo em segundos + barra verde.</summary>
    public static readonly Color4 OvertakeActive = Hex("#00E600");

    /// <summary>OT bloqueado/cooldown — saldo em segundos + barra âmbar.</summary>
    public static readonly Color4 OvertakeCooldown = Hex("#FBCD08");

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
