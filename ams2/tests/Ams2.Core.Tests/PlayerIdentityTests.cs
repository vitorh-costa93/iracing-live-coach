using System.Runtime.InteropServices;
using Ams2.Core.Calc;
using Ams2.Core.Raw;
using Ams2.Core.Reading;
using Ams2.Shared.PlayerNames;

namespace Ams2.Core.Tests;

public class PlayerIdentityTests
{
    static Sim Grid(string[] names, string[] cars, int player = 1)
        => new(7000, (300, 60), (200, 60), (100, 60)) { PlayerIndex = player, Names = names, CarNames = cars, AutoPositions = true };

    [Fact]
    public void Apply_renames_only_the_player_keeps_original_and_leaves_indices_and_distances_intact()
    {
        var sim = Grid(["Adam Alpha", "Vitor COSTA", "Carl Gamma"], ["M (M)", "M", "M (B)"]);
        var before = sim.Snapshot();
        var after = PlayerIdentity.Apply(before, c => c == "M" ? new PlayerNameEntry { Name = " Miko Hanninen " } : null);
        var me = after.PlayerCar!;
        Assert.Equal("Miko Hanninen", me.Name);
        Assert.Equal("Vitor COSTA", me.OriginalName);
        Assert.True(me.IsPlayer);
        Assert.Equal(before.Cars.Select(c => (c.Index, c.Position, c.LapDistance)), after.Cars.Select(c => (c.Index, c.Position, c.LapDistance)));
        Assert.Equal(["Adam Alpha", "Carl Gamma"], after.Cars.Where(c => !c.IsPlayer).Select(c => c.Name));
        Assert.Equal("Vitor COSTA", before.PlayerCar!.Name); // original intocado
    }

    [Fact]
    public void Apply_sets_team_and_country_from_the_chosen_livery_and_clears_them_without_one()
    {
        var sim = Grid(["Adam Alpha", "Vitor COSTA", "Carl Gamma"], ["M", "M", "M"]);
        var s = PlayerIdentity.Apply(sim.Snapshot(), _ => new PlayerNameEntry { Name = "Kimi Raikkonen", Team = "McLaren", Country = "FIN" });
        Assert.Equal(("Kimi Raikkonen", "McLaren", "FIN"), (s.PlayerCar!.Name, s.PlayerCar.TeamName, s.PlayerCar.Country));
        Assert.Equal("McLaren", BoardText.Team(s.PlayerCar));
        Assert.All(s.Cars.Where(c => !c.IsPlayer), c => Assert.Equal(("", ""), (c.TeamName, c.Country)));
        var onlyTeam = PlayerIdentity.Apply(sim.Snapshot(), _ => new PlayerNameEntry { Team = "Ferrari" });
        Assert.Equal(("Vitor COSTA", "Ferrari"), (onlyTeam.PlayerCar!.Name, onlyTeam.PlayerCar.TeamName));
        var cleared = PlayerIdentity.Apply(s, _ => null);
        Assert.Equal(("Vitor COSTA", "", ""), (cleared.PlayerCar!.Name, cleared.PlayerCar.TeamName, cleared.PlayerCar.Country));
    }

    [Fact]
    public void Apply_without_override_keeps_the_game_name_but_fills_original()
    {
        var sim = Grid(["A", "Vitor COSTA", "C"], ["x", "y", "z"]);
        var s = PlayerIdentity.Apply(sim.Snapshot(), _ => null);
        Assert.Equal("Vitor COSTA", s.PlayerCar!.Name);
        Assert.Equal("Vitor COSTA", s.PlayerCar.OriginalName);
        Assert.Same(s, PlayerIdentity.Apply(s, _ => null)); // idempotente, sem realocar
    }

    [Fact]
    public void Derived_short_name_and_code_follow_the_new_name_and_gaps_do_not_change()
    {
        var sim = Grid(["Adam Alpha", "Vitor COSTA", "Carl Gamma"], ["M", "M", "M"]);
        var gaps = new GapTracker(); var gapsRenamed = new GapTracker();
        double g1 = 0, g2 = 0;
        for (int i = 0; i < 600; i++)
        {
            sim.Step(1.0 / 60);
            var s = sim.Snapshot();
            var r = PlayerIdentity.Apply(s, _ => new PlayerNameEntry { Name = "Miko Hanninen" });
            gaps.Update(sim.Now, s.TrackLength, s.Cars);
            gapsRenamed.Update(sim.Now, r.TrackLength, r.Cars);
            g1 = gaps.GapSeconds(sim.Now, s.Cars[0], s.PlayerCar!, 1) ?? 0;
            g2 = gapsRenamed.GapSeconds(sim.Now, r.Cars[0], r.PlayerCar!, 1) ?? 0;
        }
        Assert.Equal(g1, g2);
        var snap = PlayerIdentity.Apply(sim.Snapshot(), _ => new PlayerNameEntry { Name = "Miko Hanninen" });
        Assert.Equal("Hanninen", BoardText.ShortName(snap.PlayerCar!, snap.Cars));
        Assert.Equal("HAN", BoardText.Code(snap.PlayerCar!.Name));
        var d = BoardText.Driver(snap.PlayerCar!, snap.Cars);
        Assert.Equal(("Miko Hanninen", "Hanninen", "HAN"), (d.Name, d.ShortName, d.Code));
    }

    [Fact]
    public void Board_standings_and_relative_use_the_display_name()
    {
        var sim = Grid(["Adam Alpha", "Vitor COSTA", "Carl Gamma"], ["M", "M", "M"]);
        var rig = new BoardRig(sim);
        var gaps = new GapTracker();
        for (int i = 0; i < 300; i++) { sim.Step(1.0 / 60); gaps.Update(sim.Now, 7000, sim.Snapshot().Cars); }
        var s = PlayerIdentity.Apply(sim.Snapshot(), _ => new PlayerNameEntry { Name = "Miko Hanninen" });
        var rel = RelativeBuilder.Build(s, gaps, sim.Now, 4, 4);
        Assert.Contains(rel, r => r.IsPlayer && r.Car.Name == "Miko Hanninen");
        var board = new BoardTracker(null).Update(sim.Now, s, gaps);
        Assert.NotNull(board);
    }

    // ---- dump real v14 (Interlagos, 18 carros): o jogador e "Vitor COSTA" ----

    static SessionSnapshot Real()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", "ams2-v14-interlagos.bin"));
        var raw = MemoryMarshal.Read<RawSharedMemory>(bytes);
        return SnapshotMapper.Map(in raw);
    }

    [Fact]
    public void Real_dump_player_gets_the_display_name()
    {
        var s = Real();
        var me = s.PlayerCar!;
        Assert.Equal("Vitor COSTA", me.Name);

        var r = PlayerIdentity.Apply(s, _ => new PlayerNameEntry { Name = "Miko Hanninen" });
        Assert.Equal("Miko Hanninen", r.PlayerCar!.Name);
        Assert.Equal("Vitor COSTA", r.PlayerCar.OriginalName);
        Assert.Equal(s.Cars.Count, r.Cars.Count);
        Assert.Equal(s.PlayerCar!.Position, r.PlayerCar.Position);
    }
}
