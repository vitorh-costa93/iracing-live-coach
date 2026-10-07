using System.Text.Json.Serialization;

namespace Ams2.Shared.Profiles;

/// <summary>Escala de fonte relativa ao tamanho geral; peso nulo herda o peso geral ou do tema.</summary>
public sealed record ElementFontSettings
{
    public float? Scale { get; init; }
    public int? Weight { get; init; }
}

/// <summary>Configuracao de um widget (uma janela do overlay). Nulos em Font/Rows/Columns = padrao do tema/widget.</summary>
public sealed record WidgetSettings
{
    public string Id { get; init; } = "";
    public bool Visible { get; init; } = true;
    public int X { get; init; }
    public int Y { get; init; }
    public float Scale { get; init; } = 1f;
    public float Opacity { get; init; } = 1f;
    public int Order { get; init; }
    /// <summary>Familia de fonte que substitui as fontes de texto do tema (os numeros do tema nao mudam).</summary>
    public string? Font { get; init; }
    /// <summary>Linhas visiveis (Standings; Relative = por lado). Null = padrao.</summary>
    public int? Rows { get; init; }
    /// <summary>Ids das colunas visiveis. Null = todas.</summary>
    public string[]? Columns { get; init; }
    /// <summary>Standings: pilotos mostrados no TOPO da classificacao. Null = padrao (5).</summary>
    public int? TopCount { get; init; }
    /// <summary>Standings: pilotos AO REDOR do jogador (a janela inclui o jogador). Null = padrao (3).</summary>
    public int? NearCount { get; init; }
    /// <summary>Radar: alcance em metros (frente e tras). Null = padrao (15).</summary>
    public int? RadarRange { get; init; }
    /// <summary>Radar: sensibilidade 1..5 (janelas de aviso). Null = padrao (3).</summary>
    public int? RadarSensitivity { get; init; }
    /// <summary>Largura de cada coluna dimensionavel (<see cref="WidgetDef.Widths"/>), em % da largura do tema (100 = padrao). Null = tudo padrao.</summary>
    public Dictionary<string, int>? ColumnWidths { get; init; }
    /// <summary>Tamanho do texto (1 = tema). Altera apenas as fontes, preservando a janela.</summary>
    public float? TextScale { get; init; }
    /// <summary>Fator independente da largura; 1 volta ao padrao.</summary>
    public float? WidthScale { get; init; }
    /// <summary>Fator independente da altura; 1 volta ao padrao.</summary>
    public float? HeightScale { get; init; }
    /// <summary>Fontes por elemento semantico. No patch substitui o mapa inteiro; vazio restaura todos.</summary>
    public Dictionary<string, ElementFontSettings>? ElementFonts { get; init; }
    /// <summary>Peso da fonte dos textos (100-900). Null = o do tema.</summary>
    public int? FontWeight { get; init; }
    /// <summary>Cores "#RRGGBB" que substituem as do tema: textos/titulos, rotulos e valores. Null = tema.</summary>
    public string? TextColor { get; init; }
    public string? LabelColor { get; init; }
    public string? ValueColor { get; init; }
    /// <summary>Formato de nomes, gaps, tempos e unidades. Null = padrao do widget.</summary>
    public DisplayOptions? Display { get; init; }
    /// <summary>Opcoes proprias do tema (<see cref="WidgetCatalog.OptionsFor"/>): id -> valor. Ausente = padrao da <see cref="OptionDef"/>. Null = tudo padrao.</summary>
    public Dictionary<string, string>? Options { get; init; }
    /// <summary>Grupos de sessao em que o widget aparece (<see cref="SessionIds"/>: "practice", "qualify", "race"). Null = padrao do widget
    /// (<see cref="WidgetDef.Sessions"/>); a normalizacao grava null quando igual ao padrao ou vazio (sem bloco no JSON).</summary>
    public string[]? Sessions { get; init; }

    /// <summary>Grupos efetivos (nunca vazio): os gravados ou o padrao do widget.</summary>
    [JsonIgnore] public IReadOnlyList<string> EffectiveSessions => Sessions is { Length: > 0 } s ? s : WidgetCatalog.Find(Id)?.Sessions ?? SessionIds.All;

    /// <summary>O widget aparece no grupo de sessao <paramref name="session"/>? Null (sem sessao / tipo invalido) = nao filtra.</summary>
    public bool ShowsIn(string? session) => session is null || EffectiveSessions.Contains(session, StringComparer.OrdinalIgnoreCase);

    /// <summary>Valor gravado da opcao do tema (null = padrao; use <see cref="OptionOr"/> ou o Default da <see cref="OptionDef"/>).</summary>
    public string? Option(string id) => Options is not null && Options.TryGetValue(id, out var v) ? v : null;
    /// <summary>Valor gravado da opcao ou <paramref name="fallback"/>.</summary>
    public string OptionOr(string id, string fallback) => Option(id) ?? fallback;

    /// <summary>Escala uniforme da janela; fontes e eixos sao independentes.</summary>
    [JsonIgnore] public float RenderScale => Scale;
    [JsonIgnore] public float ScaleX => Scale * (WidthScale ?? 1f);
    [JsonIgnore] public float ScaleY => Scale * (HeightScale ?? 1f);
    /// <summary>Formato efetivo (nunca nulo).</summary>
    [JsonIgnore] public DisplayOptions Fmt => Display ?? DisplayOptions.Empty;
    /// <summary>Fator (0,5..2,5) de largura da coluna; 1 quando nao configurada.</summary>
    public float WidthFactor(string column) => ColumnWidths is not null && ColumnWidths.TryGetValue(column, out var pct) ? pct / 100f : 1f;
    /// <summary>Largura da coluna: <paramref name="baseWidth"/> do tema x fator configurado.</summary>
    public float Width(string column, float baseWidth) => baseWidth * WidthFactor(column);

    /// <summary>Topo efetivo (padrao quando ausente, como em perfis antigos).</summary>
    [JsonIgnore] public int EffectiveTop => TopCount ?? WidgetCatalog.DefaultTopCount;
    [JsonIgnore] public int EffectiveNear => NearCount ?? WidgetCatalog.DefaultNearCount;
    [JsonIgnore] public int EffectiveRadarRange => RadarRange ?? WidgetCatalog.DefaultRadarRange;
    [JsonIgnore] public int EffectiveRadarSensitivity => RadarSensitivity ?? WidgetCatalog.DefaultRadarSensitivity;

    public bool ColumnVisible(string column) => Columns is null || Columns.Contains(column, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Forca limites validos (escala, opacidade, linhas, colunas conhecidas). Com <paramref name="themeId"/>, as <see cref="Options"/> sao
    /// conferidas com as <see cref="OptionDef"/> do tema (chaves desconhecidas, valores invalidos e valores iguais ao padrao saem);
    /// sem tema so as entradas vazias saem (o tema nao e conhecido aqui).
    /// </summary>
    public WidgetSettings Normalized(string? themeId = null)
    {
        var def = WidgetCatalog.Find(Id, themeId);
        int? rows = null;
        if (def is { SupportsRows: true }) rows = Math.Clamp(Rows ?? def.DefaultRows!.Value, def.MinRows!.Value, def.MaxRows!.Value);
        string[]? cols = Columns;
        if (cols is not null && def is not null)
            cols = cols.Where(c => def.Columns.Any(d => string.Equals(d.Id, c, StringComparison.OrdinalIgnoreCase))).Distinct().ToArray();
        int? top = null, near = null;
        if (def is { HasSelection: true })
        {
            top = Math.Clamp(EffectiveTop, 0, WidgetCatalog.MaxTopCount);
            near = Math.Clamp(EffectiveNear, 0, WidgetCatalog.MaxNearCount);
            if (top + near == 0) near = 1; // o jogador sempre aparece
        }
        bool radar = def is { HasRadarOptions: true };
        Dictionary<string, int>? widths = null;
        if (ColumnWidths is not null && def is not null)
        {
            foreach (var (k, v) in ColumnWidths)
            {
                var known = def.Widths.FirstOrDefault(d => string.Equals(d.Id, k, StringComparison.OrdinalIgnoreCase));
                int pct = Math.Clamp(v, WidgetCatalog.MinWidthPct, WidgetCatalog.MaxWidthPct);
                if (known is null || pct == 100) continue;
                (widths ??= new(StringComparer.OrdinalIgnoreCase))[known.Id] = pct;
            }
        }
        float? textScale = TextScale is { } ts && float.IsFinite(ts) ? MathF.Round(Math.Clamp(ts, WidgetCatalog.MinTextScale, WidgetCatalog.MaxTextScale), 2) : null;
        if (textScale is 1f) textScale = null;
        return this with
        {
            ColumnWidths = widths,
            WidthScale = NormalizeFactor(WidthScale, WidgetCatalog.MinAxisScale, WidgetCatalog.MaxAxisScale),
            HeightScale = NormalizeFactor(HeightScale, WidgetCatalog.MinAxisScale, WidgetCatalog.MaxAxisScale),
            ElementFonts = NormalizeElementFonts(),
            TextScale = textScale,
            FontWeight = FontWeight is { } fw and > 0 ? Math.Clamp((int)Math.Round(fw / 100.0) * 100, 100, 900) : null,
            TextColor = ColorHex.Normalize(TextColor),
            LabelColor = ColorHex.Normalize(LabelColor),
            ValueColor = ColorHex.Normalize(ValueColor),
            Display = Display?.Normalized(),
            Options = NormalizeOptions(themeId),
            Sessions = NormalizeSessions(),
            TopCount = top, NearCount = near,
            RadarRange = radar ? Math.Clamp(EffectiveRadarRange, WidgetCatalog.MinRadarRange, WidgetCatalog.MaxRadarRange) : null,
            RadarSensitivity = radar ? Math.Clamp(EffectiveRadarSensitivity, WidgetCatalog.MinRadarSensitivity, WidgetCatalog.MaxRadarSensitivity) : null,
            Scale = float.IsFinite(Scale) ? Math.Clamp(Scale, WidgetCatalog.MinScale, WidgetCatalog.MaxScale) : 1f,
            Opacity = float.IsFinite(Opacity) ? Math.Clamp(Opacity, WidgetCatalog.MinOpacity, WidgetCatalog.MaxOpacity) : 1f,
            Font = string.IsNullOrWhiteSpace(Font) ? null : Font,
            Rows = rows,
            Columns = cols,
        };
    }

    static float? NormalizeFactor(float? value, float min, float max)
    {
        if (value is not { } v || !float.IsFinite(v)) return null;
        var normalized = MathF.Round(Math.Clamp(v, min, max), 2);
        return normalized == 1f ? null : normalized;
    }

    Dictionary<string, ElementFontSettings>? NormalizeElementFonts()
    {
        Dictionary<string, ElementFontSettings>? result = null;
        if (ElementFonts is null) return null;
        foreach (var (key, value) in ElementFonts)
        {
            var known = WidgetCatalog.FontElements.FirstOrDefault(d => string.Equals(d.Id, key, StringComparison.OrdinalIgnoreCase));
            if (known is null || value is null) continue;
            var scale = NormalizeFactor(value.Scale, WidgetCatalog.MinTextScale, WidgetCatalog.MaxTextScale);
            int? weight = value.Weight is { } w and > 0 ? Math.Clamp((int)Math.Round(w / 100.0) * 100, 100, 900) : null;
            if (scale is null && weight is null) continue;
            (result ??= new(StringComparer.OrdinalIgnoreCase))[known.Id] = new() { Scale = scale, Weight = weight };
        }
        return result;
    }

    string[]? NormalizeSessions()
    {
        var c = SessionIds.Canonical(Sessions);
        if (c is null) return null;
        var def = WidgetCatalog.Find(Id);
        return SessionIds.SameSet(c, def?.Sessions ?? SessionIds.All) ? null : c;
    }

    Dictionary<string, string>? NormalizeOptions(string? themeId)
    {
        if (Options is null) return null;
        Dictionary<string, string>? result = null;
        var defs = themeId is null ? null : WidgetCatalog.OptionsFor(themeId, Id);
        foreach (var (k, v) in Options)
        {
            if (string.IsNullOrWhiteSpace(k) || string.IsNullOrWhiteSpace(v)) continue;
            string key = k, value = v;
            if (defs is not null)
            {
                var def = defs.FirstOrDefault(d => string.Equals(d.Id, k, StringComparison.OrdinalIgnoreCase));
                if (def?.Normalize(v) is not { } nv || nv == def.Normalize(def.Default)) continue;   // desconhecida, invalida ou padrao
                key = def.Id; value = nv;
            }
            (result ??= new(StringComparer.OrdinalIgnoreCase))[key] = value;
        }
        return result;
    }
}

/// <summary>Alteracao parcial de um widget (IPC): so os campos presentes mudam. ClearFont/AllColumns voltam ao padrao.</summary>
public sealed record WidgetPatch
{
    public bool? Visible { get; init; }
    public int? X { get; init; }
    public int? Y { get; init; }
    public float? Scale { get; init; }
    public float? Opacity { get; init; }
    public int? Order { get; init; }
    public string? Font { get; init; }
    public bool? ClearFont { get; init; }
    public int? Rows { get; init; }
    public string[]? Columns { get; init; }
    public bool? AllColumns { get; init; }
    public int? TopCount { get; init; }
    public int? NearCount { get; init; }
    public int? RadarRange { get; init; }
    public int? RadarSensitivity { get; init; }
    /// <summary>Substitui o mapa inteiro de larguras (vazio = todas no padrao).</summary>
    public Dictionary<string, int>? ColumnWidths { get; init; }
    public float? TextScale { get; init; }
    /// <summary>Fator independente da largura; 1 volta ao padrao.</summary>
    public float? WidthScale { get; init; }
    /// <summary>Fator independente da altura; 1 volta ao padrao.</summary>
    public float? HeightScale { get; init; }
    /// <summary>Fontes por elemento semantico. No patch substitui o mapa inteiro; vazio restaura todos.</summary>
    public Dictionary<string, ElementFontSettings>? ElementFonts { get; init; }
    /// <summary>0 = volta ao peso do tema.</summary>
    public int? FontWeight { get; init; }
    /// <summary>"" = volta a cor do tema.</summary>
    public string? TextColor { get; init; }
    public string? LabelColor { get; init; }
    public string? ValueColor { get; init; }
    /// <summary>Substitui o bloco de formato inteiro (todos os campos nulos = padrao).</summary>
    public DisplayOptions? Display { get; init; }
    /// <summary>Substitui o mapa inteiro de opcoes do tema (vazio = todas no padrao).</summary>
    public Dictionary<string, string>? Options { get; init; }
    /// <summary>Substitui os grupos de sessao (vazio = volta ao padrao do widget).</summary>
    public string[]? Sessions { get; init; }

    /// <param name="themeId">Tema do perfil: valida as <see cref="WidgetSettings.Options"/> (null = so descarta entradas vazias).</param>
    public WidgetSettings ApplyTo(WidgetSettings s, string? themeId = null) => (s with
    {
        Visible = Visible ?? s.Visible,
        X = X ?? s.X,
        Y = Y ?? s.Y,
        Scale = Scale ?? s.Scale,
        Opacity = Opacity ?? s.Opacity,
        Order = Order ?? s.Order,
        Font = ClearFont == true ? null : Font ?? s.Font,
        Rows = Rows ?? s.Rows,
        Columns = AllColumns == true ? null : Columns ?? s.Columns,
        TopCount = TopCount ?? s.TopCount,
        NearCount = NearCount ?? s.NearCount,
        RadarRange = RadarRange ?? s.RadarRange,
        RadarSensitivity = RadarSensitivity ?? s.RadarSensitivity,
        ColumnWidths = ColumnWidths ?? s.ColumnWidths,
        TextScale = TextScale ?? s.TextScale,
        WidthScale = WidthScale ?? s.WidthScale, HeightScale = HeightScale ?? s.HeightScale,
        ElementFonts = ElementFonts ?? s.ElementFonts,
        FontWeight = FontWeight ?? s.FontWeight,
        TextColor = TextColor ?? s.TextColor,
        LabelColor = LabelColor ?? s.LabelColor,
        ValueColor = ValueColor ?? s.ValueColor,
        Display = Display ?? s.Display,
        Options = Options ?? s.Options,
        Sessions = Sessions ?? s.Sessions,
    }).Normalized(themeId);

    /// <summary>Junta dois patches (b vence a): usado pelo Control Center para agrupar edicoes antes do envio.</summary>
    public static WidgetPatch Merge(WidgetPatch a, WidgetPatch b) => new()
    {
        Visible = b.Visible ?? a.Visible, X = b.X ?? a.X, Y = b.Y ?? a.Y, Scale = b.Scale ?? a.Scale, Opacity = b.Opacity ?? a.Opacity,
        Order = b.Order ?? a.Order,
        Font = b.ClearFont == true ? null : b.Font ?? (a.ClearFont == true ? null : a.Font),
        ClearFont = b.Font is not null ? null : b.ClearFont ?? a.ClearFont,
        Rows = b.Rows ?? a.Rows, TopCount = b.TopCount ?? a.TopCount, NearCount = b.NearCount ?? a.NearCount,
        RadarRange = b.RadarRange ?? a.RadarRange, RadarSensitivity = b.RadarSensitivity ?? a.RadarSensitivity,
        Columns = b.AllColumns == true ? null : b.Columns ?? (a.AllColumns == true ? null : a.Columns),
        AllColumns = b.Columns is not null ? null : b.AllColumns ?? a.AllColumns,
        WidthScale = b.WidthScale ?? a.WidthScale, HeightScale = b.HeightScale ?? a.HeightScale,
        ElementFonts = b.ElementFonts ?? a.ElementFonts,
        ColumnWidths = b.ColumnWidths ?? a.ColumnWidths, TextScale = b.TextScale ?? a.TextScale, FontWeight = b.FontWeight ?? a.FontWeight,
        TextColor = b.TextColor ?? a.TextColor, LabelColor = b.LabelColor ?? a.LabelColor, ValueColor = b.ValueColor ?? a.ValueColor,
        Display = b.Display ?? a.Display, Options = b.Options ?? a.Options, Sessions = b.Sessions ?? a.Sessions,
    };
}

/// <summary>Cor "#RRGGBB" do perfil.</summary>
public static class ColorHex
{
    /// <summary>"#rrggbb", "rrggbb" ou "#rgb" -> "#RRGGBB"; vazio ou invalido -> null (cor do tema).</summary>
    public static string? Normalize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var h = s.Trim().TrimStart('#');
        if (h.Length == 3) h = string.Concat(h.Select(ch => new string(ch, 2)));
        if (h.Length != 6 || !h.All(Uri.IsHexDigit)) return null;
        return "#" + h.ToUpperInvariant();
    }

    /// <summary>Componentes 0..1 de uma cor normalizada; null se invalida.</summary>
    public static (float R, float G, float B)? Parse(string? s)
    {
        var n = Normalize(s);
        if (n is null) return null;
        int v = Convert.ToInt32(n[1..], 16);
        return (((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f);
    }
}

/// <summary>Perfil nomeado de um tema: um <see cref="WidgetSettings"/> por widget.</summary>
public sealed record Profile
{
    /// <summary>
    /// 2: Standings ganha TopCount/NearCount e o catalogo ganha o widget "board". Perfis v1 carregam normalmente (valores padrao).
    /// 3: personalizacao (larguras, formato, texto) e colunas novas; listas de colunas salvas ganham as colunas novas (visiveis) e o
    /// Inputs do f1-2004 ganha o grafico (antes desligado por padrao).
    /// </summary>
    // 5: the f1-2004 input graph becomes an independent widget/window.
    // 6: text size is independent; dimensions and semantic font overrides are saved separately.
    public const int CurrentSchemaVersion = 6;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string Name { get; init; } = "Padrão";
    public string ThemeId { get; init; } = ThemeCatalog.Default;
    public List<WidgetSettings> Widgets { get; init; } = [];

    public WidgetSettings? Get(string id) => Widgets.FirstOrDefault(w => string.Equals(w.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Widgets em ordem de exibicao.</summary>
    [JsonIgnore]
    public IEnumerable<WidgetSettings> Ordered => Widgets.OrderBy(w => w.Order);

    /// <summary>
    /// Normaliza todos os widgets (opcoes conferidas com as do tema) e garante que todo widget do catalogo do tema exista (os que faltam
    /// entram com o padrao); widgets exclusivos de outros temas saem.
    /// </summary>
    public Profile Normalized(int screenWidth = 1920, int screenHeight = 1080)
    {
        string themeId = ThemeCatalog.Canonical(ThemeId);
        var defaults = ProfileFactory.CreateDefault(Name, themeId, screenWidth, screenHeight);
        var list = new List<WidgetSettings>();
        foreach (var d in WidgetCatalog.ForTheme(themeId))
        {
            var w = Get(d.Id);
            if (w is null && d.Id == "inputgraph" && SchemaVersion < 5 && Get("inputs") is { } oldInputs)
            {
                if (SchemaVersion < 3) oldInputs = MigrateV3(oldInputs);
                // Match the old graph's offset before removing it from the speedometer window.
                var source = (oldInputs with { TextScale = oldInputs.TextScale is { } t && float.IsFinite(t) ? Math.Clamp(t, .6f, 2f) : null }).Normalized();
                float stack = (source.ColumnVisible("gear") ? 88 : 0) + (source.ColumnVisible("bars") ? 88 : 0);
                float graphTop = source.ColumnVisible("speedo") ? 498 : stack > 0 ? stack + 4 : 0;
                w = source with { Id = d.Id, Columns = null, Visible = source.Visible && source.ColumnVisible("graph"),
                    Y = source.Y + (int)Math.Round(graphTop * source.Scale * (source.TextScale ?? 1f)) };
            }
            if (w is null && d.Id == "qualiboard" && SchemaVersion < 4 && Get("qualilap") is { } oldLap)
            {
                var target = defaults.Get(d.Id)!;
                w = oldLap with { Id = d.Id, X = target.X, Y = target.Y, Scale = target.Scale,
                    Order = target.Order, Visible = oldLap.Visible || Get("qualitower")?.Visible == true };
            }
            if (w is not null && SchemaVersion < 3) w = MigrateV3(w);
            if (w is not null && SchemaVersion < 4 && themeId == "f1-1998") w = MigrateBroadcast98(w, defaults.Get(d.Id)!, screenWidth, screenHeight);
            if (d.Id == "qualiboard" && SchemaVersion < 4 &&
                (Get("qualitower") is { Visible: true } qt && !LegacySlot(qt, 32, 24, 1, screenWidth, screenHeight) ||
                 Get("qualilap") is { Visible: true } ql && !LegacySlot(ql, 640, 900, 1, screenWidth, screenHeight)))
                w = (w ?? defaults.Get(d.Id)!) with { Visible = false };
            if (w is not null && SchemaVersion < 6)
            {
                // v5 TextScale resized the entire window. Preserve its appearance once.
                var legacyText = w.TextScale is { } ts && float.IsFinite(ts) ? Math.Clamp(ts, .6f, 2f) : 1f;
                var legacyScale = float.IsFinite(w.Scale) ? Math.Clamp(w.Scale, WidgetCatalog.MinScale, WidgetCatalog.MaxScale) : 1f;
                var effective = legacyScale * legacyText;
                var scale = Math.Clamp(effective, WidgetCatalog.MinScale, WidgetCatalog.MaxScale);
                var overflow = effective / scale;
                w = w with { Scale = scale, WidthScale = overflow == 1 ? w.WidthScale : overflow,
                    HeightScale = overflow == 1 ? w.HeightScale : overflow, TextScale = overflow == 1 ? null : overflow };
            }
            list.Add((w ?? defaults.Get(d.Id)!).Normalized(themeId));
        }
        var ordered = list.OrderBy(w => w.Order).Select((w, i) => w with { Order = i }).ToList();
        return this with { SchemaVersion = CurrentSchemaVersion, ThemeId = themeId, Widgets = ordered };
    }

    static bool LegacySlot(WidgetSettings w, int x, int y, float scale, int sw, int sh)
        => w.X == (int)Math.Round(x * sw / 1920.0) && w.Y == (int)Math.Round(y * sh / 1080.0)
            && Math.Abs(w.Scale - (float)Math.Round(scale * sh / 1080.0, 3)) < .001;

    static WidgetSettings MigrateBroadcast98(WidgetSettings w, WidgetSettings target, int sw, int sh)
    {
        var old = w.Id switch
        {
            "board" => (660, 872, 1f), "drivercaption" or "winner" => (32, 926, 1.2f),
            "pittimer" => (744, 780, 1.2f), "qualitower" => (32, 24, 1f),
            "qualilap" => (640, 900, 1f), "inputs" => (1542, 957, .5f), _ => (-1, -1, 0f),
        };
        if (old.Item1 < 0 || !LegacySlot(w, old.Item1, old.Item2, old.Item3, sw, sh)) return w;
        // Only stock placement changes; custom fonts, formats, columns and options survive.
        return w with { X = target.X, Y = target.Y, Scale = target.Scale,
            Visible = w.Id is "winner" or "pittimer" or "qualitower" or "qualilap" ? false : w.Visible };
    }

    /// <summary>v2 -> v3: colunas criadas na v3 entram visiveis nas listas salvas; Inputs do f1-2004 liga o grafico.</summary>
    WidgetSettings MigrateV3(WidgetSettings w)
    {
        if (w.Columns is null) return w;
        var add = WidgetCatalog.ColumnsAddedInV3.TryGetValue(w.Id, out var a) ? a.ToList() : [];
        if (string.Equals(w.Id, "inputs", StringComparison.OrdinalIgnoreCase) && string.Equals(ThemeId, "f1-2004", StringComparison.OrdinalIgnoreCase)) add.Add("graph");
        if (add.Count == 0) return w;
        return w with { Columns = w.Columns.Concat(add).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() };
    }

    /// <summary>Move o widget para a posicao <paramref name="index"/> da ordem de exibicao e renumera 0..n-1.</summary>
    public Profile MoveTo(string id, int index)
    {
        var list = Ordered.ToList();
        var w = list.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
        if (w is null) return this;
        list.Remove(w);
        list.Insert(Math.Clamp(index, 0, list.Count), w);
        return this with { Widgets = list.Select((x, i) => x with { Order = i }).ToList() };
    }

    public Profile WithWidget(WidgetSettings s) => this with { Widgets = Widgets.Select(w => string.Equals(w.Id, s.Id, StringComparison.OrdinalIgnoreCase) ? s : w).ToList() };
}

public static class ProfileFactory
{
    /// <summary>Perfil padrao: so os widgets do tema, posicoes do catalogo escaladas para a tela (referencia 1920x1080). Opcoes do tema = padrao (sem bloco).</summary>
    public static Profile CreateDefault(string name, string themeId, int screenWidth = 1920, int screenHeight = 1080)
    {
        double fx = screenWidth / 1920.0, fy = screenHeight / 1080.0;
        var list = WidgetCatalog.ForTheme(themeId).Select((d, i) => new { d, i, slot = WidgetLayout.Get(themeId, d.Id) }).Select(e => new WidgetSettings
        {
            Id = e.d.Id, Visible = e.d.DefaultVisible && !WidgetLayout.HiddenIn(themeId, e.d.Id), Order = e.i,
            X = (int)Math.Round(e.slot.X * fx), Y = (int)Math.Round(e.slot.Y * fy),
            Rows = e.d.DefaultRows, TopCount = e.d.HasSelection ? WidgetCatalog.DefaultTopCount : null, NearCount = e.d.HasSelection ? WidgetCatalog.DefaultNearCount : null, Scale = (float)Math.Round(e.slot.Scale * fy, 3),
            // Standings: so posicao + sigla; gap e classe sao opcionais. Board 1998: so legenda de pneus + indicador de pagina.
            // 2018: a torre da TV tem a coluna clara (gap/intervalo/...); o logo da equipe (classe) continua opcional.
            Columns = e.d.Id == "standings" ? (string.Equals(ThemeCatalog.Canonical(themeId), "f1-2018", StringComparison.OrdinalIgnoreCase) ? ["pos", "name", "gap"] : ["pos", "name"])
                : e.d.Id == "board" && string.Equals(themeId, "f1-1998", StringComparison.OrdinalIgnoreCase) ? ["tyre"]
                // f1-1998: lista vertical e lista por lado sao o padrao; tabela inferior (standings) e barra de tempo dividido (relative) sao opcionais.
                : e.d.Id == "relative" && string.Equals(themeId, "f1-1998", StringComparison.OrdinalIgnoreCase) ? ["pos", "name", "gap"]
                // Widgets de transmissao: coluna "always" = sempre visivel; o padrao e aparecer so nos eventos (os campos ficam visiveis).
                // Radar: "native" (indicador nativo) tambem e opcional.
                : e.d.Columns.Any(col => col.Id == "always") ? e.d.Columns.Where(col => col.Id is not ("always" or "native")).Select(col => col.Id).ToArray() : null,
        }).ToList();
        return new Profile { Name = name, ThemeId = themeId, Widgets = list };
    }
}
