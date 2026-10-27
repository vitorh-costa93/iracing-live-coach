using Ams2.Core.Calc.GpxSat;

namespace Ams2.Core.Tests;

/// <summary>Comparativo de melhor volta (peca "lapcompare"): vizinho da linha de chegada pelo criterio do "nearest" do gap e conteudo (melhor volta do jogador vs a do vizinho).</summary>
public class GpxLapCompareTests
{
    static CarSnapshot Car(int id, int position, double best = 80) => new(id, "Driver " + id, "Car", "F1", position, position,
        1, 2, 0, 0, best, 80, 50, PitState.None, RaceState.Racing, false, id == 0);
    static SessionSnapshot Session(SessionKind kind, params CarSnapshot[] cars) => new(14, 1, true, kind, "Track", "", 5000, 20, null, 0,
        new(20, 30, 0, 0, 0, 0, 0, 0), cars, new(0, new(0,0,0,0,0,0,0,0),3,9000,18000,50,40,100,[],.5));

    /// <summary>Uma volta inteira (S1, S2 e linha); o jogador cruza S2 em t=23 (a frente em 20, o de tras em 23 + <paramref name="behindGap"/>) e a linha em 34.</summary>
    static (GpxSplitTracker T, CarSnapshot[] Cars, SessionSnapshot S) Lap(int focusPos, int count, double behindGap = 1, double playerBest = 80, SessionKind kind = SessionKind.Race)
    {
        // indices: 0 = jogador; 1 = P-1 (se existir); 2 = P+1 (se existir)
        var byIndex = new Dictionary<int, CarSnapshot> { [0] = Car(0, focusPos, playerBest) };
        if (focusPos > 1) byIndex[1] = Car(1, focusPos - 1);
        if (focusPos < count) byIndex[2] = Car(2, focusPos + 1);
        var t = new GpxSplitTracker();
        SessionSnapshot S() => Session(kind, byIndex.Values.ToArray());
        void Set(double now, int id, int laps, int sector) { byIndex[id] = byIndex[id] with { LapsCompleted = laps, Sector = sector }; t.Update(now, S()); }
        int ahead = byIndex.ContainsKey(1) ? 1 : 0, behind = byIndex.ContainsKey(2) ? 2 : 0;
        t.Update(0, S());
        var events = new List<(double T, int Id, int Laps, int Sector)>
        {
            (12, 0, 1, 1), (23, 0, 1, 2), (34, 0, 2, 0),
        };
        if (ahead != 0) events.AddRange([(10, ahead, 1, 1), (20, ahead, 1, 2), (30, ahead, 2, 0)]);
        if (behind != 0) events.AddRange([(13, behind, 1, 1), (23 + behindGap, behind, 1, 2), (36, behind, 2, 0)]);
        foreach (var e in events.OrderBy(e => e.T)) Set(e.T, e.Id, e.Laps, e.Sector);
        return (t, [.. byIndex.Values], S());
    }

    [Fact]
    public void Picks_the_car_behind_when_its_gap_is_smaller_and_the_one_ahead_otherwise()
    {
        var (t, _, _) = Lap(2, 3, behindGap: 1);   // a frente 3 s, atras 1 s
        var n = t.LapNeighbor!;
        Assert.False(n.Ahead); Assert.Equal(2, n.Other); Assert.Equal(2, n.Lap); Assert.Equal(1, n.Gap!.Value, 3);
        var (t2, _, _) = Lap(2, 3, behindGap: 5);   // atras 5 s: o da frente (3 s) vence
        Assert.True(t2.LapNeighbor!.Ahead); Assert.Equal(1, t2.LapNeighbor.Other); Assert.Equal(3, t2.LapNeighbor.Gap!.Value, 3);
    }

    [Fact]
    public void Leader_uses_the_car_behind_and_the_last_one_the_car_ahead()
    {
        var (tl, _, _) = Lap(1, 2);
        Assert.False(tl.LapNeighbor!.Ahead); Assert.Equal(2, tl.LapNeighbor.Other);   // lider: so existe o de tras
        var (tz, _, _) = Lap(2, 2);
        Assert.True(tz.LapNeighbor!.Ahead); Assert.Equal(1, tz.LapNeighbor.Other);    // ultimo: so existe o da frente
    }

    [Fact]
    public void No_neighbor_outside_the_race_or_without_a_line_crossing()
    {
        var (tq, _, _) = Lap(2, 3, kind: SessionKind.Qualify);
        Assert.Null(tq.LapNeighbor);
        var t = new GpxSplitTracker();
        Assert.Null(t.LapNeighbor);
    }

    [Fact]
    public void Content_is_the_player_best_vs_the_neighbor_best_and_needs_a_valid_player_best()
    {
        var (t, cars, s) = Lap(2, 3, behindGap: 1, playerBest: 80.5);
        var behind = cars.First(c => c.Index == 2) with { BestLapTime = 80.25 };
        var live = Session(SessionKind.Race, cars.Select(c => c.Index == 2 ? behind : c).ToArray());
        var cmp = GpxLapCompare.Resolve(live, t.LapNeighbor)!;
        Assert.Equal((80.5, 80.25), (cmp.PlayerBest, cmp.OtherBest!.Value));
        Assert.Equal(0.25, cmp.Delta!.Value, 6);   // positivo = jogador mais lento
        Assert.False(cmp.OtherAhead); Assert.Equal(2, cmp.Other.Index);
        // Vizinho sem melhor volta: mostra o do jogador e nenhuma diferenca.
        var none = GpxLapCompare.Resolve(Session(SessionKind.Race, cars.Select(c => c.Index == 2 ? c with { BestLapTime = 0 } : c).ToArray()), t.LapNeighbor)!;
        Assert.Null(none.OtherBest); Assert.Null(none.Delta);
        // Jogador sem melhor volta valida: sem tela.
        Assert.Null(GpxLapCompare.Resolve(Session(SessionKind.Race, cars.Select(c => c.Index == 0 ? c with { BestLapTime = 0 } : c).ToArray()), t.LapNeighbor));
        Assert.Null(GpxLapCompare.Resolve(Session(SessionKind.Race, cars.Select(c => c.Index == 0 ? c with { BestLapTime = double.NaN } : c).ToArray()), t.LapNeighbor));
        // Fora da corrida, sem vizinho ou de outra volta: sem tela.
        Assert.Null(GpxLapCompare.Resolve(Session(SessionKind.Qualify, cars), t.LapNeighbor));
        Assert.Null(GpxLapCompare.Resolve(s, null));
        Assert.Null(GpxLapCompare.Resolve(Session(SessionKind.Race, cars.Select(c => c.Index == 0 ? c with { LapsCompleted = 3 } : c).ToArray()), t.LapNeighbor));
    }

    [Fact]
    public void Neighbor_is_dropped_when_the_line_is_crossed_in_the_pit_lane()
    {
        var (t, cars, _) = Lap(2, 3);
        Assert.NotNull(t.LapNeighbor);
        t.Update(60, Session(SessionKind.Race, cars.Select(c => c.Index == 0 ? c with { LapsCompleted = 2, Sector = 2 } : c).ToArray()));
        t.Update(70, Session(SessionKind.Race, cars.Select(c => c.Index == 0 ? c with { LapsCompleted = 3, Sector = 0, PitState = PitState.InPit } : c).ToArray()));
        Assert.Null(t.LapNeighbor);
    }
}
