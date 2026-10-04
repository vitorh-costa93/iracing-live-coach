using System.Text.Json.Serialization;

namespace Ams2.Shared.Profiles;

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
    /// <summary>Tamanho do texto (1 = tema). Redimensiona o widget inteiro junto (linhas, colunas, janela), na proporcao.</summary>
    public float? TextScale { get; init; }
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

    /// <summary>Valor gravado da opcao do tema (null = padrao; use <see cref="OptionOr"/> ou o Default da <see cref="OptionDef"/>).</summary>
    public string? Option(string id) => Options is not null && Options.TryGetValue(id, out var v) ? v : null;
    /// <summary>Valor gravado da opcao ou <paramref name="fallback"/>.</summary>
    public string OptionOr(string id, string fallback) => Option(id) ?? fallback;

    /// <summary>Escala de render efetiva = escala da janela x tamanho do texto: a fonte maior aumenta o widget inteiro na mesma proporcao.</summary>
    [JsonIgnore] public float RenderScale => Scale * (TextScale ?? 1f);
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
        var def = WidgetCatalog.Find(Id);
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
            TextScale = textScale,
            FontWeight = FontWeight is { } fw and > 0 ? Math.Clamp((int)Math.Round(fw / 100.0) * 100, 100, 900) : null,
            TextColor = ColorHex.Normalize(TextColor),
            LabelColor = ColorHex.Normalize(LabelColor),
            ValueColor = ColorHex.Normalize(ValueColor),
            Display = Display?.Normalized(),
            Options = NormalizeOptions(themeId),
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
        FontWeight = FontWeight ?? s.FontWeight,
        TextColor = TextColor ?? s.TextColor,
        LabelColor = LabelColor ?? s.LabelColor,
        ValueColor = ValueColor ?? s.ValueColor,
        Display = Display ?? s.Display,
        Options = Options ?? s.Options,
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
        ColumnWidths = b.ColumnWidths ?? a.ColumnWidths, TextScale = b.TextScale ?? a.TextScale, FontWeight = b.FontWeight ?? a.FontWeight,
        TextColor = b.TextColor ?? a.TextColor, LabelColor = b.LabelColor ?? a.LabelColor, ValueColor = b.ValueColor ?? a.ValueColor,
        Display = b.Display ?? a.Display, Options = b.Options ?? a.Options,
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
    public const int CurrentSchemaVersion = 3;

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
            if (w is not null && SchemaVersion < 3) w = MigrateV3(w);
            list.Add((w ?? defaults.Get(d.Id)!).Normalized(themeId));
        }
        var ordered = list.OrderBy(w => w.Order).Select((w, i) => w with { Order = i }).ToList();
        return this with { SchemaVersion = CurrentSchemaVersion, ThemeId = themeId, Widgets = ordered };
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
            Id = e.d.Id, Visible = e.d.DefaultVisible, Order = e.i,
            X = (int)Math.Round(e.slot.X * fx), Y = (int)Math.Round(e.slot.Y * fy),
            Rows = e.d.DefaultRows, TopCount = e.d.HasSelection ? WidgetCatalog.DefaultTopCount : null, NearCount = e.d.HasSelection ? WidgetCatalog.DefaultNearCount : null, Scale = (float)Math.Round(e.slot.Scale * fy, 3),
            // Standings: so posicao + sigla; gap e classe sao opcionais. Board 1998: so legenda de pneus + indicador de pagina.
            Columns = e.d.Id == "standings" ? ["pos", "name"]
                : e.d.Id == "board" && string.Equals(themeId, "f1-1998", StringComparison.OrdinalIgnoreCase) ? ["tyre", "page"]
                // f1-1998: lista vertical e lista por lado sao o padrao; tabela inferior (standings) e barra de tempo dividido (relative) sao opcionais.
                : e.d.Id == "relative" && string.Equals(themeId, "f1-1998", StringComparison.OrdinalIgnoreCase) ? ["pos", "name", "gap"]
                // Widgets de transmissao: coluna "always" = sempre visivel; o padrao e aparecer so nos eventos (os campos ficam visiveis).
                // Radar: "native" (indicador nativo) tambem e opcional.
                : e.d.Columns.Any(col => col.Id == "always") ? e.d.Columns.Where(col => col.Id is not ("always" or "native")).Select(col => col.Id).ToArray() : null,
        }).ToList();
        return new Profile { Name = name, ThemeId = themeId, Widgets = list };
    }
}
