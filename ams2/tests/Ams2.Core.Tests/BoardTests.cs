using Ams2.Core.Calc;

namespace Ams2.Core.Tests;

/// <summary>Bancada: Sim + GapTracker + BoardTracker com o relógio do Sim (60 Hz por padrão).</summary>
sealed class BoardRig
{
    public readonly Sim Sim;
    public readonly GapTracker Gaps = new();
    public readonly BoardTracker Board;
    public BoardState State = BoardState.Empty;
    public double Dt = 1.0 / 60;

    public BoardRig(Sim sim, BoardOptions? o = null) { Sim = sim; Board = new BoardTracker(o); Tick(); }

    public BoardState Tick()
    {
        var s = Sim.Snapshot();
        Gaps.Update(Sim.Now, s.TrackLength, s.Cars);
        return State = Board.Update(Sim.Now, s, Gaps);
    }

    /// <summary>Avança até o instante <paramref name="t"/>; <paramref name="each"/> roda a cada quadro.</summary>
    public BoardState RunTo(double t, Action<BoardState>? each = null)
    {
        while (Sim.Now < t - 1e-9)
        {
            Sim.Step(Math.Min(Dt, t - Sim.Now));
            Tick();
            each?.Invoke(State);
        }
        return State;
    }
}

public class BoardTrackerTests
{
    /// <summary>Fila indiana de <paramref name="n"/> carros a 100 m/s separados por <paramref name="spacing"/> s; o líder cruza a
    /// linha (fechando a volta <paramref name="lap"/>+1) em <paramref name="first"/> s.</summary>
    static Sim Train(int n, double len, double first, double spacing = 1, int lap = 1, int player = 10)
    {
        var cars = Enumerable.Range(0, n).Select(i => ((lap + 1) * len - first * 100 - i * spacing * 100, 100.0)).ToArray();
        return new Sim(len, cars) { PlayerIndex = Math.Min(player, n - 1), AutoPositions = true };
    }

    // ------------------------------------------------------------------ torre da linha

    [Fact]
    public void Tower_with_20_cars_builds_pages_of_8_holds_4s_and_shows_the_partial_last_page()
    {
        const double c0 = 0.37; // cruzamentos fora da grade de 60 Hz: exige interpolação sub-quadro
        var rig = new BoardRig(Train(20, 4000, c0));
        double T(int i) => c0 + i;

        var st = rig.RunTo(T(3) + 0.1);
        Assert.Equal(BoardMode.LineTower, st.Mode);
        Assert.Equal(0, st.Tower!.PageIndex);
        Assert.Equal(4, st.ItemCount);
        Assert.Equal(20, st.Tower.ExpectedCount);
        Assert.Equal(3, st.Tower.PageCount);
        var e = st.Page;
        Assert.Equal((BoardGapKind.Leader, "Lap 2", 2), (e[0].GapKind, e[0].GapText, e[0].GapLaps));
        Assert.Equal("+1.000", e[1].GapText);
        Assert.Equal(3.0, e[3].GapSeconds, 3);
        Assert.Equal((4, 0, 3), (e[3].PageSlot, e[3].Column, e[3].Row));
        Assert.Equal(T(3), e[3].CrossedT, 3);
        Assert.All(e, x => Assert.Equal(x.Position, x.RacePosition));

        st = rig.RunTo(T(7) + 0.1);
        Assert.Equal(8, st.ItemCount);
        Assert.Equal((5, 1, 0), (st.Page[4].PageSlot, st.Page[4].Column, st.Page[4].Row));
        Assert.Equal((8, 1, 3), (st.Page[7].PageSlot, st.Page[7].Column, st.Page[7].Row));
        Assert.Equal(T(7) + 4, st.WindowEndT, 3);
        Assert.Equal(4 - 0.1, st.RemainingSeconds, 2);

        st = rig.RunTo(T(7) + 3.95);
        Assert.Equal(0, st.Tower!.PageIndex);
        // Página 2 começa com quem já cruzou durante o hold (9..12) e vai sendo preenchida.
        st = rig.RunTo(T(7) + 4.05);
        Assert.Equal(BoardMode.LineTower, st.Mode);
        Assert.Equal(1, st.Tower!.PageIndex);
        Assert.Equal(4, st.ItemCount);
        Assert.Equal(9, st.Page[0].Position);
        Assert.Equal((1, 0, 0), (st.Page[0].PageSlot, st.Page[0].Column, st.Page[0].Row));
        Assert.Equal("+8.000", st.Page[0].GapText);
        Assert.Equal(T(7) + 4, st.WindowStartT, 3);

        st = rig.RunTo(T(15) + 3.95);
        Assert.Equal((1, 8), (st.Tower!.PageIndex, st.ItemCount));
        st = rig.RunTo(T(15) + 4.05);
        Assert.Equal((2, 4), (st.Tower!.PageIndex, st.ItemCount));
        Assert.True(st.Tower.Complete);
        Assert.Equal(20, st.Page[^1].Position);
        Assert.Equal("+19.000", st.Page[^1].GapText);

        // Última página parcial: 4 s depois do último carro (que cruzou durante o hold da página 2).
        st = rig.RunTo(T(19) + 3.95);
        Assert.Equal(BoardMode.LineTower, st.Mode);
        st = rig.RunTo(T(19) + 4.05);
        Assert.NotEqual(BoardMode.LineTower, st.Mode);
        Assert.Null(st.Tower);
    }

    [Fact]
    public void Partial_single_page_holds_4s_after_the_last_car_and_interpolates_at_low_rate()
    {
        var rig = new BoardRig(Train(5, 4000, 0.33, spacing: 0.731)) { Dt = 0.05 };
        double last = 0.33 + 4 * 0.731;
        var st = rig.RunTo(last + 0.02);
        Assert.Equal(BoardMode.LineTower, st.Mode);
        Assert.Equal(5, st.ItemCount);
        Assert.True(st.Tower!.Complete);
        Assert.Equal(1, st.Tower.PageCount);
        Assert.Equal("+0.731", st.Page[1].GapText);
        Assert.Equal("+2.924", st.Page[4].GapText);
        Assert.Equal((5, 1, 0), (st.Page[4].PageSlot, st.Page[4].Column, st.Page[4].Row));
        Assert.Equal(last + 4, st.WindowEndT, 3);
        Assert.NotEqual(BoardMode.LineTower, rig.RunTo(last + 4.05).Mode);
    }

    [Fact]
    public void Lapped_car_shows_plus_1L_in_the_round_of_the_leader()
    {
        // Carro 9: uma volta atrás (mesmo lugar na fila, LapsCompleted 0).
        var cars = Enumerable.Range(0, 10).Select(i => (i == 9 ? 4000 - 37 - 900 : 8000 - 37 - i * 100.0, 100.0)).ToArray();
        var sim = new Sim(4000, cars) { PlayerIndex = 3, AutoPositions = true };
        var rig = new BoardRig(sim);
        var st = rig.RunTo(11.5);                // página 2 (9º e 10º) depois do hold da página 1
        Assert.Equal(BoardMode.LineTower, st.Mode);
        Assert.Equal(1, st.Tower!.PageIndex);
        var lapped = st.Page.Single(x => x.CarIndex == 9);
        Assert.Equal((BoardGapKind.Laps, 1, "+1L"), (lapped.GapKind, lapped.GapLaps, lapped.GapText));
        Assert.Equal(10, lapped.Position);
        Assert.True(st.Tower.Complete);
    }

    [Fact]
    public void Car_stopped_in_the_pit_does_not_hold_the_tower_and_is_never_the_neighbor()
    {
        var sim = Train(10, 4000, 0.37, player: 5);
        sim.Pit[4] = PitState.InPit;
        sim.Speeds[4] = 0;
        var rig = new BoardRig(sim);
        var st = rig.RunTo(9.37 + 0.1);
        Assert.Equal(9, st.Tower!.ExpectedCount);
        Assert.True(st.Tower.Complete);
        Assert.Equal(0, st.Tower.PageIndex);                       // página 1 cheia em 8,37 → hold até 12,37
        Assert.DoesNotContain(st.Tower.Entries, x => x.CarIndex == 4);
        st = rig.RunTo(16.3);                                      // página 2 (só o 9º) até 12,37 + 4
        Assert.Equal((1, 1), (st.Tower!.PageIndex, st.ItemCount));
        Assert.NotEqual(BoardMode.LineTower, rig.RunTo(16.45).Mode);

        // Vizinhos elegíveis: 3 (à frente, 2 s: o 4 parado no box fica de fora) e 6 (atrás, 1 s) → o 6; o 4 nunca.
        var neighbors = new HashSet<int>();
        rig.RunTo(40, s => { if (s.SectorGap is { } g) neighbors.Add(g.Neighbor.CarIndex); });
        Assert.Equal([6], neighbors);
    }

    [Fact]
    public void Retired_and_garage_cars_are_ignored_and_tower_still_completes()
    {
        var sim = Train(6, 4000, 0.37, player: 2);
        sim.Race[5] = RaceState.Retired;
        sim.Pit[4] = PitState.InGarage;
        var rig = new BoardRig(sim);
        var st = rig.RunTo(3.37 + 0.1);
        Assert.True(st.Tower!.Complete);
        Assert.Equal(4, st.Tower.CrossedCount);
    }

    // ------------------------------------------------------------------ janela de setor

    static Sim Compact(int n, int player, params double[] offsets)
    {
        // Pista de 4000 m (S1 em 1333 m), todos na volta 1 (sem rodada da torre até a próxima linha).
        var cars = offsets.Select(o => (4000 + o, 100.0)).ToArray();
        return new Sim(4000, cars) { PlayerIndex = player, AutoPositions = true };
    }

    [Fact]
    public void Sector_window_opens_on_the_first_car_and_closes_2s_after_the_last()
    {
        var rig = new BoardRig(Compact(6, 3, 300, 250, 200, 150, 100, 50));
        double s1 = (4000.0 / 3 - 300) / 100;     // líder na marca S1
        double lastT = s1 + 2.5;                 // 6º carro
        Assert.Equal(BoardMode.DriverPlate, rig.RunTo(s1 - 0.1).Mode);

        var st = rig.RunTo(s1 + 0.1);
        Assert.Equal(BoardMode.SectorGap, st.Mode);
        var g = st.SectorGap!;
        Assert.Equal(1, g.Sector);
        Assert.InRange(g.OpenT, s1 - 0.01, s1 + 0.01);            // marca do setor ainda sendo aprendida (±1 quadro)
        Assert.InRange(g.CloseT, s1 + 39.99, s1 + 40.01);         // teto enquanto o último não cruza
        Assert.Equal((3, 2), (g.Player.CarIndex, g.Neighbor.CarIndex)); // empate 0,5 × 0,5 → o da frente
        Assert.True(g.NeighborAhead);
        Assert.False(g.IsSplit);

        st = rig.RunTo(s1 + 1.6);                 // jogador (4º) já cruzou: gap exato da marca
        Assert.True(st.SectorGap!.IsSplit);
        Assert.Equal(0.5, st.SectorGap.GapSeconds, 2);
        Assert.Equal("+0.500", st.SectorGap.GapText);

        st = rig.RunTo(lastT + 1.95);
        Assert.Equal(BoardMode.SectorGap, st.Mode);
        Assert.InRange(st.WindowEndT, lastT + 1.99, lastT + 2.01);
        Assert.True(st.RemainingSeconds < 0.1);
        st = rig.RunTo(lastT + 2.05);
        Assert.Equal(BoardMode.DriverPlate, st.Mode);
        Assert.Null(st.SectorGap);

        // Próxima marca (S2) abre nova janela.
        st = rig.RunTo((8000.0 / 3 - 300) / 100 + 0.1);
        Assert.Equal(2, st.SectorGap!.Sector);

        // S1 da volta seguinte (40 s depois): o cruzamento antigo do jogador não pode virar split.
        st = rig.RunTo(s1 + 40 + 0.1);
        Assert.Equal(1, st.SectorGap!.Sector);
        Assert.False(st.SectorGap.IsSplit);
        Assert.Equal(0.5, rig.RunTo(s1 + 41.6).SectorGap!.GapSeconds, 3);
    }

    [Fact]
    public void Sector_window_respects_the_safety_cap()
    {
        var rig = new BoardRig(Compact(6, 3, 300, 250, 200, 150, 100, 50), new BoardOptions { SectorMaxWindowSeconds = 1.5 });
        double s1 = (4000.0 / 3 - 300) / 100;
        Assert.Equal(BoardMode.SectorGap, rig.RunTo(s1 + 1.45).Mode);
        Assert.Equal(BoardMode.DriverPlate, rig.RunTo(s1 + 1.55).Mode);
        // Os que cruzam depois do fechamento não reabrem a janela (não são o primeiro do campo).
        rig.RunTo(s1 + 4, s => Assert.NotEqual(BoardMode.SectorGap, s.Mode));
    }

    [Fact]
    public void Sector_window_hidden_by_the_tower_is_skipped_when_too_little_is_left()
    {
        // 8 carros a 0,3 s: torre cheia em 2,47 s; hold 1,6 s → termina em 4,07. Janela S3 (linha) fecha em 2,47 + 2 = 4,47.
        var o = new BoardOptions { PageHoldSeconds = 1.6 };
        var st = new BoardRig(Train(8, 4000, 0.37, spacing: 0.3, player: 3), o).RunTo(4.2);
        Assert.Equal(BoardMode.DriverPlate, st.Mode);              // só 0,27 s restantes: não pisca
        Assert.NotNull(st.SectorGap);

        st = new BoardRig(Train(8, 4000, 0.37, spacing: 0.3, player: 3), o with { SectorMinShowSeconds = 0 }).RunTo(4.2);
        Assert.Equal(BoardMode.SectorGap, st.Mode);
        Assert.Equal(3, st.SectorGap!.Sector);
    }

    [Fact]
    public void Neighbor_is_the_closer_of_ahead_and_behind_with_negative_gap_for_the_car_behind()
    {
        // Jogador = carro 2. Frente: 1,0 s; atrás: 0,6 s → atrás.
        var rig = new BoardRig(Compact(5, 2, 600, 600, 500, 440, 300));
        var st = rig.RunTo((4000.0 / 3 - 440) / 100 + 0.1);
        var g = st.SectorGap!;
        Assert.Equal(3, g.Neighbor.CarIndex);
        Assert.False(g.NeighborAhead);
        Assert.True(g.IsSplit);
        Assert.Equal(-0.6, g.GapSeconds, 2);
        Assert.Equal("-0.600", g.GapText);
    }

    [Fact]
    public void Near_tie_prefers_the_car_ahead_and_neighbor_does_not_change_inside_the_window()
    {
        // Frente 0,62 s, atrás 0,60 s: diferença < 0,05 → o da frente.
        var sim = Compact(5, 2, 700, 562, 500, 440, 400);
        var rig = new BoardRig(sim);
        double s1 = (4000.0 / 3 - 700) / 100, close = (4000.0 / 3 - 400) / 100 + 2;
        var st = rig.RunTo(s1 + 0.05);
        Assert.Equal(1, st.SectorGap!.Neighbor.CarIndex);

        // O carro de trás encosta durante a janela: histerese mantém o vizinho até ela fechar.
        sim.Speeds[3] = 104;
        rig.RunTo(close - 0.05, s => Assert.Equal(1, s.SectorGap!.Neighbor.CarIndex));
        Assert.Null(rig.RunTo(close + 0.05).SectorGap);

        // Na janela seguinte (S2) o de trás está muito mais perto: troca.
        st = rig.RunTo((8000.0 / 3 - 700) / 100 + 0.05);
        Assert.Equal(2, st.SectorGap!.Sector);
        Assert.Equal(3, st.SectorGap.Neighbor.CarIndex);
        Assert.False(st.SectorGap.NeighborAhead);
    }

    // ------------------------------------------------------------------ comparativo e prioridades

    [Fact]
    public void Lap_comparison_every_third_lap_in_free_intervals_with_tower_and_sector_priority()
    {
        // 3000 m; jogador (1) a 100 m/s = 30,000 s; à frente (0) 101 m/s = 29,703 s; atrás (2) 95 m/s.
        var sim = new Sim(3000, (30, 101), (20, 100), (10, 95)) { PlayerIndex = 1, AutoPositions = true, AutoLapTimes = true };
        var rig = new BoardRig(sim);
        bool seenBefore = false;
        rig.RunTo(89.7, s => seenBefore |= s.LapComparison is not null);
        Assert.False(seenBefore);                                  // voltas 1 e 2: não

        var st = rig.RunTo(90.0);                                  // jogador fechou a 3ª volta (89,8 s)
        Assert.Equal(BoardMode.LineTower, st.Mode);                // a torre tem prioridade

        st = rig.RunTo(100);                                       // S1 aberta (líder em 98,7 s)
        Assert.Equal(BoardMode.SectorGap, st.Mode);
        Assert.NotNull(st.LapComparison);                          // existe, mas a janela de setor tem prioridade

        st = rig.RunTo(107.5);                                     // intervalo livre (S1 fechou em 107,2; S2 abre em 108,6)
        Assert.Equal(BoardMode.LapComparison, st.Mode);
        var cmp = st.LapComparison!;
        Assert.Equal(3, cmp.PlayerLapsCompleted);
        Assert.Equal(0, cmp.Neighbor.CarIndex);
        Assert.True(cmp.NeighborAhead);
        Assert.Equal([3, 2, 1], cmp.Laps.Select(l => l.Lap));
        Assert.Equal(3, st.ItemCount);
        foreach (var l in cmp.Laps)
        {
            Assert.Equal(30.0, l.PlayerTime!.Value, 3);
            Assert.Equal(3000 / 101.0, l.NeighborTime!.Value, 3);
            Assert.Equal(30.0 - 3000 / 101.0, l.Delta!.Value, 3);
            Assert.False(l.PlayerFaster);
        }
        Assert.True(double.IsPositiveInfinity(st.RemainingSeconds));

        bool seenLap4 = false;
        rig.RunTo(150, s => seenLap4 |= s.LapComparison is not null && sim.Snapshot().PlayerCar!.LapsCompleted == 4);
        Assert.False(seenLap4);                                    // volta 4: volta à legenda
    }

    [Fact]
    public void Lap_history_discards_laps_without_time_and_waits_for_a_late_lap_time()
    {
        var sim = new Sim(1000, (500, 100), (450, 100)) { PlayerIndex = 1 };
        var rig = new BoardRig(sim);
        sim.LastLap[1] = 0;
        rig.RunTo(5.6);                          // jogador fecha a volta 1 em 5,5 s sem tempo (≤ 0)
        rig.RunTo(7);
        Assert.Null(rig.Board.LapTime(1, 1));
        rig.RunTo(15.55);                        // volta 2 fecha em 15,5 s; o jogo publica o tempo 0,03 s depois
        sim.LastLap[1] = 10.0;
        rig.RunTo(15.6);
        Assert.Equal(10.0, rig.Board.LapTime(1, 2));
        rig.RunTo(25.6);                         // volta 3 com o mesmo tempo: gravada após a espera (1 s)
        Assert.Null(rig.Board.LapTime(1, 3));
        rig.RunTo(26.6);
        Assert.Equal(10.0, rig.Board.LapTime(1, 3));
    }

    [Fact]
    public void Practice_has_no_tower_nor_comparison_and_sector_gap_uses_the_physical_neighbor()
    {
        var sim = new Sim(3000, (600, 100), (500, 100), (200, 100), (2100, 100))
        { PlayerIndex = 1, Kind = SessionKind.Practice, AutoLapTimes = true };
        var rig = new BoardRig(sim);
        Assert.Equal(BoardMode.DriverPlate, rig.State.Mode);
        Assert.False(rig.State.IsRace);

        var st = rig.RunTo(4.5);                 // vizinho (0, 1 s à frente) cruzou S1 em 4,0
        Assert.Equal(BoardMode.SectorGap, st.Mode);
        Assert.Equal((1, 0), (st.SectorGap!.Sector, st.SectorGap.Neighbor.CarIndex));
        Assert.False(st.SectorGap.IsSplit);
        st = rig.RunTo(5.5);                     // jogador cruzou em 5,0
        Assert.True(st.SectorGap!.IsSplit);
        Assert.Equal("+1.000", st.SectorGap.GapText);
        Assert.Equal(BoardMode.DriverPlate, rig.RunTo(7.05).Mode);

        // Cruzamento de quem não é o par (carro 3 em S1 aos 9 s... e a linha) não abre janela; nada de torre/comparativo.
        rig.RunTo(130, s =>
        {
            Assert.Null(s.Tower);
            Assert.Null(s.LapComparison);
            Assert.True(s.Mode is BoardMode.DriverPlate or BoardMode.SectorGap);
            if (s.SectorGap is { } g) Assert.Equal(0, g.Neighbor.CarIndex);
        });
    }

    [Fact]
    public void Reset_on_session_change_lap_rollback_and_clock_going_back()
    {
        var sim = Train(10, 4000, 0.37);
        sim.AutoLapTimes = true;
        var rig = new BoardRig(sim);
        var st = rig.RunTo(2);
        Assert.Equal(BoardMode.LineTower, st.Mode);
        Assert.NotNull(rig.Board.LapTime(0, 2));
        long rev = st.Revision;

        // Reinício da corrida (mesma pista e tipo): as voltas voltam a 0 → reset total.
        var restart = new Sim(4000, (100, 100), (50, 100)) { PlayerIndex = 1, AutoPositions = true };
        var s2 = restart.Snapshot();
        st = rig.Board.Update(sim.Now + 1, s2, rig.Gaps);
        Assert.Equal(BoardMode.DriverPlate, st.Mode);
        Assert.Null(st.Tower);
        Assert.Null(rig.Board.LapTime(0, 2));
        Assert.True(st.Revision > rev);

        // Nova sessão (classificação): reset e sem torre.
        st = rig.Board.Update(sim.Now + 2, sim.Snapshot() with { Kind = SessionKind.Qualify }, rig.Gaps);
        Assert.Equal(BoardMode.DriverPlate, st.Mode);
        Assert.False(st.IsRace);

        // Relógio voltou com a torre no ar: reset (sem rodada fantasma).
        var fresh = new BoardRig(Train(10, 4000, 0.37));
        Assert.Equal(BoardMode.LineTower, fresh.RunTo(2).Mode);
        st = fresh.Board.Update(0.5, fresh.Sim.Snapshot(), fresh.Gaps);
        Assert.Equal(BoardMode.DriverPlate, st.Mode);

        // Fora de sessão: vazio.
        st = rig.Board.Update(1, s2 with { InSession = false }, rig.Gaps);
        Assert.Equal(BoardMode.None, st.Mode);
        Assert.Null(st.Plate);
    }

    [Fact]
    public void Driver_plate_has_team_tyre_and_position()
    {
        var sim = new Sim(3000, (600, 100), (500, 100)) { PlayerIndex = 1, AutoPositions = true };
        sim.Names = ["Michael Schumacher", "Fernando Alonso"];
        sim.CarNames = ["Formula Classic Gen2 (B)", "Formula Classic Gen2 (M)"];
        var st = new BoardRig(sim).State;
        Assert.Equal(BoardMode.DriverPlate, st.Mode);
        var p = st.Plate!;
        Assert.Equal(("Fernando Alonso", "Alonso", "ALO", "Formula Classic Gen2", "M", 2, true),
            (p.Name, p.ShortName, p.Code, p.Team, p.TyreSupplier, p.Position, p.IsPlayer));
        Assert.Equal(1, st.ItemCount);
    }

    [Fact]
    public void Steady_frames_reuse_the_heavy_blocks()
    {
        var rig = new BoardRig(Train(20, 4000, 0.37));
        var a = rig.RunTo(5.0);
        var b = rig.RunTo(5.0 + 1.0 / 60);
        Assert.Same(a.Tower, b.Tower);
        Assert.Same(a.Plate, b.Plate);
        Assert.Equal(a.Revision, b.Revision);
    }

    [Fact]
    public void Gap_text_formats()
    {
        Assert.Equal("+0.239", BoardText.Gap(0.2391));
        Assert.Equal("-1.500", BoardText.Gap(-1.5));
        Assert.Equal("+1:02.345", BoardText.Gap(62.345));
        Assert.Equal("+2L", BoardText.Laps(2));
        Assert.Equal("Lap 12", BoardText.LeaderLap(12));
    }
}
