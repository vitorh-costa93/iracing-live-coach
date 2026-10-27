namespace Ams2.Core.Calc.GpxSat;

/// <summary>
/// Comparativo de melhor volta do GPxSAT (peca "lapcompare", a cada 3 voltas, so corrida): a melhor volta do jogador contra a do carro mais proximo
/// (<see cref="GpxSplitTracker.LapNeighbor"/>: mesmo criterio do "nearest" do gap). <see cref="Delta"/> = jogador - vizinho (negativo = jogador mais rapido);
/// null quando o vizinho ainda nao tem melhor volta.
/// </summary>
public sealed record GpxLapCompare(CarSnapshot Focus, CarSnapshot Other, bool OtherAhead, double PlayerBest, double? OtherBest, double? Delta)
{
    /// <summary>
    /// Null = nada a comparar: fora da corrida, sem carro do jogador, sem vizinho da ultima linha de chegada (ou de outra volta),
    /// ou o jogador sem melhor volta valida. Os tempos sao lidos do quadro atual (a melhor volta do vizinho pode melhorar na janela).
    /// </summary>
    public static GpxLapCompare? Resolve(SessionSnapshot s, GpxLapNeighbor? n)
    {
        if (s.Kind != SessionKind.Race || n is null || s.PlayerCar is not { } me || me.Index != n.Focus) return null;
        if (me.LapsCompleted != n.Lap || !(me.BestLapTime > 0) || !double.IsFinite(me.BestLapTime)) return null;
        var other = s.Cars.FirstOrDefault(c => c.Index == n.Other);
        if (other is null) return null;
        double? ob = other.BestLapTime > 0 && double.IsFinite(other.BestLapTime) ? other.BestLapTime : null;
        return new GpxLapCompare(me, other, n.Ahead, me.BestLapTime, ob, ob is { } o ? me.BestLapTime - o : null);
    }
}
