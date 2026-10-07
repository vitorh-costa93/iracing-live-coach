using Ams2.Core.Calc;
using Ams2.Core.Reading;

namespace Ams2.Core.Tests;

/// <summary>"RACE START 0-200km/h" do 2018 (LaunchTracker + LaunchStore).</summary>
public sealed class LaunchTrackerTests : IDisposable
{
    const double Dt = 1.0 / 60;
    readonly string _dir = Path.Combine(Path.GetTempPath(), "ams2-launch-" + Guid.NewGuid().ToString("N"));
    string FilePath => Path.Combine(_dir, "launch.json");
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    /// <summary>Jogador (índice 0) parado no grid e um adversário.</summary>
    static Sim GridSim(double startSpeed = 0)
    {
        var sim = new Sim(5000, (100, startSpeed), (120, 0)) { CarNames = ["F-Car", "F-Car"] };
        sim.Race[0] = RaceState.NotStarted; sim.Race[1] = RaceState.NotStarted;
        return sim;
    }

    /// <summary>1 s no grid, sinal verde e aceleração constante até <paramref name="topKph"/> (segue mais <paramref name="after"/> s).</summary>
    static void RunStart(Sim sim, LaunchTracker tr, double accel, double topKph = 260, double after = 2)
    {
        for (int i = 0; i < 60; i++) { sim.Step(Dt); tr.Update(sim.Now, sim.Snapshot()); }
        sim.Race[0] = RaceState.Racing; sim.Race[1] = RaceState.Racing;
        double t0 = sim.Now + Dt, top = topKph / 3.6;
        sim.Step(Dt);
        tr.Update(sim.Now, sim.Snapshot());   // quadro do verde (t0)
        double end = t0 + top / accel + after;
        while (sim.Now < end)
        {
            sim.Step(Dt);
            sim.Speeds[0] = Math.Min(top, accel * (sim.Now - t0));
            tr.Update(sim.Now, sim.Snapshot());
        }
    }

    static double Accel(double t200) => 200 / 3.6 / t200;

    [Fact]
    public void Standing_start_times_0_100_and_0_200()
    {
        var sim = GridSim();
        var tr = new LaunchTracker();
        RunStart(sim, tr, Accel(4.6));
        var r = tr.Last!;
        Assert.Equal(4.6, r.T200!.Value, 3);
        Assert.Equal(2.3, r.T100!.Value, 3);
        Assert.Null(r.PrevBest200);
        Assert.Equal(4.6, tr.State.StoredBest200!.Value, 3);
        Assert.True(r.At200 > r.At100);
        Assert.Equal(LaunchStore.Key("T", "", "F-Car"), r.Key);
    }

    [Fact]
    public void Rolling_or_non_race_starts_are_discarded()
    {
        // Carro andando quando o verde acende (largada lançada): descarta.
        var moving = GridSim(startSpeed: 15);
        var tr = new LaunchTracker();
        RunStart(moving, tr, Accel(4.6));
        Assert.Null(tr.Last);
        Assert.Null(tr.State.StoredBest200);

        // Treino: sem largada.
        var practice = GridSim();
        practice.Kind = SessionKind.Practice;
        var tr2 = new LaunchTracker();
        RunStart(practice, tr2, Accel(4.6));
        Assert.Null(tr2.Last);

        // Overlay aberto com a corrida já em andamento (nunca viu o grid): nada.
        var late = GridSim();
        late.Race[0] = RaceState.Racing;
        var tr3 = new LaunchTracker();
        for (int i = 0; i < 600; i++) { late.Step(Dt); late.Speeds[0] = Math.Min(70, 12 * late.Now); tr3.Update(late.Now, late.Snapshot()); }
        Assert.Null(tr3.Last);
    }

    [Fact]
    public void Launch_that_never_reaches_200_keeps_the_100_time()
    {
        var sim = GridSim();
        var tr = new LaunchTracker();
        RunStart(sim, tr, Accel(4.6), topKph: 150, after: LaunchTracker.Timeout + 1);
        Assert.Equal(2.3, tr.Last!.T100!.Value, 3);
        Assert.Null(tr.Last.T200);
        Assert.Null(tr.State.StoredBest200);
    }

    [Fact]
    public void Previous_best_is_kept_per_track_and_car_and_persisted()
    {
        var store = new LaunchStore(FilePath);
        var tr = new LaunchTracker(store);
        var sim = GridSim();
        RunStart(sim, tr, Accel(4.6));
        Assert.Null(tr.Last!.PrevBest200);

        // Reinício da corrida (volta ao grid parado): largada mais lenta. BEST = 4.6 anterior; o melhor não muda.
        sim.Speeds[0] = 0;
        sim.Race[0] = RaceState.NotStarted; sim.Race[1] = RaceState.NotStarted;
        RunStart(sim, tr, Accel(5.0));
        Assert.Equal(5.0, tr.Last!.T200!.Value, 3);
        Assert.Equal(4.6, tr.Last.PrevBest200!.Value, 3);
        Assert.Equal(4.6, store.Best(tr.Last.Key, 200)!.Value, 3);

        // Mais rápida: vira o novo melhor; o resultado ainda mostra o melhor anterior (4.6).
        sim.Speeds[0] = 0;
        sim.Race[0] = RaceState.NotStarted; sim.Race[1] = RaceState.NotStarted;
        RunStart(sim, tr, Accel(4.2));
        Assert.Equal(4.6, tr.Last!.PrevBest200!.Value, 3);
        Assert.Equal(4.2, store.Best(tr.Last.Key, 200)!.Value, 3);

        // Arquivo: outro processo lê o mesmo melhor; outra pista/carro não tem.
        Assert.True(File.Exists(FilePath));
        var reread = new LaunchStore(FilePath);
        Assert.Equal(4.2, reread.Best(LaunchStore.Key("T", "", "F-Car"), 200)!.Value, 3);
        Assert.Equal(2.1, reread.Best(LaunchStore.Key("T", "", "F-Car"), 100)!.Value, 2);
        Assert.Null(reread.Best(LaunchStore.Key("Other", "", "F-Car"), 200));
        Assert.Null(reread.Best(LaunchStore.Key("T", "", "Other car"), 200));

        // Nova pista: o último resultado zera, o melhor guardado é o da pista nova (nenhum).
        sim.Track = "Other";
        tr.Update(sim.Now + Dt, sim.Snapshot());
        Assert.Null(tr.Last);
        Assert.Null(tr.State.StoredBest200);
    }

    [Fact]
    public void Store_never_throws_on_bad_files()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ isto nao e json");
        var st = new LaunchStore(FilePath);
        Assert.Null(st.Best("k", 200));
        Assert.True(st.Offer("k", 200, 4.5));
        Assert.False(st.Offer("k", 200, 4.9));
        Assert.False(st.Offer("k", 200, double.NaN));
        Assert.Equal(4.5, new LaunchStore(FilePath).Best("k", 200));

        // Caminho impossível (o "arquivo" é um diretório): grava só em memória, sem exceção.
        string dirAsFile = Path.Combine(_dir, "pasta");
        Directory.CreateDirectory(dirAsFile);
        var bad = new LaunchStore(dirAsFile);
        Assert.True(bad.Offer("k", 100, 2.2));
        Assert.Equal(2.2, bad.Best("k", 100));

        Assert.EndsWith(Path.Combine("ams2-live-coach", "launch.json"), LaunchStore.DefaultPath());
        Assert.Equal(Path.Combine(_dir, "launch.json"), LaunchStore.DefaultPath(_dir));
    }

    [Fact]
    public void Provider_path_reads_the_launch_from_raw_memory()
    {
        var mem = new FakeMemory();
        mem.SetCar(0, "A Driver", "car", "F1", 1, 100, speed: 0);
        mem.Raw.RaceStates[0] = 1;
        mem.Raw.Speed = 0;
        var tr = new LaunchTracker();
        double now = 0;
        mem.TryRead(out var raw);
        tr.Update(now, SnapshotMapper.Map(raw));
        mem.Raw.RaceStates[0] = 2;
        double t0 = now += Dt;
        mem.TryRead(out raw);
        tr.Update(now, SnapshotMapper.Map(raw));
        while (tr.Last?.T200 is null && now < 10)
        {
            now += Dt;
            mem.Raw.Speed = (float)(Accel(4.6) * (now - t0));
            mem.TryRead(out raw);
            tr.Update(now, SnapshotMapper.Map(raw));
        }
        Assert.Equal(4.6, tr.Last!.T200!.Value, 2);
    }
}
