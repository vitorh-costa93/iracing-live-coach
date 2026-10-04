using Ams2.Core.Calc;
using Ams2.Core.Reading;

namespace Ams2.Core.Tests;

/// <summary>Grid de largada para o modo GAINED/LOST da torre 2018 (GridTracker).</summary>
public class GridTrackerTests
{
    /// <summary>3 carros parados no grid (NotStarted) na ordem 2, 0, 1 (o índice 2 larga na pole).</summary>
    static Sim GridSim()
    {
        var sim = new Sim(1000, (100, 0), (90, 0), (110, 0)) { AutoPositions = true };
        for (int i = 0; i < 3; i++) sim.Race[i] = RaceState.NotStarted;
        return sim;
    }

    [Fact]
    public void Grid_follows_positions_before_the_start_and_freezes_at_the_start()
    {
        var sim = GridSim();
        var g = new GridTracker();
        g.Update(sim.Snapshot());
        Assert.Equal(new Dictionary<int, int> { [2] = 1, [0] = 2, [1] = 3 }, g.Grid);
        Assert.False(g.Frozen);

        // Largada: todos correndo; o índice 1 é o mais rápido e passa os dois.
        for (int i = 0; i < 3; i++) sim.Race[i] = RaceState.Racing;
        sim.Speeds[0] = 50; sim.Speeds[1] = 80; sim.Speeds[2] = 50;
        g.Update(sim.Snapshot());
        Assert.True(g.Frozen);
        for (int k = 0; k < 120; k++) { sim.Step(1.0 / 60); g.Update(sim.Snapshot()); }
        var s = sim.Snapshot();
        var car1 = s.Cars.Single(c => c.Index == 1);
        Assert.Equal(1, car1.Position);
        Assert.Equal(2, GridTracker.Delta(g.Grid, car1));                               // largou em 3o, agora 1o
        Assert.Equal(-1, GridTracker.Delta(g.Grid, s.Cars.Single(c => c.Index == 2)));  // pole, agora 2o
        Assert.Equal(new Dictionary<int, int> { [2] = 1, [0] = 2, [1] = 3 }, g.Grid);  // congelado
    }

    [Fact]
    public void Joining_on_the_first_lap_captures_current_positions_and_later_is_unknown()
    {
        var sim = new Sim(1000, (300, 50), (200, 50)) { AutoPositions = true };
        var g = new GridTracker();
        g.Update(sim.Snapshot());
        Assert.Equal(new Dictionary<int, int> { [0] = 1, [1] = 2 }, g.Grid);

        var late = new Sim(1000, (2300, 50), (2200, 50)) { AutoPositions = true };   // líder já com 2 voltas
        var g2 = new GridTracker();
        g2.Update(late.Snapshot());
        Assert.Empty(g2.Grid);
        Assert.True(g2.Frozen);
        Assert.Null(GridTracker.Delta(g2.Grid, late.Snapshot().Cars[0]));
    }

    [Fact]
    public void Non_race_sessions_have_no_grid_and_a_new_race_resets()
    {
        var sim = GridSim();
        sim.Kind = SessionKind.Qualify;
        var g = new GridTracker();
        g.Update(sim.Snapshot());
        Assert.Empty(g.Grid);

        sim.Kind = SessionKind.Race;
        g.Update(sim.Snapshot());
        Assert.Equal(3, g.Grid.Count);
        for (int i = 0; i < 3; i++) sim.Race[i] = RaceState.Racing;
        g.Update(sim.Snapshot());
        Assert.True(g.Frozen);

        sim.Track = "Outra";   // nova sessão: zera e volta a acompanhar o grid
        for (int i = 0; i < 3; i++) sim.Race[i] = RaceState.NotStarted;
        g.Update(sim.Snapshot());
        Assert.False(g.Frozen);
        Assert.Equal(3, g.Grid.Count);
    }

    [Fact]
    public void Provider_path_reads_grid_from_raw_memory()
    {
        // Estrutura crua -> modelo -> tracker: RaceStates 1 (NotStarted) mantém o grid; 2 (Racing) congela.
        var mem = new FakeMemory();
        mem.SetCar(0, "A Driver", "car", "F1", 2, 100);
        mem.SetCar(1, "B Driver", "car", "F1", 1, 120);
        mem.Raw.RaceStates[0] = 1; mem.Raw.RaceStates[1] = 1;
        var g = new GridTracker();
        mem.TryRead(out var raw);
        g.Update(SnapshotMapper.Map(raw));
        Assert.Equal(new Dictionary<int, int> { [0] = 2, [1] = 1 }, g.Grid);

        mem.Raw.RaceStates[0] = 2; mem.Raw.RaceStates[1] = 2;
        mem.Raw.Participants[0].RacePosition = 1; mem.Raw.Participants[1].RacePosition = 2;
        mem.TryRead(out raw);
        var s = SnapshotMapper.Map(raw);
        g.Update(s);
        Assert.Equal(1, GridTracker.Delta(g.Grid, s.Cars.Single(c => c.Index == 0)));
        Assert.Equal(-1, GridTracker.Delta(g.Grid, s.Cars.Single(c => c.Index == 1)));
    }
}
