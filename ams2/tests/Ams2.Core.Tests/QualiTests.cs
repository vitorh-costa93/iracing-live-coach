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
