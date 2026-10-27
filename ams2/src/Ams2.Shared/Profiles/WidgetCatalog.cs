namespace Ams2.Shared.Profiles;

/// <summary>Coluna/elemento que o usuario pode ocultar (ou, em <see cref="WidgetDef.Widths"/>, dimensionar) num widget.</summary>
public sealed record ColumnDef(string Id, string Label);

/// <summary>Definicao estatica de um widget: nome, limites de linhas, colunas, larguras ajustaveis, formatos usados e posicao padrao.</summary>
public sealed record WidgetDef(
    string Id, string DisplayName,
    int? MinRows, int? MaxRows, int? DefaultRows, string RowsLabel,
    IReadOnlyList<ColumnDef> Columns,
    int DefaultX, int DefaultY, bool DefaultVisible = true, float DefaultScale = 1f, bool HasSelection = false, bool HasRadarOptions = false,
    IReadOnlyList<ColumnDef>? WidthColumns = null, DisplayCaps Caps = DisplayCaps.None, IReadOnlyList<string>? Themes = null, IReadOnlyList<string>? DefaultSessions = null)
{
    public bool SupportsRows => MinRows.HasValue;
    /// <summary>Grupos de sessao (<see cref="SessionIds"/>) em que o widget aparece quando o perfil nao define <see cref="WidgetSettings.Sessions"/>.
    /// Nulo em <see cref="DefaultSessions"/> = todas (comportamento dos widgets que ja existiam antes do filtro, para nao mudar perfis).</summary>
    public IReadOnlyList<string> Sessions => DefaultSessions ?? SessionIds.All;
    /// <summary>Colunas com largura ajustavel (% da largura do tema).</summary>
    public IReadOnlyList<ColumnDef> Widths => WidthColumns ?? [];
    /// <summary>Widget existe no tema? <see cref="Themes"/> nulo = todos os temas.</summary>
    public bool InTheme(string themeId)
        => (Themes is null || Themes.Any(t => string.Equals(t, ThemeCatalog.Canonical(themeId), StringComparison.OrdinalIgnoreCase)))
            && (ThemeCatalog.Canonical(themeId) != "f1-1993" || WidgetCatalog.Supports93(WidgetCatalog.ParentOf(Id) ?? Id))
            && (!WidgetCatalog.Is99(themeId) || WidgetCatalog.Supports99(WidgetCatalog.ParentOf(Id) ?? Id));
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
    public const float MinAxisScale = 0.25f, MaxAxisScale = 3f;
    public static IReadOnlyList<ColumnDef> FontElements { get; } =
        [new("name", "Nome do piloto"), new("time", "Tempos"), new("gap", "Gap / intervalo"),
         new("position", "Posição"), new("label", "Rótulos"), new("title", "Títulos"), new("value", "Valores")];
    public const float MinOpacity = 0.2f, MaxOpacity = 1f;
    /// <summary>Tamanho das fontes independente da geometria; largura de coluna em %.</summary>
    public const float MinTextScale = 0.4f, MaxTextScale = 2f;
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
            WidthColumns: [C("pos", "Posição"), C("name", "Nome"), C("gap", "Gap")], Caps: DisplayCaps.Name | DisplayCaps.Gap,
            // Na classificacao a Quali Tower ocupa o lugar da torre (WidgetLayout.ExclusiveGroups): as duas juntas se sobrepunham e o
            // painel translucido da Quali Tower deixava ver o "LAP" e as siglas da torre por baixo (texto fantasma no teste de 04/10).
            DefaultSessions: [SessionIds.Practice, SessionIds.Race]),
        new("relative", "Relative", 1, 4, 3, "Linhas por lado", [C("pos", "Posição"), C("name", "Piloto"), C("gap", "Gap"), C("bar", "Barra do vizinho (2004-2008) / tempo dividido (1998-2001)")], 1430, 925, DefaultVisible: false, DefaultScale: 0.58f,
            WidthColumns: [C("pos", "Posição"), C("name", "Nome"), C("gap", "Gap")], Caps: DisplayCaps.Name | DisplayCaps.Gap),
        new("fuel", "Fuel", null, null, null, "", [C("laps", "Voltas"), C("use", "Consumo"), C("add", "Adicionar")], 1133, 931, DefaultScale: 0.66f,
            WidthColumns: [C("value", "Valores")], Caps: DisplayCaps.Fuel),
        new("tyres", "Tyres", null, null, null, "", [C("temp", "Temperatura"), C("wear", "Desgaste"), C("compound", "Composto")], 1139, 690, DefaultScale: 0.6f, Caps: DisplayCaps.Temp),
        new("weather", "Weather", null, null, null, "", [C("air", "Ar"), C("track", "Pista"), C("rain", "Chuva")], 1139, 812, DefaultScale: 0.86f, Caps: DisplayCaps.Temp),
        new("inputs", "Inputs", null, null, null, "", [C("graph", "Gráfico (acelerador e freio)"), C("bars", "Barras / pedais"), C("gear", "Marcha e velocidade"), C("speedo", "Velocímetro analógico (2004-2008)")], 745, 905, DefaultScale: 0.55f,
            WidthColumns: [C("graph", "Gráfico")], Caps: DisplayCaps.Speed),
        new("inputgraph", "Inputs Graph", null, null, null, "", [], 1700, 988, DefaultScale: 0.6f,
            WidthColumns: [C("graph", "Gráfico")], Themes: ["f1-2004", "f1-2012"]),
        new("radar", "Radar", null, null, null, "", [C("native", "Indicador nativo do AMS2 (senão: painel estilo V3)"), C("always", "Sempre visível (senão só com carro próximo)")], 900, 585, DefaultScale: 1f, HasRadarOptions: true),
        new("lapcounter", "Lap Counter", null, null, null, "", [], 918, 14, DefaultScale: 0.7f, DefaultSessions: [SessionIds.Practice, SessionIds.Race]),
        new("drivercaption", "Driver Caption", null, null, null, "", [C("always", "Sempre visível (senão só em eventos)"), C("team", "Equipe"), C("tyre", "Fornecedor de pneus")], 60, 930, DefaultVisible: false, DefaultScale: 0.75f,
            Caps: DisplayCaps.Name),
        new("pitstops", "Pit Stops", 1, 4, 4, "Linhas por coluna", [C("always", "Sempre visível (senão ao entrar nos boxes)"), C("pos", "Posição")], 20, 480, DefaultScale: 0.6f,
            WidthColumns: [C("name", "Nome"), C("stops", "Paradas")], Caps: DisplayCaps.Name),
        new("pittimer", "Pit Timer", null, null, null, "", [C("always", "Sempre visível (senão só parado)")], 820, 960, DefaultScale: 0.8f,
            WidthColumns: [C("name", "Nome")], Caps: DisplayCaps.Name),
        new("board", "Board (torre, setor, voltas, legenda)", null, null, null, "", [C("tyre", "Fornecedor de pneus (legenda)"), C("page", "Indicador de página X/Y")], 640, 870,
            WidthColumns: [C("name", "Nome (torre)"), C("gap", "Gap (torre)"), C("time", "Tempo de volta (comparativo)")], Caps: DisplayCaps.Name | DisplayCaps.Gap | DisplayCaps.LapTime,
            DefaultSessions: [SessionIds.Practice, SessionIds.Race]),   // idem: a Quali Lap ocupa o lugar do Board na classificacao
        new("winner", "Winner", null, null, null, "", [C("always", "Sempre visível (senão ao fim da corrida)"), C("team", "Equipe"), C("stats", "Tempo, distância e média")], 60, 930, DefaultScale: 0.75f,
            Caps: DisplayCaps.Name | DisplayCaps.Speed),
        // Exclusivo do 2018: painel "LIVE SPEED" da TV (velocidade do jogador em km/h e mph). Unidades e visibilidade nas opcoes do tema.
        new("livespeed", "Live Speed", null, null, null, "", [], 1600, 400, DefaultScale: 0.65f, Caps: DisplayCaps.Name, Themes: ["f1-2018"]),
        // Exclusivo do 2018: "RACE START 0-200km/h" da TV (tempo do jogador de 0 a 100/200 km/h na largada + melhor anterior da pista+carro).
        new("racestart", "Race Start", null, null, null, "", [], 1600, 560, DefaultScale: 0.65f, Caps: DisplayCaps.Name, Themes: ["f1-2018"]),
        // Exclusivo do 2018: "Race Control" da TV (caixa de bandeira YELLOW FLAG / INCIDENT e barra "SLOW STOP -x.xs" de parada lenta do jogador).
        new("racecontrol", "Race Control", null, null, null, "", [], 320, 40, DefaultScale: 0.8f, Caps: DisplayCaps.Name, Themes: ["f1-2018"]),
        // Exclusivos do 2018, no lugar do Board da corrida (a TV 2018 nao o tem): "Battle" (duelo com DRS e velocidades dos dois carros) e a legenda de
        // posicao dupla com bandeira do pais. Na classificacao dao lugar a Quali Lap (WidgetLayout.ExclusiveGroups).
        new("battle", "Battle (duelo, DRS e velocidades)", null, null, null, "", [], 700, 940, Caps: DisplayCaps.Name, Themes: ["f1-2018", "f1-2012"], DefaultSessions: [SessionIds.Practice, SessionIds.Race]),
        new("poscaption", "Legenda de posição (com bandeira)", null, null, null, "", [], 640, 962, DefaultScale: 0.9f, Caps: DisplayCaps.Name, Themes: ["f1-2018"], DefaultSessions: [SessionIds.Practice, SessionIds.Race]),
        // Exclusivo do 2012: faixa inferior de classificacao (5 celulas por pagina, [pos][SIGLA][intervalo]), no lugar do Board.
        new("ticker", "Faixa de classificação (5 por página)", null, null, null, "", [], 60, 1000, Caps: DisplayCaps.Name | DisplayCaps.Gap, Themes: ["f1-2012"], DefaultSessions: [SessionIds.Practice, SessionIds.Race]),
        // Classificacao (todos os temas, PLANO-QUALI.md): nascem so na sessao de classificacao. qualitower, qualilap e qualiresult desenhados nos 3 temas.
        new("qualitower", "Quali Tower", null, null, null, "", [], 32, 24, Caps: DisplayCaps.Name | DisplayCaps.LapTime, DefaultSessions: [SessionIds.Qualify]),
        new("qualilap", "Quali Lap", null, null, null, "", [], 660, 900, Caps: DisplayCaps.Name | DisplayCaps.LapTime, DefaultSessions: [SessionIds.Qualify]),
        new("qualiboard", "Quali Board (torre e volta)", null, null, null, "", [], 0, 780, Caps: DisplayCaps.Name | DisplayCaps.LapTime, Themes: ["f1-1993", "f1-1998", "f1-1999-digital"], DefaultSessions: [SessionIds.Qualify]),
        new("qualiresult", "Quali Result", null, null, null, "", [], 320, 200, Caps: DisplayCaps.Name | DisplayCaps.LapTime, DefaultSessions: [SessionIds.Qualify]),
        // Lap Delta (estilo iRacing, temas 2004/2012/2018): delta ao vivo da volta vs melhor volta + parciais + ultima volta. Desligado por padrao (liga no Control Center).
        new("lapdelta", "Lap Delta (delta, parciais e última volta)", null, null, null, "", [], 640, 20, DefaultVisible: false, DefaultScale: 0.8f, Caps: DisplayCaps.Gap | DisplayCaps.LapTime, Themes: ["f1-2004", "f1-2012", "f1-2018"]),
        new("gpxgauges", "Instrumentos GPxCInfo (Lo2k)", null, null, null, "", [], 341, 567, DefaultScale: 1.406f,
            Themes: ["f1-1998", "f1-1999-digital", "f1-2004", "f1-2012"]),
        // GPxSAT (GPxPatch do GP4): camada de tela inteira com as telas do modo do tema (Classic, Digital, Digital 2004, Digital 2010).
        new("gpxsat", "GPxSAT (telas do GPxPatch)", null, null, null, "", [], 0, 0, DefaultScale: 1f, Caps: DisplayCaps.Name,
            Themes: ["f1-1998", "f1-1999-digital", "f1-2004", "f1-2012"]),
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

    public static WidgetDef? Find(string id, string? themeId = null)
        => ParentOf(id) is { } parent ? FindPiece(id, parent, themeId)
            : (themeId is null ? All : ForTheme(themeId)).FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Janela propria de uma peca da Exibicao ("pai.peca", p.ex. "board.fastest"): fora de <see cref="All"/>/<see cref="ForTheme"/> (o perfil so
    /// ganha a entrada quando o usuario a edita no Control Center), com posicao/escala/visibilidade proprias e o conteudo do pai
    /// (<see cref="RenderSettings"/>). Sem colunas/opcoes/formato proprios: tudo isso continua no pai.
    /// </summary>
    public const char PieceSeparator = '.';
    public static string? ParentOf(string id) { int i = id.IndexOf(PieceSeparator); return i > 0 ? id[..i] : null; }
    public static bool IsPiece(string id) => ParentOf(id) is not null;
    /// <summary>Ids das janelas de peca do widget <paramref name="parentId"/> no tema (vazio = widget sem pecas).</summary>
    public static IEnumerable<string> PieceIds(string themeId, string parentId)
        => IsPiece(parentId) ? [] : ShowCatalog.For(themeId, parentId).Pieces.Select(p => parentId + PieceSeparator + p.Id);

    static WidgetDef? FindPiece(string id, string parentId, string? themeId)
    {
        if (Find(parentId, themeId) is not { } parent) return null;
        string pieceId = id[(parentId.Length + 1)..], label = pieceId;
        if (themeId is not null)
        {
            if (ShowCatalog.For(themeId, parent.Id).Pieces.FirstOrDefault(p => string.Equals(p.Id, pieceId, StringComparison.OrdinalIgnoreCase)) is not { } piece) return null;
            pieceId = piece.Id; label = piece.Label;
        }
        return parent with
        {
            Id = parent.Id + PieceSeparator + pieceId, DisplayName = $"{parent.DisplayName} › {label}",
            MinRows = null, MaxRows = null, DefaultRows = null, RowsLabel = "", Columns = [], WidthColumns = [],
            HasSelection = false, HasRadarOptions = false, DefaultVisible = false, Caps = DisplayCaps.None,
        };
    }

    /// <summary>Widgets do tema, na ordem do catalogo (os exclusivos de outros temas ficam de fora).</summary>
    static readonly HashSet<string> Widgets93 = new(StringComparer.OrdinalIgnoreCase)
        { "board", "qualiboard", "standings", "qualitower", "radar", "inputs" };
    internal static bool Supports93(string id) => Widgets93.Contains(id);
    static readonly HashSet<string> Widgets99 = new(StringComparer.OrdinalIgnoreCase)
        { "board", "qualiboard", "inputs", "radar", "gpxsat", "gpxgauges" };
    internal static bool Supports99(string id) => Widgets99.Contains(id);
    /// <summary>Temas da familia 1999 (F1 1999 e F1 1999 Digital): mesma composicao de widgets.</summary>
    public static bool Is99(string themeId) => ThemeCatalog.Canonical(themeId) is var t
        && (string.Equals(t, "f1-1998", StringComparison.OrdinalIgnoreCase) || string.Equals(t, "f1-1999-digital", StringComparison.OrdinalIgnoreCase));
    /// <summary>Colunas das listas do 1993 (Race/Qualy Standings): faixa azul de patrocinador (ligada) e gap (desligado).</summary>
    static readonly ColumnDef[] Columns93List = [new("sponsor", "Faixa azul de patrocinador"), new("time", "Gap / tempo ao lado do sobrenome")];
    public static IEnumerable<WidgetDef> ForTheme(string themeId) => All.Where(d => d.InTheme(themeId)).Select(d =>
        Is99(themeId) && d.Id == "board"
            ? d with { DisplayName = "Race Board (gaps, pit stop, volta rápida e legenda)", Columns = [new("team", "Equipe"), new("tyre", "Fornecedor de pneus")], WidthColumns = [], Caps = DisplayCaps.Name | DisplayCaps.Gap | DisplayCaps.LapTime }
        : Is99(themeId) && d.Id == "qualiboard"
            ? d with { DisplayName = "Qualy Board (out lap, intermediárias e resultado)", Columns = [new("team", "Equipe"), new("tyre", "Fornecedor de pneus")], Caps = DisplayCaps.Name | DisplayCaps.Gap | DisplayCaps.LapTime | DisplayCaps.Speed }
        : Is99(themeId) && d.Id == "radar"
            ? d with { DefaultSessions = [SessionIds.Practice, SessionIds.Race] }
        : ThemeCatalog.Canonical(themeId) == "f1-1993" && d.Id == "board"
            ? d with { DisplayName = "Race Board (gap, volta mais rápida e legenda)", Columns = [], WidthColumns = [] }
        : ThemeCatalog.Canonical(themeId) == "f1-1993" && d.Id == "qualiboard"
            ? d with { DisplayName = "Qualy Board (volta, parcial e resultado)" }
        : ThemeCatalog.Canonical(themeId) == "f1-1993" && d.Id == "standings"
            ? d with { DisplayName = "Race Standings", Columns = Columns93List, WidthColumns = [], HasSelection = false, DefaultScale = 1f }
        : ThemeCatalog.Canonical(themeId) == "f1-1993" && d.Id == "qualitower"
            ? d with { DisplayName = "Qualy Standings", Columns = Columns93List }
        :
        d.Id == "inputs" && ThemeCatalog.Canonical(themeId) is var themeKey && (string.Equals(themeKey, "f1-2004", StringComparison.OrdinalIgnoreCase) || string.Equals(themeKey, "f1-2012", StringComparison.OrdinalIgnoreCase))
            ? d with { DisplayName = "Velocímetro", Columns = d.Columns.Where(c => c.Id != "graph").ToArray(), WidthColumns = [] }
            : d);

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
                new("intervalSeconds", "Atualização visual de gap/intervalo (s)", OptionKind.Number, null, "1", Min: 0.25, Max: 3),
                new("battle", "Bloco \"BATTLE FOR\" (jogador a menos de 1 s de alguém)", OptionKind.Toggle, null, "true"),
                new("fullNames", "Nomes completos sob bandeira amarela / Safety Car", OptionKind.Toggle, null, "true"),
                new("outBlock", "Pilotos fora da corrida no bloco cinza \"OUT\" (senão ocultos)", OptionKind.Toggle, null, "true"),
            ],
            ["board"] =
            [
                new("intervalSeconds", "Atualização visual do intervalo ao vivo (s)", OptionKind.Number, null, "1", Min: 0.25, Max: 3),
            ],
            // Legenda do 2018: placa do piloto, variante STARTED / NOW (grid de largada x posicao atual), resultado no fim ou automatico.
            ["drivercaption"] =
            [
                new("variant", "Variante da legenda", OptionKind.Choice,
                    [O("driver", "Piloto (nome, número e equipe)"), O("startednow", "Largou / Agora (STARTED / NOW)"),
                     O("result", "Resultado (posição final)"), O("auto", "Automático (como na TV)")], "auto"),
                new("showFor", "Tempo na tela por evento (s)", OptionKind.Number, null, "6", Min: 3, Max: 15),
            ],
            // Vencedor do 2018: banner superior "WINNER | Nome SOBRENOME", pódio com três cartões (2º, vencedor, 3º) ou os dois.
            ["winner"] =
            [
                new("style", "Estilo", OptionKind.Choice,
                    [O("banner", "Banner superior (WINNER)"), O("podium", "Pódio (2º, vencedor, 3º)"), O("both", "Banner e pódio")], "banner"),
                new("showFor", "Tempo na tela (s)", OptionKind.Number, null, "12", Min: 5, Max: 30),
            ],
            // Pit lane do 2018: "PIT LANE" + faixa do piloto + "STOP TIME" ciano; durante a parada "PIT 23.8" no lugar do rotulo.
            ["pittimer"] =
            [
                new("showPitTime", "Tempo na pit lane (\"PIT 23.8\") durante a parada", OptionKind.Toggle, null, "true"),
                new("showPosition", "Caixa de posição", OptionKind.Toggle, null, "true"),
                new("showTick", "Tique da cor da classe", OptionKind.Toggle, null, "true"),
            ],
            // Live Speed do 2018: velocidade do jogador; padrao so com o jogador no carro (regra PlayerDriving), "always" fixa na tela.
            ["livespeed"] =
            [
                new("units", "Unidades", OptionKind.Choice,
                    [O("both", "Ambos (km/h e mph)"), O("kph", "Só km/h"), O("mph", "Só mph")], "both"),
                new("showName", "Nome do piloto", OptionKind.Toggle, null, "true"),
                new("always", "Sempre visível (senão só com o jogador no carro)", OptionKind.Toggle, null, "false"),
            ],
            // Race Start do 2018: aparece ao alcancar o alvo na largada e fica showFor s; "always" mostra sempre o ultimo resultado.
            ["racestart"] =
            [
                new("target", "Alvo", OptionKind.Choice, [O("100", "0-100 km/h"), O("200", "0-200 km/h")], "200"),
                new("showBest", "Mostrar o melhor anterior (BEST) da pista e carro", OptionKind.Toggle, null, "true"),
                new("showFor", "Tempo na tela após alcançar o alvo (s)", OptionKind.Number, null, "10", Min: 5, Max: 30),
                new("always", "Sempre visível (mostra o último resultado)", OptionKind.Toggle, null, "false"),
            ],
            // Battle do 2018: duelo do jogador com o vizinho (da frente, de tras ou o mais proximo) quando o intervalo e menor que gapSeconds.
            ["battle"] =
            [
                new("rival", "Rival", OptionKind.Choice, [O("auto", "O mais próximo (frente ou trás)"), O("ahead", "Carro da frente"), O("behind", "Carro de trás")], "auto"),
                new("gapSeconds", "Aparece com intervalo menor que (s)", OptionKind.Number, null, "1", Min: 0.3, Max: 3),
                new("showDrs", "Indicador DRS (só o jogador; o AMS2 não informa o do rival)", OptionKind.Toggle, null, "true"),
                new("always", "Sempre visível (senão só no duelo e com o jogador no carro)", OptionKind.Toggle, null, "false"),
            ],
            // Legenda de posicao do 2018: dois pilotos lado a lado (nome, numero, equipe, bandeira) a cada mudanca de posicao do jogador.
            ["poscaption"] =
            [
                new("cars", "Pilotos", OptionKind.Choice, [O("rival", "Jogador e vizinho de posição"), O("top2", "Líder e 2º colocado")], "rival"),
                new("showFor", "Tempo na tela por evento (s)", OptionKind.Number, null, "6", Min: 3, Max: 15),
                new("always", "Sempre visível (senão só ao mudar de posição)", OptionKind.Toggle, null, "false"),
            ],
            // Race Control do 2018: caixa da bandeira (amarela/azul/vermelha/xadrez) e barra de parada lenta do jogador (regra em SlowStopDetector).
            ["racecontrol"] =
            [
                new("showFlags", "Caixa de bandeira (YELLOW FLAG / INCIDENT...)", OptionKind.Toggle, null, "true"),
                new("showSlowStop", "Barra de parada lenta (SLOW STOP -x.xs)", OptionKind.Toggle, null, "true"),
                new("slowStopLimit", "Parada lenta: considerada lenta se passar desse tempo parado (s)", OptionKind.Number, null, "5", Min: 3, Max: 30),
                new("showFor", "Tempo na tela da barra de parada lenta (s)", OptionKind.Number, null, "8", Min: 3, Max: 15),
            ],
            // Torre de classificacao do 2018: melhores voltas (1o com tempo, demais +diferenca), relogio "Q", zona de eliminacao,
            // cartao DRIVER AT RISK e modo FASTEST TYRE (o AMS2 so informa o composto do jogador: os outros ficam "-").
            ["qualitower"] =
            [
                new("rows", "Linhas do topo", OptionKind.Number, null, "10", Min: 5, Max: 20),
                new("nearCount", "Pilotos ao redor do jogador", OptionKind.Number, null, "3", Min: 0, Max: 10),
                new("eliminationFrom", "Zona de eliminação: posição do primeiro eliminado (0 = desligada)", OptionKind.Number, null, "0", Min: 0, Max: 30),
                new("mode", "Modo", OptionKind.Choice,
                    [O("time", "Tempos (1º com tempo, demais +diferença)"), O("fastesttyre", "Pneu mais rápido (composto e décimos)")], "time"),
                new("showClock", "Relógio da sessão no cabeçalho", OptionKind.Toggle, null, "true"),
                new("showAtRisk", "Cartão \"DRIVER AT RISK\" (piloto no corte)", OptionKind.Toggle, null, "true"),
            ],
            // Placa de volta do 2018: tempo corrente, comparativo (lider ou melhor pessoal), barra S1 S2 S3, resultado "1:18.917 +0.685 7"
            // e painel "SECTOR n / SOBRENOME / tempo" ao fechar S1/S2.
            ["qualilap"] =
            [
                new("compareTo", "Comparar com", OptionKind.Choice, [O("leader", "Líder (melhor tempo da sessão)"), O("personal", "Melhor volta pessoal")], "leader"),
                new("showSectors", "Barra de setores S1 S2 S3", OptionKind.Toggle, null, "true"),
                new("showSectorPanel", "Painel de setor (SECTOR n) ao fechar S1/S2", OptionKind.Toggle, null, "true"),
                new("showFor", "Tempo na tela do resultado após cruzar a linha (s)", OptionKind.Number, null, "6", Min: 3, Max: 15),
                new("always", "Sempre visível (senão só em volta lançada e no resultado)", OptionKind.Toggle, null, "false"),
            ],
            // Resultado da classificacao do 2018: bloco "ELIMINATED" (uma faixa por eliminado, +gap grande) e tabela "CLASSIFICATION".
            ["qualiresult"] =
            [
                new("rows", "Linhas da tabela (o jogador sempre aparece)", OptionKind.Number, null, "10", Min: 3, Max: 30),
                new("eliminationFrom", "Bloco ELIMINATED: posição do primeiro eliminado (0 = desligado)", OptionKind.Number, null, "0", Min: 0, Max: 30),
                new("maxEliminated", "Bloco ELIMINATED: máximo de pilotos", OptionKind.Number, null, "5", Min: 3, Max: 5),
                new("showFor", "Tempo na tela após o fim da sessão (s)", OptionKind.Number, null, "15", Min: 5, Max: 60),
                new("always", "Sempre visível (mostra a lista atual)", OptionKind.Toggle, null, "false"),
            ],
        },
        ["f1-2004"] = new(StringComparer.OrdinalIgnoreCase)
        {
            // Torre minima de siglas do 2004 (Japao 2008): 1a linha "1 HAM 1:18.232" com o tempo do lider em caixa preta, abaixo so
            // posicao + sigla (topo + ao redor do jogador), numeros vermelhos na zona de eliminacao e caixa do relogio "Q | m:ss".
            ["qualitower"] =
            [
                new("rows", "Linhas do topo (inclui o líder)", OptionKind.Number, null, "5", Min: 1, Max: 20),
                new("nearCount", "Pilotos ao redor do jogador", OptionKind.Number, null, "3", Min: 0, Max: 10),
                new("showClock", "Caixa do relógio da sessão (Q | m:ss)", OptionKind.Toggle, null, "true"),
            ],
            // Barra de volta do 2004: nome em celula branca, tempo em celula preta, faixa de setores e [posicao][+0.471] em laranja.
            ["qualilap"] =
            [
                new("compareTo", "Comparar com", OptionKind.Choice, [O("leader", "Líder (melhor tempo da sessão)"), O("personal", "Melhor volta pessoal")], "leader"),
                new("showSectors", "Faixa de setores S1 S2 S3", OptionKind.Toggle, null, "false"),
                new("showFor", "Tempo na tela do resultado após cruzar a linha (s)", OptionKind.Number, null, "6", Min: 3, Max: 15),
                new("always", "Sempre visível (senão só em volta lançada e no resultado)", OptionKind.Toggle, null, "false"),
            ],
            // Resultado do 2004: faixa de siglas [posicao][SIGLA][tempo/+gap], eliminados com a caixa de posicao vermelha.
            ["qualiresult"] =
            [
                new("rows", "Linhas da lista (o jogador sempre aparece)", OptionKind.Number, null, "10", Min: 3, Max: 30),
                new("showFor", "Tempo na tela após o fim da sessão (s)", OptionKind.Number, null, "15", Min: 5, Max: 60),
                new("always", "Sempre visível (mostra a lista atual)", OptionKind.Toggle, null, "false"),
            ],
        },
        ["f1-2012"] = new(StringComparer.OrdinalIgnoreCase)
        {
            // Torre minima de siglas do 2004 (Japao 2008): 1a linha "1 HAM 1:18.232" com o tempo do lider em caixa preta, abaixo so
            // posicao + sigla (topo + ao redor do jogador), numeros vermelhos na zona de eliminacao e caixa do relogio "Q | m:ss".
            ["qualitower"] =
            [
                new("rows", "Linhas do topo (inclui o líder)", OptionKind.Number, null, "5", Min: 1, Max: 20),
                new("nearCount", "Pilotos ao redor do jogador", OptionKind.Number, null, "3", Min: 0, Max: 10),
                new("showClock", "Caixa do relógio da sessão (Q | m:ss)", OptionKind.Toggle, null, "true"),
            ],
            // Barra de volta do 2004: nome em celula branca, tempo em celula preta, faixa de setores e [posicao][+0.471] em laranja.
            ["qualilap"] =
            [
                new("compareTo", "Comparar com", OptionKind.Choice, [O("leader", "Líder (melhor tempo da sessão)"), O("personal", "Melhor volta pessoal")], "leader"),
                new("showSectors", "Faixa de setores S1 S2 S3", OptionKind.Toggle, null, "false"),
                new("showFor", "Tempo na tela do resultado após cruzar a linha (s)", OptionKind.Number, null, "6", Min: 3, Max: 15),
                new("always", "Sempre visível (senão só em volta lançada e no resultado)", OptionKind.Toggle, null, "false"),
            ],
            // Resultado do 2004: faixa de siglas [posicao][SIGLA][tempo/+gap], eliminados com a caixa de posicao vermelha.
            ["qualiresult"] =
            [
                new("rows", "Linhas da lista (o jogador sempre aparece)", OptionKind.Number, null, "10", Min: 3, Max: 30),
                new("showFor", "Tempo na tela após o fim da sessão (s)", OptionKind.Number, null, "15", Min: 5, Max: 60),
                new("always", "Sempre visível (mostra a lista atual)", OptionKind.Toggle, null, "false"),
            ],
            ["battle"] =
            [
                new("rival", "Rival", OptionKind.Choice, [O("auto", "O mais próximo (frente ou trás)"), O("ahead", "Carro da frente"), O("behind", "Carro de trás")], "auto"),
                new("gapSeconds", "Aparece com intervalo menor que (s)", OptionKind.Number, null, "1", Min: 0.3, Max: 3),
                new("showDrs", "Indicador DRS (só o jogador; o AMS2 não informa o do rival)", OptionKind.Toggle, null, "true"),
                new("always", "Sempre visível (senão só no duelo e com o jogador no carro)", OptionKind.Toggle, null, "false"),
            ],
            // Faixa inferior do 2012: modo do valor (intervalo para o carro da frente ou gap para o lider) e tempo de cada pagina de 5 pilotos.
            ["ticker"] =
            [
                new("mode", "Valor", OptionKind.Choice, [O("interval", "Intervalo (carro da frente)"), O("gap", "Gap para o líder")], "interval"),
                new("pageSeconds", "Segundos por página (5 pilotos)", OptionKind.Number, null, "6", Min: 3, Max: 15),
            ],
        },
        ["f1-1993"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["board"] = [
                new("gapPointPercent", "Ponto de medição do gap (% da volta; 0 = chegada)", OptionKind.Number, null, "0", Min: 0, Max: 99.9),
                new("gapArrow", "Seta na placa de gap", OptionKind.Toggle, null, "true"),
                new("gapHoldSeconds", "Duração do resultado do gap (s)", OptionKind.Number, null, "6", Min: 3, Max: 15),
                new("showFastest", "Mostrar nova volta mais rápida da corrida", OptionKind.Toggle, null, "true"),
                new("captionMode", "Legenda", OptionKind.Choice, [O("full", "Nome e equipe"), O("onboard", "Sobrenome a bordo")], "full"),
            ],
            ["qualiboard"] = [
                new("compareTo", "Comparar com", OptionKind.Choice, [O("leader", "Líder"), O("personal", "Melhor volta pessoal")], "leader"),
                new("showFor", "Duração do resultado da volta (s)", OptionKind.Number, null, "6", Min: 3, Max: 15),
            ],
            ["drivercaption"] = [new("captionMode", "Legenda", OptionKind.Choice, [O("full", "Nome e equipe"), O("onboard", "Sobrenome a bordo")], "full")],
            // Listas discretas da TV de 1993: número de carro + sobrenome; mostram sempre o topo (sem janela ao redor do jogador).
            ["standings"] = [new("rows", "Pilotos na lista", OptionKind.Number, null, "6", Min: 3, Max: 20)],
            ["qualitower"] = [new("rows", "Pilotos na lista", OptionKind.Number, null, "6", Min: 3, Max: 20)],
        },
        ["f1-1998"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["qualiboard"] =
            [
                new("compareTo", "Comparar com", OptionKind.Choice, [O("leader", "Líder (melhor tempo da sessão)"), O("personal", "Melhor volta pessoal")], "leader"),
                new("showSpeed", "Velocidade na passagem pelas intermediárias e chegada", OptionKind.Toggle, null, "true"),
                new("showFor", "Retenção do resultado da volta (s)", OptionKind.Number, null, "6", Min: 3, Max: 15),
            ],
            // Lista de classificacao do 1998 (Monaco 2003): colunas de caixas amarelas + NOME, 1o com o tempo, demais a diferenca sem "+".
            ["qualitower"] =
            [
                new("rows", "Pilotos na lista (o jogador sempre aparece)", OptionKind.Number, null, "8", Min: 2, Max: 10),
                new("columns", "Colunas", OptionKind.Choice, [O("2", "Duas colunas (como na TV)"), O("1", "Uma coluna")], "2"),
                new("eliminationFrom", "Zona de eliminação: posição do primeiro eliminado (0 = desligada)", OptionKind.Number, null, "0", Min: 0, Max: 30),
                new("showClock", "Cabeçalho com o relógio da sessão", OptionKind.Toggle, null, "false"),
            ],
            // Tempo corrente do 1998: NOME + tempo com sombra; no resultado tempo da volta, diferenca e "FINISH LINE".
            ["qualilap"] =
            [
                new("compareTo", "Comparar com", OptionKind.Choice, [O("leader", "Líder (melhor tempo da sessão)"), O("personal", "Melhor volta pessoal")], "leader"),
                new("showSpeed", "Velocidade na linha de chegada (resultado)", OptionKind.Toggle, null, "true"),
                new("showFor", "Tempo na tela do resultado após cruzar a linha (s)", OptionKind.Number, null, "6", Min: 3, Max: 15),
                new("always", "Sempre visível (senão só em volta lançada e no resultado)", OptionKind.Toggle, null, "false"),
            ],
            // Resultado do 1998: lista completa em duas colunas [caixa amarela][NOME], 1o com o tempo, demais a diferenca sem "+".
            ["qualiresult"] =
            [
                new("rows", "Pilotos na lista (o jogador sempre aparece)", OptionKind.Number, null, "10", Min: 3, Max: 30),
                new("eliminationFrom", "Eliminados: posição do primeiro eliminado (0 = desligado)", OptionKind.Number, null, "0", Min: 0, Max: 30),
                new("showFor", "Tempo na tela após o fim da sessão (s)", OptionKind.Number, null, "15", Min: 5, Max: 60),
                new("always", "Sempre visível (mostra a lista atual)", OptionKind.Toggle, null, "false"),
            ],
        },
    };

    /// <summary>
    /// Opcoes do GPxSAT (gpxsat.ini do GPxPatch), iguais nos quatro temas. Padroes = os do GPx; o passo da animacao e 2x FadingSpeed.
    /// </summary>
    static readonly IReadOnlyList<OptionDef> GpxSatOptions =
    [
        new("showMode", "ShowMode: telas exibidas", OptionKind.Choice,
            [O("all", "Todas"), O("sky", "Só sky messages + time left"), O("none", "Nenhuma")], "all"),
        new("showTimeLeft", "Time left / lap counter no topo", OptionKind.Toggle, null, "true"),
        new("raceStandings", "Corrida: standings", OptionKind.Choice,
            [O("off", "Desligado (SpeedSplit)"), O("standings", "Classificação"), O("pitstops", "Pit stops"), O("interval", "Intervalos (2010)")], "off"),
        new("qualiStandings", "Fora da corrida: standings de classificação", OptionKind.Toggle, null, "true"),
        new("speedSplitRace", "Corrida: SpeedSplit", OptionKind.Choice,
            [O("nearest", "Gap para o carro mais próximo"), O("ahead", "Gap para o carro à frente"), O("behind", "Gap para o carro atrás"), O("trap", "Speed trap")], "nearest"),
        new("speedSplitQuali", "Fora da corrida: SpeedSplit", OptionKind.Choice,
            [O("split", "Split de quali"), O("trap", "Speed trap")], "split"),
        new("directorsCut", "Director's cut (corrida)", OptionKind.Toggle, null, "false"),
        new("showLogo", "Logo do patrocinador ao fim da animação", OptionKind.Toggle, null, "false"),
        new("fadingSpeed", "FadingSpeed (passo = 2x, ms)", OptionKind.Number, null, "10", Min: 1, Max: 50),
        new("skySeconds", "Sky messages na tela (s)", OptionKind.Number, null, "8", Min: 2, Max: 30),
        new("splitSeconds", "Splits/gaps/trap na tela (s)", OptionKind.Number, null, "8", Min: 2, Max: 30),
        new("pageSeconds", "Standings: segundos por página", OptionKind.Number, null, "6", Min: 2, Max: 30),
        new("rotation", "Standings da corrida só quando o líder fecha a volta (janela finita; sem ciclo contínuo)", OptionKind.Toggle, null, "true"),
        new("bottomTower", "2010: torre inferior", OptionKind.Choice, [O("off", "Desligada"), O("standings", "Gap ao líder"), O("pitstops", "Pit stops"), O("interval", "Intervalos")], "off"),
        new("raceProgression", "2010: torre lateral de corrida", OptionKind.Toggle, null, "true"),
        new("qualiMode", "2010: valor na torre de classificação", OptionKind.Choice, [O("gap", "Gap ao líder"), O("absolute", "Tempo absoluto")], "gap"),
        new("raceTowerCars", "2010: carros na torre de corrida", OptionKind.Number, null, "10", Min: 1, Max: 32),
        new("qualiTowerCars", "2010: carros na torre de classificação", OptionKind.Number, null, "22", Min: 1, Max: 32),
        new("bottomCars", "2010: carros na torre inferior", OptionKind.Number, null, "5", Min: 1, Max: 10),
        new("alwaysTower", "2010: manter torre com standings", OptionKind.Toggle, null, "false"),
        new("showLapInStandings", "2010: mostrar volta nas standings", OptionKind.Toggle, null, "true"),
        new("lapsLeftMode", "Contador de voltas", OptionKind.Choice, [O("remaining", "Voltas restantes"), O("current", "Volta atual")], "remaining"),
        new("microTime", "Microtime", OptionKind.Toggle, null, "false"),
        new("microCompare", "Microtime: referência", OptionKind.Choice, [O("leader", "Melhor da sessão"), O("personal", "Melhor pessoal")], "leader"),
        new("microAbsolute", "Microtime: tempos absolutos (senão delta)", OptionKind.Toggle, null, "false"),
        new("infoScreen", "Lista de modos ativos", OptionKind.Toggle, null, "false"),
        new("trackMap", "Mapa aprendido da pista", OptionKind.Choice, [O("off", "Desligado"), O("always", "Sempre"), O("auto", "Bandeira amarela")], "auto"),
        new("originalFonts", "Fontes originais do GPxPatch (fonts/gpxpatch ou GP3; Barlow só se faltarem)", OptionKind.Toggle, null, "true"),
        new("aspect", "Área 4:3", OptionKind.Choice, [O("pillarbox", "Centrada 4:3 (como o GPx)"), O("stretch", "Esticada na janela")], "pillarbox"),
    ];

    /// <summary>Opcoes do widget no tema (vazio = nenhuma).</summary>
    public static IReadOnlyList<OptionDef> OptionsFor(string themeId, string widgetId)
        => string.Equals(widgetId, "gpxsat", StringComparison.OrdinalIgnoreCase) ? (Find("gpxsat")!.InTheme(themeId) ? GpxSatOptions.Select(o => o.Id == "showTimeLeft" && ThemeCatalog.Canonical(themeId) == "f1-1998" ? o with { Default = "false" } : o).ToArray() : [])
        : ThemeOptions.TryGetValue(Is99(themeId) ? "f1-1998" : ThemeCatalog.Canonical(themeId), out var t) && t.TryGetValue(widgetId, out var o) ? o : [];
}

public sealed record ThemeDef(string Id, string DisplayName, bool Available);

public static class ThemeCatalog
{
    public const string Default = "f1-1998";

    /// <summary>Temas conhecidos. Os ainda nao implementados aparecem desabilitados no Control Center.</summary>
    public static readonly IReadOnlyList<ThemeDef> All =
    [
        new("f1-1993", "F1 1993", true),
        new("f1-1998", "F1 1999", true),
        new("f1-1999-digital", "F1 1999 Digital", true),
        new("f1-2004", "F1 2004-2008", true),
        new("f1-2012", "F1 2012", true),
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
