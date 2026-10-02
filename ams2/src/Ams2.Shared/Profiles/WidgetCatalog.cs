namespace Ams2.Shared.Profiles;

/// <summary>Coluna/elemento que o usuario pode ocultar num widget.</summary>
public sealed record ColumnDef(string Id, string Label);

/// <summary>Definicao estatica de um widget: nome, limites de linhas, colunas e posicao padrao.</summary>
public sealed record WidgetDef(
    string Id, string DisplayName,
    int? MinRows, int? MaxRows, int? DefaultRows, string RowsLabel,
    IReadOnlyList<ColumnDef> Columns,
    int DefaultX, int DefaultY, bool DefaultVisible = true)
{
    public bool SupportsRows => MinRows.HasValue;
}

public static class WidgetCatalog
{
    public const float MinScale = 0.5f, MaxScale = 3f;
    public const float MinOpacity = 0.2f, MaxOpacity = 1f;

    /// <summary>Ordem padrao = ordem desta lista. Posicoes padrao em pixels de uma tela 1920x1080 (ajustadas por <see cref="ProfileFactory"/>).</summary>
    public static readonly IReadOnlyList<WidgetDef> All =
    [
        new("standings", "Standings", 3, 20, 8, "Linhas", [new("pos", "Posição"), new("name", "Piloto"), new("class", "Classe"), new("gap", "Gap")], 40, 40),
        new("relative", "Relative", 1, 4, 3, "Linhas por lado", [new("pos", "Posição"), new("name", "Piloto"), new("gap", "Gap")], 40, 860),
        new("fuel", "Fuel", null, null, null, "", [new("laps", "Voltas"), new("use", "Consumo"), new("add", "Adicionar")], 1380, 40),
        new("tyres", "Tyres", null, null, null, "", [new("temp", "Temperatura"), new("wear", "Desgaste")], 1380, 230),
        new("weather", "Weather", null, null, null, "", [], 1380, 450),
        new("inputs", "Inputs", null, null, null, "", [new("graph", "Gráfico"), new("bars", "Barras"), new("gear", "Marcha e velocidade")], 1180, 860),
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
