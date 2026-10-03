namespace Ams2.Shared.Profiles;

/// <summary>Coluna/elemento que o usuario pode ocultar num widget.</summary>
public sealed record ColumnDef(string Id, string Label);

/// <summary>Definicao estatica de um widget: nome, limites de linhas, colunas e posicao padrao.</summary>
public sealed record WidgetDef(
    string Id, string DisplayName,
    int? MinRows, int? MaxRows, int? DefaultRows, string RowsLabel,
    IReadOnlyList<ColumnDef> Columns,
    int DefaultX, int DefaultY, bool DefaultVisible = true, float DefaultScale = 1f, bool HasSelection = false, bool HasRadarOptions = false)
{
    public bool SupportsRows => MinRows.HasValue;
}

public static class WidgetCatalog
{
    public const float MinScale = 0.5f, MaxScale = 3f;
    public const float MinOpacity = 0.2f, MaxOpacity = 1f;
    /// <summary>Standings (como no iRacing): quantos pilotos do TOPO e quantos AO REDOR do jogador (a janela inclui o jogador).</summary>
    public const int DefaultTopCount = 5, MaxTopCount = 20, DefaultNearCount = 3, MaxNearCount = 10;
    /// <summary>Radar: alcance em metros (frente e tras) e sensibilidade 1..5 (3 = janelas do V3: 7 m vermelho, 12 m ambar).</summary>
    public const int MinRadarRange = 10, MaxRadarRange = 40, DefaultRadarRange = 15, MinRadarSensitivity = 1, MaxRadarSensitivity = 5, DefaultRadarSensitivity = 3;
    /// <summary>Multiplicador das janelas de aviso do radar para o nivel de sensibilidade (1 = 0,5x ... 3 = 1x ... 5 = 1,5x).</summary>
    public static double RadarSensitivityFactor(int level) => 0.5 + (Math.Clamp(level, MinRadarSensitivity, MaxRadarSensitivity) - 1) * 0.25;

    /// <summary>Ordem padrao = ordem desta lista. Posicoes e escalas padrao reproduzem o layout do usuario no V3/iRacing (v3-layout.json), em pixels de uma tela 1920x1080 (ajustadas por <see cref="ProfileFactory"/>).</summary>
    public static readonly IReadOnlyList<WidgetDef> All =
    [
        new("standings", "Standings", null, null, null, "", [new("pos", "Posição"), new("name", "Piloto (sigla)"), new("flag", "Bandeira"), new("tyre", "Pneu M/B (2004-2008)"), new("class", "Classe"), new("gap", "Gap"), new("table", "Tabela inferior 2 colunas (1998-2001)")], 0, 0, DefaultScale: 0.6f, HasSelection: true),
        new("relative", "Relative", 1, 4, 3, "Linhas por lado", [new("pos", "Posição"), new("name", "Piloto"), new("gap", "Gap"), new("bar", "Barra do vizinho (2004-2008) / tempo dividido (1998-2001)")], 1430, 925, DefaultVisible: false, DefaultScale: 0.58f),
        new("fuel", "Fuel", null, null, null, "", [new("laps", "Voltas"), new("use", "Consumo"), new("add", "Adicionar")], 1133, 931, DefaultScale: 0.66f),
        new("tyres", "Tyres", null, null, null, "", [new("temp", "Temperatura"), new("wear", "Desgaste")], 1139, 690, DefaultScale: 0.6f),
        new("weather", "Weather", null, null, null, "", [], 1139, 812, DefaultScale: 0.86f),
        new("inputs", "Inputs", null, null, null, "", [new("graph", "Gráfico"), new("bars", "Barras"), new("gear", "Marcha e velocidade")], 745, 905, DefaultScale: 0.55f),
        new("radar", "Radar", null, null, null, "", [new("always", "Sempre visível (senão só com carro próximo)")], 900, 585, DefaultScale: 1f, HasRadarOptions: true),
        new("lapcounter", "Lap Counter", null, null, null, "", [], 918, 14, DefaultScale: 0.7f),
        new("drivercaption", "Driver Caption", null, null, null, "", [new("always", "Sempre visível (senão só em eventos)"), new("flag", "Bandeira (1998-2001)")], 60, 930, DefaultVisible: false, DefaultScale: 0.75f),
        new("pitstops", "Pit Stops", 1, 4, 4, "Linhas por coluna", [new("always", "Sempre visível (senão ao entrar nos boxes)")], 20, 480, DefaultScale: 0.6f),
        new("pittimer", "Pit Timer", null, null, null, "", [new("always", "Sempre visível (senão só parado)")], 820, 960, DefaultScale: 0.8f),
        new("board", "Board (torre, setor, voltas, legenda)", null, null, null, "", [new("flag", "Bandeira"), new("tyre", "Fornecedor de pneus (legenda)"), new("page", "Indicador de página X/Y")], 640, 870),
        new("winner", "Winner", null, null, null, "", [new("always", "Sempre visível (senão ao fim da corrida)"), new("flag", "Bandeira (1998-2001)")], 60, 930, DefaultScale: 0.75f),
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
