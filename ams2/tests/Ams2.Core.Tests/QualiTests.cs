using Ams2.Core.Calc;
using Ams2.Core.Reading;

namespace Ams2.Core.Tests;

public class SessionGroupTests
{
    [Theory]
    [InlineData(SessionKind.Practice, "practice")]
    [InlineData(SessionKind.Test, "practice")]
    [InlineData(SessionKind.TimeAttack, "practice")]
    [InlineData(SessionKind.Qualify, "qualify")]
    [InlineData(SessionKind.FormationLap, "race")]
    [InlineData(SessionKind.Race, "race")]
    [InlineData(SessionKind.Invalid, null)]
    public void Maps_session_kind_to_group(SessionKind kind, string? id)
    {
        Assert.Equal(id, SessionGroups.IdOf(kind));
        Assert.Equal(id is null, SessionGroups.From(kind) is null);
    }

    [Fact]
    public void No_session_does_not_filter() => Assert.Null(SessionGroups.IdOf(null));
}

public class QualiTableTests
{
    static SessionSnapshot Snap(Sim sim, Func<CarSnapshot, CarSnapshot> f, double? remaining = 600)
    {
        var s = sim.Snapshot();
        return s with { Kind = SessionKind.Qualify, TimeRemainingSeconds = remaining, Cars = s.Cars.Select(f).ToList() };
    }

    [Fact]
    public void Orders_by_best_lap_then_untimed_by_position_with_gaps_states_and_clock()
    {
        var sim = new Sim(3000, (100, 50), (200, 50), (300, 50), (400, 50), (500, 0), (600, 50)) { PlayerIndex = 3 };
        sim.Pit[2] = PitState.InPit; sim.Pit[4] = PitState.InGarage;
        double[] best = [61.2, 0, 60.5, 60.9, 0, 0];
        var s = Snap(sim, c => c with { BestLapTime = best[c.Index], LapsCompleted = c.Index == 5 ? 0 : 3, Position = 6 - c.Index });
        var q = QualiTable.Build(s);

        Assert.Equal([2, 3, 0, 5, 4, 1], q.Rows.Select(r => r.Car.Index));   // sem tempo: posicao 1 (#5) antes de 2 (#4) e 5 (#1)
        Assert.Equal([1, 2, 3, 4, 5, 6], q.Rows.Select(r => r.Rank));
        Assert.Equal(0, q.Rows[0].GapToFirst);
        Assert.Equal(0.4, q.Rows[1].GapToFirst!.Value, 6);
        Assert.Equal(0.7, q.Rows[2].GapToFirst!.Value, 6);
        Assert.Null(q.Rows[3].GapToFirst);
        Assert.Equal(QualiStatus.InPit, q.Rows[0].Status);    // com tempo, parado no box
        Assert.Equal(QualiStatus.TimeSet, q.Rows[1].Status);
        Assert.Equal(QualiStatus.OutLap, q.Rows[3].Status);   // #5: na pista, sem volta completa e sem tempo
        Assert.Equal(QualiStatus.NoTime, q.Rows[4].Status);   // #4: na garagem sem tempo
        Assert.Equal(QualiStatus.NoTime, q.Rows[5].Status);   // #1: na pista com voltas mas sem tempo
        Assert.True(q.Player!.IsPlayer);
        Assert.Equal(2, q.Player.Rank);
        Assert.Equal(2, q.Leader!.Car.Index);
        Assert.Equal(600, q.TimeRemaining);

        // Texto no lugar do tempo (TV): so sem tempo. Com tempo, o tempo/gap fica mesmo no box ou em volta de saida.
        Assert.Null(q.Rows[0].StateText);                      // #2: com tempo e IN PIT -> mostra o tempo
        Assert.Null(q.Rows[1].StateText);
        Assert.Equal("OUT LAP", q.Rows[3].StateText);
        Assert.Equal("NO TIME", q.Rows[4].StateText);
        Assert.Equal("NO TIME", q.Rows[5].StateText);
        Assert.Null((q.Rows[1] with { Status = QualiStatus.OutLap }).StateText);   // lider/2o em volta de saida: o tempo nao some
        Assert.Equal("IN PIT", (q.Rows[3] with { Status = QualiStatus.InPit }).StateText);   // no box sem tempo: IN PIT

        Assert.False(q.Rows[3].InEliminationZone(5));
        Assert.True(q.Rows[4].InEliminationZone(5));
        Assert.True(q.Rows[5].InEliminationZone(5));
        Assert.False(q.Rows[5].InEliminationZone(0));          // 0 = desligado
    }

    [Fact]
    public void Ties_on_best_lap_keep_the_game_position()
    {
        var sim = new Sim(3000, (0, 50), (10, 50));
        var q = QualiTable.Build(Snap(sim, c => c with { BestLapTime = 60, LapsCompleted = 2, Position = c.Index == 0 ? 2 : 1 }));
        Assert.Equal([1, 0], q.Rows.Select(r => r.Car.Index));
        Assert.Equal(0, q.Rows[1].GapToFirst);
    }

    [Fact]
    public void Out_lap_tracker_marks_the_lap_after_the_pits_until_the_line()
    {
        var sim = new Sim(3000, (2800, 0), (1000, 50)) { PlayerIndex = 1 };
        var tr = new OutLapTracker();
        sim.Pit[0] = PitState.InPit;
        var s0 = sim.Snapshot();
        tr.Update(s0 with { Cars = s0.Cars.Select(c => c with { LapsCompleted = c.LapsCompleted + 2, CurrentLap = c.CurrentLap + 2 }).ToList() });
        sim.Pit[0] = PitState.None; sim.Speeds[0] = 50;
        sim.Step(1);
        var s = Snap(sim, c => c with { BestLapTime = 60, LapsCompleted = c.LapsCompleted + 2, CurrentLap = c.CurrentLap + 2 });
        tr.Update(s);
        var q = QualiTable.Build(s, tr.IsOutLap);
        Assert.Equal(QualiStatus.OutLap, q.Row(0)!.Status);   // tem tempo, mas esta na volta de saida
        Assert.Equal(QualiStatus.TimeSet, q.Row(1)!.Status);
        sim.Step(4);                                           // cruza a linha: volta lancada
        s = Snap(sim, c => c with { BestLapTime = 60, LapsCompleted = c.LapsCompleted + 2, CurrentLap = c.CurrentLap + 2 });
        tr.Update(s);
        Assert.Equal(QualiStatus.TimeSet, QualiTable.Build(s, tr.IsOutLap).Row(0)!.Status);
    }

    [Fact]
    public void Empty_session_gives_no_rows() =>
        Assert.Empty(QualiTable.Build(new Sim(3000, (0, 50)).Snapshot() with { Cars = [] }).Rows);
}

public class QualiLapTrackerTests
{
    const double Len = 3000, Dt = 0.05;

    [Fact]
    public void First_flying_lap_starts_without_reference_and_leader_updates_during_it()
    {
        var sim = new Sim(Len, (2900, 50), (0, 55)) { PlayerIndex = 0, Kind = SessionKind.Qualify };
        var tracker = new QualiLapTracker();
        Assert.True(tracker.Update(0, sim.Snapshot()).OutLap);
        for (int i = 0; i < 50; i++) { sim.Step(Dt); tracker.Update(sim.Now, sim.Snapshot()); }
        var first = tracker.State;
        Assert.False(first.OutLap);
        Assert.InRange(first.Elapsed!.Value, .49, .51);
        Assert.Null(first.LeaderBestLap);
        Assert.Null(first.PersonalBestLap);
        var late = sim.Snapshot() with { Cars = sim.Snapshot().Cars.Select(c => c.Index == 1 ? c with { BestLapTime = 54.5 } : c).ToArray() };
        var updated = tracker.Update(sim.Now, late);
        Assert.Equal(54.5, updated.LeaderBestLap);
        Assert.Equal(1, updated.LeaderIndex);
        Assert.Equal(first.Elapsed, updated.Elapsed);
    }

    [Fact]
    public void Single_initial_pit_sample_marks_out_lap_even_with_previous_time()
    {
        var sim = new Sim(Len, (100, 50)) { PlayerIndex = 0, Kind = SessionKind.Qualify };
        var tracker = new QualiLapTracker();
        var initial = sim.Snapshot();
        initial = initial with { Cars = initial.Cars.Select(c => c with { BestLapTime = 60, PitState = PitState.InGarage }).ToArray() };
        tracker.Update(0, initial);
        sim.Step(Dt);
        Assert.True(tracker.Update(sim.Now, sim.Snapshot()).OutLap);
    }

    [Fact]
    public void Connecting_mid_flying_lap_does_not_fabricate_start_or_show_classification()
    {
        var sim = new Sim(Len, (3500, 50)) { PlayerIndex = 0, Kind = SessionKind.Qualify };
        var state = new QualiLapTracker().Update(10, sim.Snapshot());
        Assert.False(state.OutLap);
        Assert.Null(state.Elapsed);
        Assert.Equal(QualiBoardStage.Hidden, new QualiOutLapPresentation().Update(state, 10, 20));
    }

    [Fact]
    public void Clock_rollback_discards_old_lap_clock_and_observes_a_new_start()
    {
        var rig = new Rig(memoryLapTimes: false);
        var before = rig.RunTo(25);
        var reset = rig.T.Update(1, rig.Sim.Snapshot());
        Assert.True(reset.SessionGeneration > before.SessionGeneration);
        Assert.Null(reset.Elapsed); Assert.Null(reset.LastSplit); Assert.Null(reset.LastResult);
        var atLine = rig.Sim.Snapshot();
        atLine = atLine with { Cars = atLine.Cars.Select(c => c.Index == 0 ? c with
            { LapsCompleted = c.LapsCompleted + 1, CurrentLap = c.CurrentLap + 1, LapDistance = 10, Sector = 0 } : c).ToArray() };
        Assert.InRange(rig.T.Update(2, atLine).Elapsed!.Value, 0, 1);
    }

    [Fact]
    public void Loading_restarts_same_out_lap_but_pause_menu_and_replay_preserve_it()
    {
        var sim = new Sim(Len, (100, 50)) { Kind = SessionKind.Qualify };
        var tracker = new QualiLapTracker();
        var snapshot = sim.Snapshot();
        var state = tracker.Update(10, snapshot);
        var flow = new QualiOutLapPresentation();
        Assert.Equal(QualiBoardStage.Tower, flow.Update(state, 10, 8));
        Assert.Equal(QualiBoardStage.Caption, flow.Update(state, 20, 8));
        foreach (uint gameState in new uint[] { 1, 4, 5, 6 })
        {
            var retained = tracker.State;
            Assert.Same(retained, tracker.Update(21, snapshot with { InSession = false, GameState = gameState }));
            var resumed = tracker.Update(22, snapshot);
            Assert.Equal(state.SessionGeneration, resumed.SessionGeneration);
            Assert.Equal(QualiBoardStage.Caption, flow.Update(resumed, 22, 8));
        }
        tracker.Update(30, snapshot with { InSession = false, GameState = 3 });
        var restarted = tracker.Update(31, snapshot);
        Assert.True(restarted.SessionGeneration > state.SessionGeneration);
        Assert.Equal(QualiBoardStage.Tower, flow.Update(restarted, 31, 8));
    }

    [Fact]
    public void Same_track_restart_by_lap_regression_clears_stale_split_and_result()
    {
        var rig = new Rig(memoryLapTimes: false);
        var before = rig.RunTo(85);
        Assert.NotNull(before.LastResult); Assert.NotNull(before.LastSplit);
        var fresh = new Sim(Len, (100, 50), (0, 55)) { Kind = SessionKind.Qualify }.Snapshot();
        var restarted = rig.T.Update(100, fresh);
        Assert.True(restarted.SessionGeneration > before.SessionGeneration);
        Assert.Null(restarted.LastResult); Assert.Null(restarted.LastSplit); Assert.Null(restarted.Elapsed);
        Assert.Null(restarted.PersonalBestLap);
    }

    [Fact]
    public void Missing_player_car_for_a_few_ticks_does_not_reset_but_a_real_car_change_does()
    {
        var rig = new Rig(memoryLapTimes: false);
        var before = rig.RunTo(25);
        Assert.NotNull(before.LastSplit);
        for (int i = 0; i < 2; i++)
        {
            rig.Sim.Step(Dt);
            var s = rig.Sim.Snapshot();
            rig.T.Update(rig.Sim.Now, s with { Cars = s.Cars.Where(c => c.Index != 0).ToList() });
        }
        var back = rig.RunTo(26);
        Assert.Equal(before.SessionGeneration, back.SessionGeneration);
        Assert.NotNull(back.LastSplit);
        var done = rig.RunTo(63);
        Assert.Equal(before.SessionGeneration, done.SessionGeneration);
        Assert.Equal(60, done.PersonalBestLap!.Value, 1);

        rig.Sim.Step(Dt);
        var sw = rig.Sim.Snapshot();
        sw = sw with { Cars = sw.Cars.Select(c => c.Index == 0 ? c with { CarName = "other car" } : c).ToList() };
        Assert.True(rig.T.Update(rig.Sim.Now, sw).SessionGeneration > done.SessionGeneration);
    }

    [Fact]
    public void Pit_exit_rearms_table_even_when_overlay_did_not_draw_in_pit()
    {
        var sim = new Sim(Len, (100, 50)) { Kind = SessionKind.Qualify };
        var tracker = new QualiLapTracker();
        var flow = new QualiOutLapPresentation();
        var snapshot = sim.Snapshot();
        var first = tracker.Update(10, snapshot);
        flow.Update(first, 10, 8);
        Assert.Equal(QualiBoardStage.Caption, flow.Update(first, 20, 8));
        tracker.Update(21, snapshot with { Cars = snapshot.Cars.Select(c => c with { PitState = PitState.InGarage }).ToArray() });
        var exited = tracker.Update(22, snapshot);
        Assert.True(exited.PitExitGeneration > first.PitExitGeneration);
        Assert.Equal(QualiBoardStage.Tower, flow.Update(exited, 22, 8));
    }

    /// <summary>Jogador (#0) a 50 m/s (setores de 20 s) cruzando a linha em t=2; #1 a 55 m/s com melhor volta 54,5 s.</summary>
    sealed class Rig
    {
        public readonly Sim Sim = new(Len, (2900, 50), (0, 55)) { PlayerIndex = 0, Kind = SessionKind.Qualify };
        public readonly QualiLapTracker T = new();
        double _playerBest;
        public Rig(bool memoryLapTimes) { Sim.AutoLapTimes = memoryLapTimes; }
        public QualiLapState RunTo(double t, Action<QualiLapState>? each = null)
        {
            while (Sim.Now < t - 1e-9)
            {
                Sim.Step(Dt);
                if (Sim.LastLap.TryGetValue(0, out var l) && l > 0) _playerBest = _playerBest <= 0 ? l : Math.Min(_playerBest, l);
                var s = Sim.Snapshot();
                s = s with { Cars = s.Cars.Select(c => c with { BestLapTime = c.Index == 1 ? 54.5 : _playerBest }).ToList() };
                var st = T.Update(Sim.Now, s);   // fora do ?.: com each nulo o Update nao seria chamado
                each?.Invoke(st);
            }
            return T.State;
        }
    }

    [Fact]
    public void Derives_lap_and_sector_times_from_sector_changes_and_the_line()
    {
        var rig = new Rig(memoryLapTimes: false);
        var st = rig.RunTo(1);
        Assert.Null(st.Elapsed);                       // inicio da volta ainda nao visto
        st = rig.RunTo(12);
        Assert.Equal(10, st.Elapsed!.Value, 1);
        Assert.Equal(0, st.Sector);
        st = rig.RunTo(25);
        Assert.Equal(20, st.Sectors[0]!.Time, 1);
        Assert.False(st.Sectors[0]!.FromMemory);
        Assert.Equal(1, st.LastSplit!.Sector);
        Assert.Equal(20, st.LastSplit.Elapsed, 1);
        Assert.Null(st.LastSplit.DeltaPersonal);      // nenhuma volta completa observada ainda

        st = rig.RunTo(63);
        var r = st.LastResult!;
        Assert.Equal(60, r.LapTime, 1);
        Assert.False(r.FromMemory);
        Assert.True(r.Improved);
        Assert.Null(r.DeltaPersonal);
        Assert.Equal(2, r.Position);                  // #1 tem 54,5
        Assert.Equal(5.5, r.GapToFirst!.Value, 1);
        Assert.All(r.Sectors, x => Assert.Equal(20, x!.Time, 1));
        Assert.Equal(60, st.PersonalBestLap!.Value, 1);
        Assert.Equal((54.5, 1), (st.LeaderBestLap, st.LeaderIndex));
        Assert.Null(st.LastSplit);                    // parciais zeram na volta nova
        Assert.All(st.Sectors, x => Assert.Null(x));
    }

    [Fact]
    public void Colours_sectors_and_gives_split_deltas_against_the_personal_best()
    {
        var rig = new Rig(memoryLapTimes: true);
        rig.RunTo(62);                                           // volta 1 = 60 s (20/20/20); #1 ja tem setores de ~18,2 s
        rig.Sim.Speeds[0] = 60;                                  // S1 da volta 2: 16,67 s (melhor geral)
        double s1 = 62 + 1000 / 60.0, s2 = s1 + 20, cross = s2 + 1000 / 45.0;
        rig.RunTo(s1);
        rig.Sim.Speeds[0] = 50;                                  // S2 igual ao pessoal (20 s), mais lento que o #1 (18,2 s)
        var st = rig.RunTo(s1 + 0.5);
        Assert.Equal(16.667, st.Sectors[0]!.Time, 1);
        Assert.Equal(SectorMark.OverallBest, st.Sectors[0]!.Mark);
        Assert.Equal(-3.333, st.LastSplit!.DeltaPersonal!.Value, 1);
        rig.RunTo(s2);
        rig.Sim.Speeds[0] = 45;                                  // S3 22,2 s: mais lento
        st = rig.RunTo(s2 + 0.5);
        Assert.Equal(SectorMark.PersonalBest, st.Sectors[1]!.Mark);
        Assert.Equal(-3.333, st.LastSplit!.DeltaPersonal!.Value, 1);
        Assert.Equal(18.18, st.OverallBestSectors[1]!.Value, 1);
        st = rig.RunTo(cross + 0.5);
        var r = st.LastResult!;
        Assert.Equal(SectorMark.Slower, r.Sectors[2]!.Mark);
        Assert.True(r.FromMemory);                               // LastLapTime do jogo adotado
        Assert.Equal(58.889, r.LapTime, 1);
        Assert.Equal(-1.111, r.DeltaPersonal!.Value, 1);
        Assert.True(r.Improved);
        Assert.Equal(2, r.Position);
        Assert.Equal(4.389, r.GapToFirst!.Value, 1);
        Assert.Equal(rig.Sim.LastLap[0], r.LapTime, 6);         // exatamente o LastLapTime do jogo
        Assert.Equal(cross, r.At, 1);
        Assert.Equal(16.667, st.PersonalBestSectors[0]!.Value, 1);
        Assert.Equal(20, st.PersonalBestSectors[1]!.Value, 1);
        Assert.Equal(20, st.PersonalBestSectors[2]!.Value, 1);   // S3 de 22,2 s nao melhora
    }

    [Fact]
    public void Pit_laps_do_not_count_for_bests_and_session_change_resets()
    {
        var rig = new Rig(memoryLapTimes: false);
        rig.RunTo(30);
        rig.Sim.Pit[0] = PitState.DrivingIntoPits;
        var st = rig.RunTo(63);                      // cruza a linha (t=62) ainda no pit lane
        rig.Sim.Pit[0] = PitState.None;
        st = rig.RunTo(64);
        Assert.Null(st.PersonalBestSectors[1]);       // S2 passou pelo pit lane: nao vale
        Assert.True(st.LastResult!.Invalid);
        Assert.True(st.OutLap);                      // a volta corrente comecou no pit lane
        rig.Sim.Kind = SessionKind.Race;
        st = rig.RunTo(64.2);
        Assert.Null(st.LastResult);
    }

    [Fact]
    public void Uses_shared_memory_sector_times_when_present()
    {
        var mem = new FakeMemory { Raw = { SessionState = 3, ViewedParticipantIndex = 0, TrackLength = (float)Len, NumSectors = 3 } };
        mem.SetCar(0, "Player", "car", "F1", 1, 2900, 0, 50);
        mem.SetCar(1, "Other", "car", "F1", 2, 0, 0, 55);
        mem.Raw.FastestLapTimes[1] = 54.5f;
        mem.Raw.FastestSector1Times[1] = 15.5f; mem.Raw.FastestSector2Times[1] = 18f; mem.Raw.FastestSector3Times[1] = 21f;
        var tr = new QualiLapTracker();
        double total = 2900, now = 0;
        QualiLapState st = QualiLapState.Empty;
        while (now < 23)
        {
            now += Dt; total += 50 * Dt;
            int laps = (int)(total / Len); double d = total - laps * Len;
            ref var p = ref mem.Raw.Participants[0];
            p.LapsCompleted = (uint)laps; p.CurrentLap = (uint)laps + 1; p.CurrentLapDistance = (float)d; p.CurrentSector = (int)(d / Len * 3);
            if (laps >= 1 && p.CurrentSector >= 1) mem.Raw.CurrentSector1Times[0] = 20.3f;   // jogo informa o setor no mesmo quadro
            st = tr.Update(now, SnapshotMapper.Map(mem.Raw));
        }
        Assert.True(st.Sectors[0]!.FromMemory, $"{st.Sectors[0]} el={st.Elapsed}");
        Assert.Equal(20.3, st.Sectors[0]!.Time, 3);
        Assert.Equal(20.3, st.PersonalBestSectors[0]!.Value, 3);
        Assert.Equal(15.5, st.OverallBestSectors[0]!.Value, 3);    // melhor geral vem do mFastestSector1Times do outro carro
        Assert.Equal(SectorMark.PersonalBest, st.Sectors[0]!.Mark);   // melhor pessoal, mas nao o geral
        Assert.Equal(1, st.LeaderIndex);
    }
}

public class QualiEndTrackerTests
{
    static SessionSnapshot Snap(double? remaining, double[] best, uint flag = 0, RaceState state = RaceState.Racing)
    {
        var sim = new Sim(3000, (100, 50), (200, 50), (300, 50)) { PlayerIndex = 1 };
        var s = sim.Snapshot();
        return s with { Kind = SessionKind.Qualify, TimeRemainingSeconds = remaining, FlagColour = flag,
            Cars = s.Cars.Select(c => c with { BestLapTime = best[c.Index], LapsCompleted = 3, RaceState = state }).ToList() };
    }

    static QualiEndState? Step(QualiEndTracker t, double now, SessionSnapshot s) => t.Update(now, s, QualiTable.Build(s));

    [Fact]
    public void Ends_on_zero_clock_chequered_flag_or_everyone_finished_and_resets_on_a_new_session()
    {
        double[] best = [61.2, 60.9, 61.5];
        var t = new QualiEndTracker();
        Assert.Null(Step(t, 10, Snap(30, best)));
        Assert.Null(Step(t, 11, Snap(null, best)));                         // sem relogio e sem bandeira: em andamento
        var e = Step(t, 40, Snap(0, best));
        Assert.Equal(40, e!.EndedAt);
        Assert.Empty(e.Changes);
        Assert.Equal(40, Step(t, 41, Snap(0, best))!.EndedAt);              // continua o mesmo fim
        Assert.Null(Step(t, 50, Snap(900, best)));                          // nova sessao
        Assert.Equal(60, Step(new QualiEndTracker(), 60, Snap(300, best, flag: 11))!.EndedAt);
        Assert.Equal(70, Step(new QualiEndTracker(), 70, Snap(300, best, state: RaceState.Finished))!.EndedAt);
    }

    [Fact]
    public void Records_table_changes_after_the_end_and_the_display_window_extends_or_reopens()
    {
        var t = new QualiEndTracker();
        Step(t, 100, Snap(0, [61.2, 60.9, 61.5]));
        Step(t, 105, Snap(0, [61.2, 60.9, 61.5]));                          // nada mudou
        var e = Step(t, 108, Snap(0, [61.2, 60.9, 60.7]))!;                  // volta final: muda a ordem
        Assert.Equal([108.0], e.Changes);
        Assert.Equal((100.0, 123.0), e.Window(15));                          // estendida sem reiniciar
        var late = new QualiEndState(100, [108, 140]);
        Assert.Equal((140.0, 155.0), late.Window(15));                       // depois que saiu: janela nova
        Assert.Equal((100.0, 115.0), new QualiEndState(100, []).Window(15));
    }
}
