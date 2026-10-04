namespace Ams2.Shared.Profiles;

/// <summary>Coluna/elemento que o usuario pode ocultar (ou, em <see cref="WidgetDef.Widths"/>, dimensionar) num widget.</summary>
public sealed record ColumnDef(string Id, string Label);

/// <summary>Definicao estatica de um widget: nome, limites de linhas, colunas, larguras ajustaveis, formatos usados e posicao padrao.</summary>
public sealed record WidgetDef(
    string Id, string DisplayName,
    int? MinRows, int? MaxRows, int? DefaultRows, string RowsLabel,
    IReadOnlyList<ColumnDef> Columns,
    int DefaultX, int DefaultY, bool DefaultVisible = true, float DefaultScale = 1f, bool HasSelection = false, bool HasRadarOptions = false,
    IReadOnlyList<ColumnDef>? WidthColumns = null, DisplayCaps Caps = DisplayCaps.None, IReadOnlyList<string>? Themes = null)
{
    public bool SupportsRows => MinRows.HasValue;
    /// <summary>Colunas com largura ajustavel (% da largura do tema).</summary>
    public IReadOnlyList<ColumnDef> Widths => WidthColumns ?? [];
    /// <summary>Widget existe no tema? <see cref="Themes"/> nulo = todos os temas.</summary>
    public bool InTheme(string themeId)
        => Themes is null || Themes.Any(t => string.Equals(t, ThemeCatalog.Canonical(themeId), StringComparison.OrdinalIgnoreCase));
}

public enum OptionKind { Choice, Toggle, Number }

/// <summary>Valor de uma opcao do tipo Choice: o valor gravado no perfil e o rotulo do Control Center.</summary>
public sealed record OptionChoice(string Value, string Label);

/// <summary>
/// Opcao propria de um widget num tema (p.ex. modo da coluna do Standings no 2018). Valores gravados como texto em
/// <see cref="WidgetSettings.Options"/>: Choice = um dos <see cref="Choices"/>, Toggle = "true"/"false", Number = numero invariante em [Min, Max].
/// </summary>
public sealed record OptionDef(string Id, string Label, OptionKind Kind, IReadOnlyList<OptionChoice>? Choices, string Default, double? Min = null, double? Max = null)
{
    /// <summary>Valor canonico (Choice no case do catalogo, Toggle "true"/"false", Number limitado e invariante); null se invalido.</summary>
    public string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        switch (Kind)
        {
            case OptionKind.Choice:
                return Choices?.FirstOrDefault(c => string.Equals(c.Value, value, StringComparison.OrdinalIgnoreCase))?.Value;
            case OptionKind.Toggle:
                return bool.TryParse(value, out var b) ? (b ? "true" : "false") : null;
            case OptionKind.Number:
                if (!double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) || !double.IsFinite(n)) return null;
                if (Min is { } mn) n = Math.Max(n, mn);
                if (Max is { } mx) n = Math.Min(n, mx);
                return n.ToString(System.Globalization.CultureInfo.InvariantCulture);
            default:
                return null;
        }
    }
}

public static class WidgetCatalog
{
    public const float MinScale = 0.5f, MaxScale = 3f;
    public const float MinOpacity = 0.2f, MaxOpacity = 1f;
    /// <summary>Tamanho do texto (redimensiona o widget junto) e largura de coluna em %.</summary>
    public const float MinTextScale = 0.6f, MaxTextScale = 2f;
    public const int MinWidthPct = 50, MaxWidthPct = 250;
    /// <summary>Standings (como no iRacing): quantos pilotos do TOPO e quantos AO REDOR do jogador (a janela inclui o jogador).</summary>
    public const int DefaultTopCount = 5, MaxTopCount = 20, DefaultNearCount = 3, MaxNearCount = 10;
    /// <summary>Radar: alcance em metros (frente e tras) e sensibilidade 1..5 (3 = janelas do V3: 7 m vermelho, 12 m ambar).</summary>
    public const int MinRadarRange = 10, MaxRadarRange = 40, DefaultRadarRange = 15, MinRadarSensitivity = 1, MaxRadarSensitivity = 5, DefaultRadarSensitivity = 3;
    /// <summary>Multiplicador das janelas de aviso do radar para o nivel de sensibilidade (1 = 0,5x ... 3 = 1x ... 5 = 1,5x).</summary>
    public static double RadarSensitivityFactor(int level) => 0.5 + (Math.Clamp(level, MinRadarSensitivity, MaxRadarSensitivity) - 1) * 0.25;

    static ColumnDef C(string id, string label) => new(id, label);

    /// <summary>Ordem padrao = ordem desta lista. Posicoes e escalas padrao reproduzem o layout do usuario no V3/iRacing (v3-layout.json), em pixels de uma tela 1920x1080 (ajustadas por <see cref="ProfileFactory"/>).</summary>
    public static readonly IReadOnlyList<WidgetDef> All =
    [
        new("standings", "Standings", null, null, null, "", [C("pos", "Posição"), C("name", "Piloto (sigla)"), C("tyre", "Pneu M/B (2004-2008)"), C("class", "Classe (2018: no lugar do logo)"), C("gap", "Gap (2018: coluna clara, \"Leader\")"), C("table", "Tabela inferior 2 colunas (1998-2001)")], 0, 0, DefaultScale: 0.6f, HasSelection: true,
            WidthColumns: [C("pos", "Posição"), C("name", "Nome"), C("gap", "Gap")], Caps: DisplayCaps.Name | DisplayCaps.Gap),
        new("relative", "Relative", 1, 4, 3, "Linhas por lado", [C("pos", "Posição"), C("name", "Piloto"), C("gap", "Gap"), C("bar", "Barra do vizinho (2004-2008) / tempo dividido (1998-2001)")], 1430, 925, DefaultVisible: false, DefaultScale: 0.58f,
            WidthColumns: [C("pos", "Posição"), C("name", "Nome"), C("gap", "Gap")], Caps: DisplayCaps.Name | DisplayCaps.Gap),
        new("fuel", "Fuel", null, null, null, "", [C("laps", "Voltas"), C("use", "Consumo"), C("add", "Adicionar")], 1133, 931, DefaultScale: 0.66f,
            WidthColumns: [C("value", "Valores")], Caps: DisplayCaps.Fuel),
        new("tyres", "Tyres", null, null, null, "", [C("temp", "Temperatura"), C("wear", "Desgaste"), C("compound", "Composto")], 1139, 690, DefaultScale: 0.6f, Caps: DisplayCaps.Temp),
        new("weather", "Weather", null, null, null, "", [C("air", "Ar"), C("track", "Pista"), C("rain", "Chuva")], 1139, 812, DefaultScale: 0.86f, Caps: DisplayCaps.Temp),
        new("inputs", "Inputs", null, null, null, "", [C("graph", "Gráfico (acelerador e freio)"), C("bars", "Barras / pedais"), C("gear", "Marcha e velocidade"), C("speedo", "Velocímetro analógico (2004-2008)")], 745, 905, DefaultScale: 0.55f,
            WidthColumns: [C("graph", "Gráfico")], Caps: DisplayCaps.Speed),
        new("radar", "Radar", null, null, null, "", [C("native", "Indicador nativo do AMS2 (senão: painel estilo V3)"), C("always", "Sempre visível (senão só com carro próximo)")], 900, 585, DefaultScale: 1f, HasRadarOptions: true),
        new("lapcounter", "Lap Counter", null, null, null, "", [], 918, 14, DefaultScale: 0.7f),
        new("drivercaption", "Driver Caption", null, null, null, "", [C("always", "Sempre visível (senão só em eventos)"), C("team", "Equipe"), C("tyre", "Fornecedor de pneus")], 60, 930, DefaultVisible: false, DefaultScale: 0.75f,
            Caps: DisplayCaps.Name),
        new("pitstops", "Pit Stops", 1, 4, 4, "Linhas por coluna", [C("always", "Sempre visível (senão ao entrar nos boxes)"), C("pos", "Posição")], 20, 480, DefaultScale: 0.6f,
            WidthColumns: [C("name", "Nome"), C("stops", "Paradas")], Caps: DisplayCaps.Name),
        new("pittimer", "Pit Timer", null, null, null, "", [C("always", "Sempre visível (senão só parado)")], 820, 960, DefaultScale: 0.8f,
            WidthColumns: [C("name", "Nome")], Caps: DisplayCaps.Name),
        new("board", "Board (torre, setor, voltas, legenda)", null, null, null, "", [C("tyre", "Fornecedor de pneus (legenda)"), C("page", "Indicador de página X/Y")], 640, 870,
            WidthColumns: [C("name", "Nome (torre)"), C("gap", "Gap (torre)"), C("time", "Tempo de volta (comparativo)")], Caps: DisplayCaps.Name | DisplayCaps.Gap | DisplayCaps.LapTime),
        new("winner", "Winner", null, null, null, "", [C("always", "Sempre visível (senão ao fim da corrida)"), C("team", "Equipe"), C("stats", "Tempo, distância e média")], 60, 930, DefaultScale: 0.75f,
            Caps: DisplayCaps.Name | DisplayCaps.Speed),
    ];

    /// <summary>Colunas criadas no esquema 3: entram visiveis nas listas de colunas salvas por perfis antigos (migracao).</summary>
    public static readonly IReadOnlyDictionary<string, string[]> ColumnsAddedInV3 = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["tyres"] = ["compound"],
        ["weather"] = ["air", "track", "rain"],
        ["inputs"] = ["speedo"],
        ["drivercaption"] = ["team", "tyre"],
        ["pitstops"] = ["pos"],
        ["winner"] = ["team", "stats"],
    };

    public static WidgetDef? Find(string id) => All.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Widgets do tema, na ordem do catalogo (os exclusivos de outros temas ficam de fora).</summary>
    public static IEnumerable<WidgetDef> ForTheme(string themeId) => All.Where(d => d.InTheme(themeId));

    static OptionChoice O(string value, string label) => new(value, label);

    /// <summary>Opcoes proprias de cada widget por tema: [tema][widget] -> definicoes. Widgets/temas ausentes = sem opcoes.</summary>
    static readonly Dictionary<string, Dictionary<string, IReadOnlyList<OptionDef>>> ThemeOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["f1-2018"] = new(StringComparer.OrdinalIgnoreCase)
        {
            // Torre do 2018: o que a coluna clara mostra (a TV alterna gap, intervalo, posicoes ganhas, paradas e melhor volta).
            ["standings"] =
            [
                new("mode", "Modo da coluna", OptionKind.Choice,
                    [O("gap", "Gap para o líder"), O("interval", "Intervalo (carro à frente)"), O("gainedlost", "Posições ganhas/perdidas"),
                     O("pitstops", "Paradas nos boxes"), O("bestlap", "Melhor volta"), O("auto", "Automático (alterna como na TV)")], "gap"),
                new("modeSeconds", "Automático: segundos por modo", OptionKind.Number, null, "10", Min: 5, Max: 30),
                new("battle", "Bloco \"BATTLE FOR\" (jogador a menos de 1 s de alguém)", OptionKind.Toggle, null, "true"),
                new("fullNames", "Nomes completos sob bandeira amarela / Safety Car", OptionKind.Toggle, null, "true"),
                new("outBlock", "Pilotos fora da corrida no bloco cinza \"OUT\" (senão ocultos)", OptionKind.Toggle, null, "true"),
            ],
            // Legenda do 2018: placa do piloto, variante STARTED / NOW (grid de largada x posicao atual), resultado no fim ou automatico.
            ["drivercaption"] =
            [
                new("variant", "Variante da legenda", OptionKind.Choice,
                    [O("driver", "Piloto (nome, número e equipe)"), O("startednow", "Largou / Agora (STARTED / NOW)"),
                     O("result", "Resultado (posição final)"), O("auto", "Automático (como na TV)")], "auto"),
                new("showFor", "Tempo na tela por evento (s)", OptionKind.Number, null, "6", Min: 3, Max: 15),
            ],
        },
    };

    /// <summary>Opcoes do widget no tema (vazio = nenhuma).</summary>
    public static IReadOnlyList<OptionDef> OptionsFor(string themeId, string widgetId)
        => ThemeOptions.TryGetValue(ThemeCatalog.Canonical(themeId), out var t) && t.TryGetValue(widgetId, out var o) ? o : [];
}

public sealed record ThemeDef(string Id, string DisplayName, bool Available);

public static class ThemeCatalog
{
    public const string Default = "f1-1998";

    /// <summary>Temas conhecidos. Os ainda nao implementados aparecem desabilitados no Control Center.</summary>
    public static readonly IReadOnlyList<ThemeDef> All =
    [
        new("f1-1998", "F1 1998-2001", true),
        new("f1-2004", "F1 2004-2008", true),
        new("f1-2018", "F1 2018", true),
    ];

    /// <summary>Ids de temas que foram substituidos (perfis e state.json antigos): "f1-2010s" virou "f1-2018".</summary>
    public static readonly IReadOnlyDictionary<string, string> LegacyIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["f1-2010s"] = "f1-2018",
    };

    /// <summary>Id atual do tema: troca ids antigos pelo substituto; os demais passam como estao.</summary>
    public static string Canonical(string id) => LegacyIds.TryGetValue(id, out var n) ? n : id;

    public static ThemeDef? Find(string? id) => id is null ? null : All.FirstOrDefault(t => string.Equals(t.Id, Canonical(id), StringComparison.OrdinalIgnoreCase));
}
