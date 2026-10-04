namespace Ams2.Core.Calc;

/// <summary>Estado de um piloto na tabela de classificacao (prioridade: InPit &gt; OutLap &gt; TimeSet/NoTime; na garagem sem tempo = NoTime).</summary>
public enum QualiStatus { TimeSet, OutLap, NoTime, InPit }

/// <summary>
/// Linha da tabela de melhores voltas. <see cref="BestLap"/> null = sem tempo; <see cref="GapToFirst"/> = 0 no 1o, null sem tempo.
/// <see cref="Status"/> e o estado atual (um piloto com tempo pode estar em OUT LAP ou IN PIT): o widget decide o que mostrar.
/// </summary>
public sealed record QualiRow(CarSnapshot Car, int Rank, double? BestLap, double? GapToFirst, QualiStatus Status, bool IsPlayer)
{
    public bool HasTime => BestLap is not null;
    /// <summary>Zona de eliminacao: posicao igual ou pior que <paramref name="cutoff"/> (opcao "eliminationFrom"; 0 = desligado).</summary>
    public bool InEliminationZone(int cutoff) => cutoff > 0 && Rank >= cutoff;
}

/// <summary>Tabela da classificacao: linhas ordenadas e relogio da sessao (s restantes; null = desconhecido).</summary>
public sealed record QualiTableState(IReadOnlyList<QualiRow> Rows, double? TimeRemaining)
{
    public static readonly QualiTableState Empty = new([], null);
    public QualiRow? Leader => Rows.Count > 0 && Rows[0].HasTime ? Rows[0] : null;
    public QualiRow? Player => Rows.FirstOrDefault(r => r.IsPlayer);
    public QualiRow? Row(int carIndex) => Rows.FirstOrDefault(r => r.Car.Index == carIndex);
}

/// <summary>
/// Tabela de melhores voltas (pura): com tempo primeiro, por <see cref="CarSnapshot.BestLapTime"/> (empate: posicao, depois indice);
/// sem tempo por ultimo, na ordem de posicao do jogo (posicao 0 = fim). Volta de saida vem de <paramref name="isOutLap"/>
/// (<see cref="OutLapTracker"/> no provider); alem dele, "sem volta completa e sem tempo, na pista" tambem conta como OUT LAP.
/// </summary>
public static class QualiTable
{
    public static QualiTableState Build(SessionSnapshot s, Func<CarSnapshot, bool>? isOutLap = null)
    {
        if (s.Cars.Count == 0) return new QualiTableState([], s.TimeRemainingSeconds);
        var timed = s.Cars.Where(c => c.BestLapTime > 0).OrderBy(c => c.BestLapTime).ThenBy(c => c.Position <= 0 ? int.MaxValue : c.Position).ThenBy(c => c.Index);
        var untimed = s.Cars.Where(c => c.BestLapTime <= 0).OrderBy(c => c.Position <= 0 ? int.MaxValue : c.Position).ThenBy(c => c.Index);
        var rows = new List<QualiRow>(s.Cars.Count);
        double? pole = null;
        foreach (var c in timed.Concat(untimed))
        {
            double? best = c.BestLapTime > 0 ? c.BestLapTime : null;
            pole ??= best;
            bool outLap = (isOutLap?.Invoke(c) ?? false) || best is null && c.LapsCompleted == 0 && !c.InGarage;
            // Na garagem sem tempo = NO TIME (ainda nao saiu); com tempo, ou no pit lane, = IN PIT.
            var st = c.InPitLane || c.InGarage && best is not null ? QualiStatus.InPit : outLap ? QualiStatus.OutLap : best is null ? QualiStatus.NoTime : QualiStatus.TimeSet;
            rows.Add(new QualiRow(c, rows.Count + 1, best, best is { } b && pole is { } p ? b - p : null, st, c.IsPlayer));
        }
        return new QualiTableState(rows, s.TimeRemainingSeconds);
    }
}

/// <summary>
/// Volta de saida: a volta (CurrentLap) em que o carro foi visto pela ultima vez no pit lane ou na garagem continua sendo "OUT LAP"
/// depois que ele volta para a pista, ate cruzar a linha (CurrentLap muda). Vale com o box antes ou depois da linha.
/// </summary>
public sealed class OutLapTracker
{
    const int MaxCars = 64;
    readonly int[] _pitLap = Enumerable.Repeat(-1, MaxCars).ToArray();

    public void Reset() => Array.Fill(_pitLap, -1);

    public void Update(SessionSnapshot s)
    {
        foreach (var c in s.Cars)
            if (c.Index is >= 0 and < MaxCars && (c.InPitLane || c.InGarage)) _pitLap[c.Index] = c.CurrentLap;
    }

    public bool IsOutLap(CarSnapshot c)
        => c.Index is >= 0 and < MaxCars && !c.InPitLane && !c.InGarage && _pitLap[c.Index] >= 0 && _pitLap[c.Index] == c.CurrentLap;
}
