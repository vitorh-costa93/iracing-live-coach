using Ams2.Core;
using Ams2.Core.Calc;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Gfx;
using Ams2.OverlayHost.Theme;
using Ams2.OverlayHost.Widgets;
using Ams2.Shared.Profiles;

namespace Ams2.Integration.Tests;

public class Broadcast93BoardStatesTests
{
    static readonly WidgetSettings Cfg = new() { Id = "board", Columns = [] };
    static readonly WidgetSettings QualiCfg = new() { Id = "qualiboard" };
    const double Now = 200;
    static readonly string[] Names = ["Mark Blundell", "Alain Prost", "Ayrton Senna", "Damon Hill", "Michael Schumacher", "Gerhard Berger", "Jean Alesi", "Martin Brundle"];

    static byte[] Render(Action<ThemeCanvas> draw, string? artifact = null)
    {
        using var gfx = DeviceResources.CreateOffscreen(1920, 300);
        using var c = new ThemeCanvas(gfx, Themes.F1_1993);
        gfx.BeginFrame(); c.Begin(); draw(c); c.End(); gfx.EndFrame();
        var pixels = gfx.ReadPixelsBgra();
        if (artifact is not null && Environment.GetEnvironmentVariable("AMS2_REVIEW_FRAMES") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            PngWriter.SaveFromPremultipliedBgra(Path.Combine(directory, artifact + ".png"), pixels, 1920, 300, (45, 50, 55));
        }
        return pixels;
    }

    static SessionSnapshot Named(SessionSnapshot s)
        => s with { Cars = s.Cars.Select((c, i) => c with { Name = Names[i % Names.Length], TeamName = i == 0 ? "LIGIER RENAULT" : "WILLIAMS RENAULT" }).ToArray() };

    /// <summary>Corrida de 20 voltas com as voltas restantes dadas; nada ativo.</summary>
    static OverlayModel Race(int lapsRemaining = 12)
    {
        var m = LayoutPreview.WithCountries(LayoutPreview.ForWidget("board")) with { Now = Now, PlayerDriving = true };
        return m with
        {
            Session = Named(m.Session! with { InSession = true, GameState = 2, LapsInEvent = 20 }),
            Board = m.Board! with { Gap93 = null, LapsRemaining = lapsRemaining },
            Broadcast = BroadcastState.Empty,
        };
    }

    static OverlayModel WithGap(OverlayModel m)
    {
        var s = m.Session!;
        var player = s.PlayerCar!; var neighbor = s.Cars.First(c => c.Index != player.Index);
        return m with { Board = m.Board! with { Gap93 = new BoardGap93(BoardText.Driver(player, s.Cars), BoardText.Driver(neighbor, s.Cars), true, 1.268, "1.268", m.Now - 1.268, m.Now, m.Now + 7) } };
    }

    static OverlayModel WithFastest(OverlayModel m, double time)
    {
        var s = m.Session!;
        var best = s.Cars.First(c => !c.IsPlayer);
        return m with { Session = s with { Cars = s.Cars.Select(c => c.Index == best.Index ? c with { BestLapTime = time } : c).ToArray() } };
    }

    static OverlayModel WithLapStart(OverlayModel m, double ago) => m with { Broadcast = BroadcastState.Empty with { PlayerLapChangedT = m.Now - ago } };

    static OverlayModel WithPit(OverlayModel m, double stopped) => m with
    {
        Broadcast = BroadcastState.Empty with { PlayerInPitLane = true, PlayerPitLaneStartT = m.Now - stopped - 8, PlayerStopped = true, PlayerStopStartT = m.Now - stopped }
    };

    [Fact]
    public void Nothing_active_keeps_player_caption_and_position()
    {
        var m = Race();
        Assert.Equal(Broadcast93RaceBoard.Mode.Caption, Broadcast93RaceBoard.Select(m));
        var board = new Broadcast93RaceBoard();
        Assert.Contains(Render(c => board.Draw(c, m, Cfg), "race93-caption"), b => b != 0);
        Assert.Contains(Render(c => board.Draw(c, m with { Now = Now + 30 }, Cfg)), b => b != 0);
    }

    [Fact]
    public void Caption_uses_actual_1999_team_under_1993_theme()
    {
        var m = Race();
        var player = m.Session!.PlayerCar!;
        m = m with { Session = m.Session with { Cars = m.Session.Cars.Select(c => c.Index == player.Index
            ? c with { Name = "Michael Schumacher", TeamName = "Ferrari", CarName = "F-V10_Gen2_1999_Ferrari" } : c).ToArray() } };
        Assert.Equal("Ferrari", BroadcastUi.Team(m.Session.PlayerCar!));
        var board = new Broadcast93RaceBoard();
        var actual = Render(c => board.Draw(c, m, Cfg), "race93-schumacher99-ferrari");
        var wrong = m with { Session = m.Session with { Cars = m.Session.Cars.Select(c => c.IsPlayer
            ? c with { TeamName = "Benetton-Ford" } : c).ToArray() } };
        Assert.NotEqual(actual, Render(c => new Broadcast93RaceBoard().Draw(c, wrong, Cfg)));
        var moved = m with { Session = m.Session with { Cars = m.Session.Cars.Select(c => c.IsPlayer
            ? c with { Position = c.Position + 1 } : c).ToArray() } };
        Assert.NotEqual(actual, Render(c => new Broadcast93RaceBoard().Draw(c, moved, Cfg)));
    }

    [Fact]
    public void Fastest_does_not_consume_gap_and_remaining_display_windows()
    {
        var m = WithLapStart(WithGap(Race(10)), 0);
        var fastest = new FastestLapEvent(m.Session!.Cars[1], 87.438, Now);
        var presentation = new Broadcast93RacePresentation();
        Assert.Equal(Broadcast93RaceBoard.Mode.Fastest, presentation.Update(m, fastest).Mode);
        for (double age = .5; age < 7; age += .5)
            Assert.Equal(Broadcast93RaceBoard.Mode.Fastest, presentation.Update(m with { Now = Now + age }, fastest).Mode);
        Assert.Equal(Broadcast93RaceBoard.Mode.LapsToGo, presentation.Update(m with { Now = Now + 7 }, fastest).Mode);
        for (double age = 7.5; age < 13; age += .5) presentation.Update(m with { Now = Now + age }, fastest);
        var gap = presentation.Update(m with { Now = Now + 13 }, fastest);
        Assert.Equal(Broadcast93RaceBoard.Mode.Gap, gap.Mode);
        Assert.Equal(m.Board!.Gap93, gap.Gap);
        for (double age = 13.5; age < 20; age += .5) presentation.Update(m with { Now = Now + age }, fastest);
        Assert.Equal(Broadcast93RaceBoard.Mode.Caption, presentation.Update(m with { Now = Now + 20 }, fastest).Mode);
        Assert.Equal(Broadcast93RaceBoard.Mode.None, presentation.Update(m with { PlayerDriving = false }).Mode);
    }

    [Fact]
    public void Priority_is_pit_then_fastest_then_laps_to_go_then_gap()
    {
        var m = WithLapStart(WithGap(Race(10)), 1);
        var fastest = new FastestLapEvent(m.Session!.Cars[1] with { BestLapTime = 87.438 }, 87.438, Now - 1);
        Assert.Equal(Broadcast93RaceBoard.Mode.Fastest, Broadcast93RaceBoard.Select(m, fastest));
        Assert.Equal(Broadcast93RaceBoard.Mode.LapsToGo, Broadcast93RaceBoard.Select(m, fastest, fastestEnabled: false));
        Assert.Equal(Broadcast93RaceBoard.Mode.Gap, Broadcast93RaceBoard.Select(m with { Board = m.Board! with { LapsRemaining = 7 } }));
        Assert.Equal(Broadcast93RaceBoard.Mode.Pit, Broadcast93RaceBoard.Select(WithPit(m, 3), fastest));
        Assert.Equal(Broadcast93RaceBoard.Mode.Caption, Broadcast93RaceBoard.Select(m with { Board = m.Board! with { Gap93 = null, LapsRemaining = 7 } }));
    }

    [Fact]
    public void Laps_to_go_shows_six_seconds_from_lap_start_only_in_lap_races()
    {
        var m = WithLapStart(Race(10), 0);
        Assert.Equal(Broadcast93RaceBoard.Mode.LapsToGo, Broadcast93RaceBoard.Select(m));
        Assert.Equal(Broadcast93RaceBoard.Mode.LapsToGo, Broadcast93RaceBoard.Select(m with { Now = Now + 5.9 }));
        Assert.Equal(Broadcast93RaceBoard.Mode.Caption, Broadcast93RaceBoard.Select(m with { Now = Now + 6.1 }));
        Assert.Equal("10 LAPS TO GO", Broadcast93RaceBoard.LapsToGoText(10));
        Assert.Equal("1 LAP TO GO", Broadcast93RaceBoard.LapsToGoText(1));
        Assert.Equal(Broadcast93RaceBoard.Mode.Caption, Broadcast93RaceBoard.Select(WithLapStart(Race(7), 0)));
        var timed = WithLapStart(Race(10), 0);
        timed = timed with { Board = timed.Board! with { LapsRemaining = null }, Session = timed.Session! with { LapsInEvent = 0 } };
        Assert.Equal(Broadcast93RaceBoard.Mode.Caption, Broadcast93RaceBoard.Select(timed));
    }

    [Fact]
    public void Pit_timer_runs_while_stopped_and_holds_the_last_value_for_three_seconds()
    {
        var running = WithPit(Race(), 12.3);
        Assert.Equal(12.3, Broadcast93RaceBoard.PitSeconds(running.Broadcast!, Now), 6);
        var left = running.Broadcast! with { PlayerStopped = false, PlayerStopEndT = Now - 1, PlayerStopSeconds = 2.4, PlayerInPitLane = false };
        Assert.True(Broadcast93RaceBoard.PitActive(left, Now));
        Assert.Equal(2.4, Broadcast93RaceBoard.PitSeconds(left, Now));
        Assert.False(Broadcast93RaceBoard.PitActive(left, Now + 3));
        var entering = BroadcastState.Empty with { PlayerInPitLane = true, PlayerPitLaneStartT = Now - 2, PlayerStopEndT = Now - 50, PlayerStopSeconds = 9 };
        Assert.True(Broadcast93RaceBoard.PitActive(entering, Now));
        Assert.Equal(0, Broadcast93RaceBoard.PitSeconds(entering, Now));
    }

    [Fact]
    public void Photo_key_normalisation()
    {
        Assert.Equal("alain_prost", DriverPhotos.Key("Alain Prost"));
        Assert.Equal("jean_eric_vergne", DriverPhotos.Key("  Jean-Éric  VERGNE "));
        Assert.Equal("ayrton_senna_da_silva", DriverPhotos.Key("Ayrton Senna da Silva"));
        Assert.Equal("nico_hulkenberg", DriverPhotos.Key("Nico Hülkenberg"));
        Assert.Equal("mclaren_mp4_8_ford", DriverPhotos.Key("McLaren MP4/8 (Ford)"));
        Assert.Equal("", DriverPhotos.Key("  ---  "));
        Assert.Equal("", DriverPhotos.Key(null));
        Assert.Null(DriverPhotos.DriverPath("Nobody Atall"));
        Assert.Null(DriverPhotos.CarPath(""));
    }

    [Fact]
    public void Review_frames_race_states()
    {
        var board = new Broadcast93RaceBoard();
        var m = Race(10);
        Render(c => board.Draw(c, m, Cfg)); // baseline: the next better time is an event
        var fastest = WithFastest(m, 87.438) with { Now = Now + 1 };
        Assert.Contains(Render(c => board.Draw(c, fastest, Cfg), "e2-board-fastest"), b => b != 0);
        Assert.Contains(Render(c => board.Draw(c, WithGap(m with { Now = Now + 30 }), Cfg), "e2-board-gap"), b => b != 0);
        Assert.Contains(Render(c => board.Draw(c, WithLapStart(m with { Now = Now + 60 }, 1), Cfg), "e2-board-lapstogo10"), b => b != 0);
        Assert.Equal(Broadcast93RaceBoard.Mode.Caption, Broadcast93RaceBoard.Select(WithLapStart(Race(1), 1)));   // 1 is not a multiple of 5: the singular text is only a formatter
        Assert.Contains(Render(c => Broadcast93RaceBoard.LapsToGo(c, 1), "e2-board-lapstogo-singular-forced"), b => b != 0);
        Assert.Contains(Render(c => board.Draw(c, WithPit(m with { Now = Now + 120 }, 12.3), Cfg), "e2-board-pit-running"), b => b != 0);
        var left = m with { Now = Now + 150, Broadcast = BroadcastState.Empty with { PlayerStopEndT = Now + 149, PlayerStopSeconds = 2.4 } };
        Assert.Contains(Render(c => board.Draw(c, left, Cfg), "e2-board-pit-held"), b => b != 0);
    }

    [Fact]
    public void Review_frames_quali_states()
    {
        var m = LayoutPreview.WithCountries(LayoutPreview.ForWidget("qualilap")) with { Now = 100, PlayerDriving = true };
        m = m with { Session = Named(m.Session! with { InSession = true, GameState = 2 }) };
        var other = m.Session!.Cars.First(c => !c.IsPlayer);
        m = m with { QualiLap = m.QualiLap! with { OutLap = false, InPit = false, Elapsed = 30, LastResult = null, LastSplit = null, LeaderIndex = other.Index, LeaderBestLap = 72.533 } };
        var board = new Broadcast93QualiBoard();
        var caption = Render(c => board.Draw(c, m, QualiCfg), "e2-quali-C-caption");
        Assert.Contains(caption, b => b != 0);
        var result = new QualiLapResult(3, 72.266, 1, -.267, null, true, false, [null, null, null], 100, true) { ReferenceCar = other with { BestLapTime = 72.533 } };
        var resultFrame = m with { QualiLap = m.QualiLap with { LastResult = result } };
        var a = Render(c => board.Draw(c, resultFrame, QualiCfg), "e2-quali-A-result");
        Assert.NotEqual(caption, a);
        var invalid = resultFrame with { QualiLap = resultFrame.QualiLap! with { LastResult = result with { Invalid = true } } };
        Assert.NotEqual(a, Render(c => board.Draw(c, invalid, QualiCfg), "e2-quali-A-invalid"));
        // B: another driver improves the best while no result is showing.
        var baseline = m with { Now = 120 };
        var fresh = new Broadcast93QualiBoard();
        Render(c => fresh.Draw(c, baseline, QualiCfg));
        var improved = baseline with { Now = 121, Session = baseline.Session! with { Cars = baseline.Session.Cars.Select(c => c.Index == other.Index ? c with { BestLapTime = Math.Max(1, baseline.Session.Cars.Where(x => x.BestLapTime > 0).Select(x => x.BestLapTime).DefaultIfEmpty(80).Min() - 1) } : c).ToArray() } };
        var b = Render(c => fresh.Draw(c, improved, QualiCfg), "e2-quali-B-fastest");
        Assert.NotEqual(caption, b);
        Assert.NotEqual(b, Render(c => fresh.Draw(c, improved with { Now = 129 }, QualiCfg)));   // after 7 s: back to the caption
        // A takes priority over B at the same moment.
        var both = improved with { QualiLap = improved.QualiLap! with { LastResult = result with { At = 121 } } };
        Assert.NotEqual(b, Render(c => fresh.Draw(c, both, QualiCfg)));
    }
}
