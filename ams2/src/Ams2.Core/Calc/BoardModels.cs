using System.Globalization;

namespace Ams2.Core.Calc;

/// <summary>Modo do widget rotativo inferior ("board"). Ordem numérica = prioridade (maior sobrepõe menor).</summary>
public enum BoardMode { None = 0, DriverPlate = 1, LapComparison = 2, SectorGap = 3, LineTower = 4 }

/// <summary>Tipo do valor da coluna de gap da torre: líder ("Lap N"), intervalo em tempo ("+0.239") ou voltas ("+1L").</summary>
public enum BoardGapKind { Leader, Time, Laps }

/// <summary>Parâmetros do board (ver ams2/reference/board-spec.md).</summary>
public sealed record BoardOptions
{
    /// <summary>Pilotos por página da torre (metade à esquerda, metade à direita).</summary>
    public int PageSize { get; init; } = 8;
    /// <summary>Tempo que uma página cheia (ou a última, parcial) fica na tela.</summary>
    public double PageHoldSeconds { get; init; } = 4;
    /// <summary>Página ainda incompleta sem nenhum cruzamento novo por este tempo encerra a rodada (carro parado na pista).</summary>
    public double TowerStallSeconds { get; init; } = 30;
    /// <summary>A janela de setor fecha este tempo depois que o último carro cruza a marca.</summary>
    public double SectorCloseDelaySeconds { get; init; } = 2;
    /// <summary>Teto de segurança da janela de setor, contado da abertura.</summary>
    public double SectorMaxWindowSeconds { get; init; } = 40;
    /// <summary>Diferença de |gap| abaixo da qual o vizinho da frente é preferido ao de trás.</summary>
    public double NeighborTieSeconds { get; init; } = 0.05;
    /// <summary>O comparativo aparece quando as voltas completas do jogador são múltiplas deste valor (e ≥ ele).</summary>
    public int LapComparisonEvery { get; init; } = 3;
    /// <summary>Quantas voltas o comparativo lista (a última completa e as anteriores).</summary>
    public int LapComparisonLaps { get; init; } = 3;
    /// <summary>Espera máxima pelo LastLapTime novo depois que LapsCompleted sobe (o jogo pode atualizar com atraso).</summary>
    public double LapTimeSettleSeconds { get; init; } = 1;

    public static readonly BoardOptions Default = new();
}

/// <summary>Piloto pronto para exibição. <see cref="Position"/> = posição oficial do jogo no momento.</summary>
public sealed record BoardDriver(
    int CarIndex,
    int Position,
    string Name,          // nome completo do jogo
    string ShortName,     // sobrenome ("Alonso"); com sobrenome repetido no grid, "M Schumacher"
    string Code,          // sigla de 3 letras ("ALO")
    string Team,          // nome do carro sem "(M)"/"(B)" e sem o nome da classe
    string Nationality,   // ISO alpha-2 minúsculo; "" = sem bandeira
    string TyreSupplier,  // "M", "B" ou ""
    bool IsPlayer);

/// <summary>
/// Um piloto na página da torre da linha. <see cref="PageSlot"/> 1..PageSize; <see cref="Column"/> 0 = esquerda (slots 1–4),
/// 1 = direita (5–8); <see cref="Row"/> 0..3 de cima para baixo. <see cref="Position"/> = ordem de passagem pela linha nesta
/// rodada (1 = líder); <see cref="RacePosition"/> = posição oficial do jogo no instante do cruzamento.
/// Gap: <see cref="GapKind"/> Leader → <see cref="GapLaps"/> = volta do líder (texto "Lap N"); Time → <see cref="GapSeconds"/>
/// desde a passagem do líder ("+0.239"); Laps → <see cref="GapLaps"/> voltas atrás ("+1L").
/// </summary>
public sealed record BoardTowerEntry(
    int PageSlot,
    int Column,
    int Row,
    int Position,
    int RacePosition,
    int CarIndex,
    string Name,
    string ShortName,
    string Code,
    BoardGapKind GapKind,
    double GapSeconds,
    int GapLaps,
    string GapText,
    bool IsPlayer,
    string Nationality,
    string TyreSupplier,
    double CrossedT);

/// <summary>
/// Página atual da torre da linha. <see cref="Entries"/> já é só a página exibida (até PageSize itens, na ordem dos slots).
/// <see cref="PageCount"/> = páginas conhecidas até agora (cresce com <see cref="ExpectedCount"/>).
/// </summary>
public sealed record BoardTower(
    int LeaderLap,
    int PageIndex,
    int PageCount,
    int PageSize,
    IReadOnlyList<BoardTowerEntry> Entries,
    int CrossedCount,
    int ExpectedCount,
    bool Complete,
    double PageStartT,
    double PageEndT);

/// <summary>
/// Gráfico de gap na passagem de setor. <see cref="Sector"/> 1..3 (3 = linha). <see cref="GapSeconds"/> com sinal:
/// positivo = vizinho à frente do jogador; negativo = vizinho atrás. <see cref="IsSplit"/> = os dois já cruzaram a marca e o
/// gap é a diferença exata entre os cruzamentos (congelado); senão é o gap ao vivo do GapTracker (ou estimativa por distância).
/// <see cref="CloseT"/> = fim previsto da janela (teto enquanto o último carro não cruzou).
/// </summary>
public sealed record BoardSectorGap(
    int Sector,
    BoardDriver Player,
    BoardDriver Neighbor,
    bool NeighborAhead,
    double GapSeconds,
    bool IsSplit,
    string GapText,
    double OpenT,
    double CloseT);

/// <summary>Uma volta do comparativo. Delta = jogador − vizinho (negativo = jogador mais rápido, verde). null = sem tempo.</summary>
public sealed record BoardLapRow(int Lap, double? PlayerTime, double? NeighborTime, double? Delta, bool PlayerFaster);

/// <summary>Comparativo das últimas voltas completas (mais recente primeiro: "Lap 27 / 26 / 25").</summary>
public sealed record BoardLapComparison(
    int PlayerLapsCompleted,
    BoardDriver Player,
    BoardDriver Neighbor,
    bool NeighborAhead,
    IReadOnlyList<BoardLapRow> Laps);

/// <summary>
/// Saída imutável do <see cref="BoardTracker"/>, publicada a cada quadro. <see cref="Mode"/> diz o que desenhar; os blocos de
/// dados dos outros modos podem vir preenchidos (ex.: <see cref="Plate"/> existe sempre que há jogador).
/// <see cref="Revision"/> sobe em toda mudança estrutural (modo, página, novo piloto na página, nova janela, novo vizinho):
/// o widget usa para disparar fade-in/animação. <see cref="RemainingSeconds"/> = tempo até o fim previsto do modo atual
/// (<see cref="double.PositiveInfinity"/> para DriverPlate/LapComparison, que duram enquanto o intervalo estiver livre).
/// </summary>
public sealed record BoardState(
    BoardMode Mode,
    long Revision,
    double Now,
    bool IsRace,
    double WindowStartT,
    double WindowEndT,
    double RemainingSeconds,
    int ItemCount,
    BoardTower? Tower,
    BoardSectorGap? SectorGap,
    BoardLapComparison? LapComparison,
    BoardDriver? Plate)
{
    public static readonly BoardState Empty = new(BoardMode.None, 0, 0, false, double.NegativeInfinity, double.PositiveInfinity,
        double.PositiveInfinity, 0, null, null, null, null);

    /// <summary>Itens da página atual da torre (vazio fora do modo LineTower).</summary>
    public IReadOnlyList<BoardTowerEntry> Page => Tower?.Entries ?? [];
    /// <summary>Segundos desde o início do modo/página atual.</summary>
    public double ElapsedSeconds => Now - WindowStartT;
}

/// <summary>Textos derivados do modelo usados pelo board (equipe, siglas, gaps). Sem dependência de UI.</summary>
public static class BoardText
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Sobrenome do piloto; com sobrenome repetido no grid, inicial + sobrenome ("M Schumacher").</summary>
    public static string ShortName(CarSnapshot car, IEnumerable<CarSnapshot> field)
    {
        var parts = car.Name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "";
        if (parts.Length == 1) return parts[0];
        string last = string.Join(' ', parts[1..]);
        bool dup = field.Any(o => o.Index != car.Index && o.Name.Trim().EndsWith(last, StringComparison.OrdinalIgnoreCase));
        return dup ? parts[0][0] + " " + last : last;
    }

    /// <summary>Sigla de 3 letras: início do sobrenome (última palavra), em maiúsculas.</summary>
    public static string Code(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string last = parts.Length > 0 ? parts[^1] : name;
        last = new string(last.Where(char.IsLetter).ToArray());
        return (last.Length >= 3 ? last[..3] : last).ToUpperInvariant();
    }

    /// <summary>Equipe deduzida do nome do carro: sem o sufixo de fornecedor "(M)"/"(B)" e sem o nome da classe.</summary>
    public static string Team(CarSnapshot car)
    {
        string t = car.CarName.Trim();
        int p = t.LastIndexOf('(');
        if (p > 0 && t.EndsWith(')')) t = t[..p].Trim();
        if (car.ClassName.Length > 0 && t.Length > car.ClassName.Length && t.StartsWith(car.ClassName, StringComparison.OrdinalIgnoreCase)) t = t[car.ClassName.Length..].Trim();
        else if (car.ClassName.Length > 0 && t.Length > car.ClassName.Length && t.EndsWith(car.ClassName, StringComparison.OrdinalIgnoreCase)) t = t[..^car.ClassName.Length].Trim();
        return t.Length > 0 ? t : car.CarName.Trim();
    }

    public static BoardDriver Driver(CarSnapshot c, IEnumerable<CarSnapshot> field) =>
        new(c.Index, c.Position, c.Name, ShortName(c, field), Code(c.Name), Team(c), c.Nationality, c.TyreSupplier, c.IsPlayer);

    /// <summary>"+0.239", "+12.345"; a partir de 60 s "+1:02.345". Negativos com "-".</summary>
    public static string Gap(double seconds)
    {
        string sign = seconds < 0 ? "-" : "+";
        double a = Math.Abs(seconds);
        if (a >= 60) { int m = (int)(a / 60); return sign + m.ToString(Inv) + ":" + (a - m * 60).ToString("00.000", Inv); }
        return sign + a.ToString("0.000", Inv);
    }

    public static string Laps(int laps) => "+" + laps.ToString(Inv) + "L";
    public static string LeaderLap(int lap) => "Lap " + lap.ToString(Inv);
}
