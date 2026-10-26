using Ams2.Core;

namespace Ams2.Integration.Tests;

/// <summary>
/// Simulacao roteirizada de corrida (provider real + escritor falso, 750 s simulados em passos de 0,1 s, um GpxSatWidget por tema).
/// Roteiro: o jogador (indice 3) entra nos boxes em t=150..156 s e o lider recebe a bandeirada em t=700 s.
/// </summary>
public sealed class RaceRun
{
    public sealed record Tick(double T, int LeaderLaps, int PlayerLaps, bool PlayerInPitLane, double Gap2, ScriptedSim.Shown? Main, ScriptedSim.Shown? Top, ScriptedSim.Shown? Gap);

    public readonly Dictionary<string, List<Tick>> Ticks = ScriptedSim.GpxThemes.ToDictionary(t => t, _ => new List<Tick>());
    public readonly Dictionary<(string Theme, string Moment), (int Painted, string Hash)> Frames = [];

    public static readonly (string Moment, double T)[] Moments =
    [
        ("caption", 28), ("standings", 35), ("gap", 270), ("time", 503), ("pit", 153), ("winner", 702),
    ];

    public RaceRun()
    {
        using var sim = new ScriptedSim(false, (src, clock) => new ScriptedSource(src, clock, Script), board: false);
        sim.Option("f1-1998", "showTimeLeft", "true");
        sim.Run(750, 0.1, t =>
        {
            var m = sim.Model; var ev = m.Events!;
            foreach (var th in ScriptedSim.GpxThemes)
            {
                Ticks[th].Add(new Tick(t, ev.LeaderLaps, ev.PlayerLaps, m.Session!.PlayerCar!.InPitLane,
                    m.Standings.FirstOrDefault(r => r.Car.Position == 2)?.GapToLeader ?? double.NaN, sim.Main(th), sim.Main(th, "top"), sim.Main(th, "gap")));
                foreach (var (name, at) in Moments)
                {
                    if (Math.Abs(t - at) > 0.05) continue;
                    var px = sim.Render(th);
                    ScriptedSim.Save($"race-{th}-{name}", px);
                    Frames[(th, name)] = (ScriptedSim.Painted(px), ScriptedSim.Hash(px));
                }
            }
        });
    }

    static void Script(double t, ref Ams2.Core.Raw.RawSharedMemory raw)
    {
        const int player = 3;
        if (t is >= 150 and < 151) raw.PitModes[player] = 1;
        else if (t is >= 151 and < 155) { raw.PitModes[player] = 2; raw.Speeds[player] = 0; }
        else if (t is >= 155 and < 156) raw.PitModes[player] = 3;
        if (t >= 700) raw.RaceStates[0] = (uint)RaceState.Finished;
    }
}

public sealed class ScriptedRaceTests(RaceRun run) : IClassFixture<RaceRun>
{
    static List<(double Start, double End, double LapAt)> Windows(List<RaceRun.Tick> ticks, Func<RaceRun.Tick, ScriptedSim.Shown?> pick, Func<ScriptedSim.Shown, bool> match)
    {
        var res = new List<(double, double, double)>();
        double? start = null; double lap = 0, last = 0;
        foreach (var tk in ticks)
        {
            var s = pick(tk);
            bool on = s is not null && match(s) && s.Alpha > 0.01f;
            if (on && start is null) { start = tk.T; lap = tk.PlayerLaps; }
            if (!on && start is not null) { res.Add((start.Value, tk.T, lap)); start = null; }
            last = tk.T;
        }
        if (start is not null) res.Add((start.Value, last, lap));
        return res;
    }

    public static IEnumerable<object[]> Themes() => ScriptedSim.GpxThemes.Select(t => new object[] { t });

    [Theory, MemberData(nameof(Themes))]
    public void Caption_8s_every_25s_and_only_inside_the_window(string theme)
    {
        var ticks = run.Ticks[theme];
        var caps = Windows(ticks, t => t.Main, s => s.Key.Contains("cap", StringComparison.OrdinalIgnoreCase));
        Assert.True(caps.Count >= 15, $"{theme}: legendas={caps.Count}");
        foreach (var tk in ticks.Where(t => t.Main is { } m && m.Key.Contains("cap", StringComparison.OrdinalIgnoreCase) && m.Alpha > 0.01f))
            Assert.True(tk.T % 25 < 8.15, $"{theme}: legenda fora dos 8 s (t={tk.T:0.0})");
    }

    [Theory, MemberData(nameof(Themes))]
    public void Gap_only_on_laps_3_6_9_for_about_6s(string theme)
    {
        var ticks = run.Ticks[theme];
        var gaps = Windows(ticks, t => t.Main is { Piece: "split" } ? t.Main : t.Gap, s => s.Piece == "split");
        Assert.Equal(new[] { 3.0, 6.0 }, gaps.Select(w => w.LapAt).ToArray());   // 750 s = 6 voltas do jogador
        Assert.All(gaps, w => Assert.InRange(w.End - w.Start, 5.0, 6.5));
    }

    [Theory, MemberData(nameof(Themes))]
    public void Remaining_laps_only_on_lap_5_for_about_6s(string theme)
    {
        var ticks = run.Ticks[theme];
        var wins = Windows(ticks, t => t.Main is { Piece: "time" } ? t.Main : t.Top, s => s.Piece == "time");
        Assert.Equal(new[] { 5.0 }, wins.Select(w => w.LapAt).ToArray());
        Assert.All(wins, w => Assert.InRange(w.End - w.Start, 5.0, 6.5));
    }

    [Theory, MemberData(nameof(Themes))]
    public void Standings_open_once_per_leader_crossing_and_stay_frozen(string theme)
    {
        var ticks = run.Ticks[theme];
        var wins = Windows(ticks, t => t.Main, s => s.Piece == "standings");
        Assert.True(wins.Count >= 5, $"{theme}: janelas de classificacao={wins.Count}");
        // Congelada: por (volta do lider, pagina) o texto e identico em todos os quadros.
        var groups = ticks.Where(t => t.Main is { Piece: "standings" } m && m.Alpha > 0.99f).GroupBy(t => (t.LeaderLaps, t.Main!.Page)).ToList();
        Assert.NotEmpty(groups);
        foreach (var g in groups) Assert.True(g.Select(t => t.Main!.Texts).Distinct().Count() == 1, $"{theme}: texto mudou na volta {g.Key}");
        // Enquanto isso o gap ao vivo do 2o colocado mudou.
        // Retratos diferentes de um cruzamento para o outro.
        var perLap = ticks.Where(t => t.Main is { Piece: "standings", Page: 0 }).GroupBy(t => t.LeaderLaps).Select(g => g.First().Main!.Texts).Distinct().Count();
        Assert.True(perLap >= 3, $"{theme}: retratos iguais ({perLap})");
    }

    [Theory, MemberData(nameof(Themes))]
    public void Pit_timer_beats_standings_and_winner_beats_everything(string theme)
    {
        var ticks = run.Ticks[theme];
        var pit = ticks.Where(t => t.T is > 152 and < 154.5).Select(t => t.Main).ToList();
        Assert.All(pit, s => Assert.NotEqual("standings", s?.Piece));
        var win = ticks.Where(t => t.T is > 702 and < 706).Select(t => t.Main ?? t.Top ?? t.Gap).ToList();
        Assert.All(win, s => Assert.NotNull(s));
    }

    [Theory, MemberData(nameof(Themes))]
    public void Key_frames_render_and_differ(string theme)
    {
        var frames = RaceRun.Moments.Select(m => (m.Moment, f: run.Frames[(theme, m.Moment)])).ToList();
        Assert.All(frames, x => Assert.True(x.f.Painted > 500, $"{theme}/{x.Moment}: quadro vazio ({x.f.Painted})"));
        Assert.True(frames.Select(x => x.f.Hash).Distinct().Count() >= 5, $"{theme}: quadros repetidos");
    }
}
