namespace Ams2.Shared.Profiles;

/// <summary>Coluna/elemento que o usuario pode ocultar num widget.</summary>
public sealed record ColumnDef(string Id, string Label);

/// <summary>Definicao estatica de um widget: nome, limites de linhas, colunas e posicao padrao.</summary>
public sealed record WidgetDef(
    string Id, string DisplayName,
    int? MinRows, int? MaxRows, int? DefaultRows, string RowsLabel,
    IReadOnlyList<ColumnDef> Columns,
    int DefaultX, int DefaultY, bool DefaultVisible = true, float DefaultScale = 1f)
{
    public bool SupportsRows => MinRows.HasValue;
}

public static class WidgetCatalog
{
    public const float MinScale = 0.5f, MaxScale = 3f;
    public const float MinOpacity = 0.2f, MaxOpacity = 1f;

    /// <summary>Ordem padrao = ordem desta lista. Posicoes e escalas padrao reproduzem o layout do usuario no V3/iRacing (v3-layout.json), em pixels de uma tela 1920x1080 (ajustadas por <see cref="ProfileFactory"/>).</summary>
    public static readonly IReadOnlyList<WidgetDef> All =
    [
        new("standings", "Standings", 3, 20, 8, "Linhas", [new("pos", "Posição"), new("name", "Piloto"), new("class", "Classe"), new("gap", "Gap")], 0, 0, DefaultScale: 0.6f),
        new("relative", "Relative", 1, 4, 3, "Linhas por lado", [new("pos", "Posição"), new("name", "Piloto"), new("gap", "Gap")], 1430, 925, DefaultScale: 0.58f),
        new("fuel", "Fuel", null, null, null, "", [new("laps", "Voltas"), new("use", "Consumo"), new("add", "Adicionar")], 1133, 931, DefaultScale: 0.66f),
        new("tyres", "Tyres", null, null, null, "", [new("temp", "Temperatura"), new("wear", "Desgaste")], 1139, 690, DefaultScale: 0.6f),
        new("weather", "Weather", null, null, null, "", [], 1139, 812, DefaultScale: 0.86f),
        new("inputs", "Inputs", null, null, null, "", [new("graph", "Gráfico"), new("bars", "Barras"), new("gear", "Marcha e velocidade")], 745, 905, DefaultScale: 0.55f),
        new("lapcounter", "Lap Counter", null, null, null, "", [], 918, 14, DefaultScale: 0.7f),
    ];

    public static WidgetDef? Find(string id) => All.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
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
        new("f1-2010s", "F1 2010s", true),
    ];

    public static ThemeDef? Find(string? id) => All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
}
