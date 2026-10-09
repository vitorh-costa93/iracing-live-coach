using Ams2.Core.Calc;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Gfx;
using Ams2.OverlayHost.Theme;
using Ams2.OverlayHost.Widgets;
using Ams2.Shared.Profiles;

namespace Ams2.Integration.Tests;

public class Broadcast93RenderTests
{
    static readonly WidgetSettings QualiConfig = new() { Id = "qualiboard" };
    static OverlayModel Flying()
    {
        var m = LayoutPreview.ForWidget("qualilap") with { Now = 100, PlayerDriving = true };
        return m with { Session = m.Session! with { InSession = true, GameState = 2 },
            QualiLap = m.QualiLap! with { OutLap = false, InPit = false, Elapsed = .5, Sector = 0, LastResult = null, LastSplit = null,
                PersonalBestLap = null, LeaderBestLap = null, LeaderIndex = -1, PersonalBestSectors = [null, null, null], OverallBestSectors = [null, null, null] } };
    }

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

    [Fact]
    public void First_lap_without_reference_and_late_slower_reference_keep_current_clock()
    {
        var board = new Broadcast93QualiBoard(); var m = Flying();
        var initial = Render(c => board.Draw(c, m, QualiConfig), "1993-quali-first");
        Assert.Contains(initial, b => b != 0);
        m = m with { QualiLap = m.QualiLap! with { Elapsed = 75 } };
        var empty = Render(c => board.Draw(c, m, QualiConfig));
        var later = m with { QualiLap = m.QualiLap! with { LeaderBestLap = 72.830, LeaderIndex = m.Session!.Cars.First(car => !car.IsPlayer).Index } };
        var reference = Render(c => board.Draw(c, later, QualiConfig), "1993-quali-slower");
        Assert.Equal(72.830, Broadcast93QualiBoard.Resolve(later, QualiConfig)!.ReferenceTime);
        Assert.NotEqual(empty, reference);
        for (int row = 0; row < 300; row++)
            Assert.Equal(empty[(row * 1920 * 4)..((row * 1920 + 700) * 4)], reference[(row * 1920 * 4)..((row * 1920 + 700) * 4)]);
    }

    [Fact]
    public void Partial_clock_continues_and_completed_lap_uses_previous_reference()
    {
        var m = Flying();
        m = m with { QualiLap = m.QualiLap! with { Elapsed = 44.9, LastSplit = new(1, 41.971, null, -.290, 99), LeaderBestLap = 72.830 } };
        var view = Broadcast93QualiBoard.Resolve(m, QualiConfig)!;
        Assert.Equal(44.9, view.Lap.Elapsed); Assert.Equal(42.261, view.ReferenceTime!.Value, 3); Assert.Equal(-.290, view.Delta);
        var board = new Broadcast93QualiBoard();
        Assert.Contains(Render(c => board.Draw(c, m, QualiConfig), "1993-quali-split"), b => b != 0);
        m = m with { QualiLap = m.QualiLap with { LeaderBestLap = 72.533,
            LastResult = new(1, 72.533, 1, -.297, null, true, false, [null, null, null], 99, true) } };
        view = Broadcast93QualiBoard.Resolve(m, QualiConfig)!;
        Assert.Equal(72.830, view.ReferenceTime!.Value, 3);
        Assert.Contains(Render(c => board.Draw(c, m, QualiConfig), "1993-quali-result"), b => b != 0);
    }

    [Theory]
    [InlineData(1)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void Menu_pause_replay_hide_all_plates(int gameState)
    {
        var m = Flying(); m = m with { Session = m.Session! with { GameState = (uint)gameState, InSession = false } };
        var quali = new Broadcast93QualiBoard(); var race = new Broadcast93RaceBoard();
        Assert.DoesNotContain(Render(c => quali.Draw(c, m, QualiConfig)), b => b != 0);
        Assert.DoesNotContain(Render(c => race.Draw(c, m, new() { Id = "board", Columns = ["always"] })), b => b != 0);
        Assert.Equal(Broadcast93RaceBoard.Mode.None, Broadcast93RaceBoard.Select(m));
    }

    [Fact]
    public void Race_gap_is_fixed_without_countdown_and_outlap_uses_caption()
    {
        var m = LayoutPreview.ForWidget("board") with { Now = 100, PlayerDriving = true };
        var player = new BoardDriver(0, 2, "Ayrton Senna", "Senna", "SEN", "car", "", true);
        var ahead = new BoardDriver(1, 1, "Michael Schumacher", "Schumacher", "MSC", "car", "", false);
        var gap = new BoardGap93(player, ahead, true, 12.668, "12.668", 87.332, 100, 107);
        m = m with { Session = m.Session! with { InSession = true, GameState = 2 }, Board = m.Board! with { Gap93 = gap } };
        var board = new Broadcast93RaceBoard(); var cfg = new WidgetSettings { Id = "board", Columns = [] };
        var first = Render(c => board.Draw(c, m, cfg), "1993-race-gap");
        Assert.Equal(first, Render(c => board.Draw(c, m with { Now = 103 }, cfg)));
        Assert.Equal(Broadcast93RaceBoard.Mode.Caption, Broadcast93RaceBoard.Select(m with { Now = 107 }));
        m = Flying(); m = m with { QualiLap = m.QualiLap! with { OutLap = true, Elapsed = null } };
        var quali = new Broadcast93QualiBoard();
        Assert.Contains(Render(c => quali.Draw(c, m, QualiConfig), "1993-quali-caption"), b => b != 0);
        Assert.Equal("1'00.000", Broadcast93QualiBoard.Time(59.9999));
    }

    [Fact]
    public void Fastest_uses_new_real_best_only_and_respects_toggle()
    {
        var m = LayoutPreview.ForWidget("board") with { Now = 100, PlayerDriving = true, Board = null, Broadcast = BroadcastState.Empty };
        m = m with { Session = m.Session! with { InSession = true, GameState = 2 } };
        var board = new Broadcast93RaceBoard(); var cfg = new WidgetSettings { Id = "board", Columns = [] };
        Assert.DoesNotContain(Render(c => board.Draw(c, m, cfg)), b => b != 0);
        var bestCar = m.Session.Cars.MinBy(car => car.BestLapTime)!;
        var updated = m with { Now = 101, Session = m.Session with { Cars = m.Session.Cars.Select(car => car.Index == bestCar.Index ? car with { BestLapTime = car.BestLapTime - .4 } : car).ToArray() } };
        var visible = Render(c => board.Draw(c, updated, cfg), "1993-race-fastest");
        Assert.Contains(visible, b => b != 0);
        Assert.DoesNotContain(Render(c => board.Draw(c, updated, cfg with { Options = new() { ["showFastest"] = "false" } })), b => b != 0);
        Assert.DoesNotContain(Render(c => board.Draw(c, updated with { Now = 108 }, cfg)), b => b != 0);
    }

    [Fact]
    public void First_timed_lap_after_observed_session_without_times_shows_fastest()
    {
        var m = LayoutPreview.ForWidget("board") with { Now = 100, PlayerDriving = true, Board = null, Broadcast = BroadcastState.Empty };
        m = m with { Session = m.Session! with { InSession = true, GameState = 2,
            Cars = m.Session!.Cars.Select(car => car with { BestLapTime = 0 }).ToArray() } };
        var board = new Broadcast93RaceBoard(); var cfg = new WidgetSettings { Id = "board", Columns = [] };
        Assert.DoesNotContain(Render(c => board.Draw(c, m, cfg)), b => b != 0);
        var updated = m with { Now = 101, Session = m.Session with { Cars = m.Session.Cars.Select(car => car.IsPlayer ? car with { BestLapTime = 80.123 } : car).ToArray() } };
        var actual = Render(c => board.Draw(c, updated, cfg));
        Assert.Contains(actual, b => b != 0);
        Assert.Equal(Render(c => Broadcast93RaceBoard.Fastest(c, updated.Session!.PlayerCar!, updated.Session.Cars, cfg, updated.Session.TrackLength)), actual);
        // Attaching after that time already exists seeds the baseline without a stale event.
        var attached = new Broadcast93RaceBoard();
        Assert.DoesNotContain(Render(c => attached.Draw(c, updated, cfg)), b => b != 0);
    }

    [Fact]
    public void Session_generation_resets_fastest_without_drawing_hidden_loading_frames()
    {
        var m = LayoutPreview.ForWidget("board") with { Now = 100, PlayerDriving = true, Board = null, Broadcast = BroadcastState.Empty,
            QualiLap = QualiLapState.Empty with { SessionGeneration = 1 } };
        m = m with { Session = m.Session! with { InSession = true, GameState = 2 } };
        var cfg = new WidgetSettings { Id = "board", Columns = [] }; var board = new Broadcast93RaceBoard();
        Render(c => board.Draw(c, m, cfg));
        var best = m.Session.Cars.MinBy(car => car.BestLapTime)!;
        var record = m with { Now = 101, Session = m.Session with { Cars = m.Session.Cars.Select(car => car.Index == best.Index ? car with { BestLapTime = best.BestLapTime - .4 } : car).ToArray() } };
        Assert.Contains(Render(c => board.Draw(c, record, cfg)), b => b != 0);
        // Same track, player, clock direction and lap count; only the provider's generation changes.
        var restarted = record with { Now = 102, QualiLap = record.QualiLap! with { SessionGeneration = 2 } };
        Assert.DoesNotContain(Render(c => board.Draw(c, restarted, cfg)), b => b != 0);
        Assert.Equal(Render(c => new Broadcast93RaceBoard().Draw(c, restarted, cfg)), Render(c => board.Draw(c, restarted, cfg)));
    }

    [Fact]
    public void Result_uses_captured_rival_identity_when_player_is_current_leader()
    {
        var m = Flying(); var player = m.Session!.PlayerCar!;
        var rival = m.Session.Cars.First(car => !car.IsPlayer) with { Name = "Actual Rival", BestLapTime = 72.830 };
        m = m with { Session = m.Session with { Cars = m.Session.Cars.Select(car => car.Index == player.Index ? car with { BestLapTime = 72.830 } : car.Index == rival.Index ? car with { Name = "Changed Rival", BestLapTime = 71.500 } : car).ToArray() },
            QualiLap = m.QualiLap! with { LeaderIndex = player.Index, LeaderBestLap = 72.830 } };
        var board = new Broadcast93QualiBoard();
        Render(c => board.Draw(c, m, QualiConfig)); // Caches the player, who led before this completed lap.
        var result = new QualiLapResult(1, 72.533, 1, -.297, null, true, false, [null, null, null], 100, true) { ReferenceCar = rival };
        var completed = m with { QualiLap = m.QualiLap with { LastResult = result, LeaderBestLap = 72.533 } };
        var actual = Render(c => board.Draw(c, completed, QualiConfig));
        var expected = completed with
        {
            Session = completed.Session! with { Cars = completed.Session.Cars.Select(car => car.Index == rival.Index ? rival : car).ToArray() },
            QualiLap = completed.QualiLap! with { LeaderIndex = rival.Index, LastResult = result with { ReferenceCar = null } }
        };
        Assert.Equal(Render(c => new Broadcast93QualiBoard().Draw(c, expected, QualiConfig)), actual);
        var wrong = completed with { QualiLap = completed.QualiLap with { LastResult = result with { ReferenceCar = rival with { Name = "Wrong Driver" } } } };
        Assert.NotEqual(actual, Render(c => new Broadcast93QualiBoard().Draw(c, wrong, QualiConfig)));
    }

    [Fact]
    public void Driver_caption_onboard_differs_from_full_and_obeys_events_and_outlap()
    {
        var m = LayoutPreview.ForWidget("board") with { Now = 100, PlayerDriving = true, Board = null, Broadcast = BroadcastState.Empty with { SessionSeenT = 100 } };
        m = m with { Session = m.Session! with { InSession = true, GameState = 2 } };
        var cfg = new WidgetSettings { Id = "drivercaption", Columns = ["team"], Options = new() { ["captionMode"] = "onboard" } };
        var caption = new DriverCaptionWidget(); caption.UseTheme(Themes.F1_1993); caption.Configure(cfg);
        var onboard = Render(c => caption.Draw(c, m));
        Assert.Equal(Render(c => Broadcast93RaceBoard.Onboard(c, m.Session!.PlayerCar!, m.Session.Cars, cfg)), onboard);
        caption.Configure(cfg with { Options = new() { ["captionMode"] = "full" } });
        var full = Render(c => caption.Draw(c, m));
        Assert.NotEqual(onboard, full);
        Assert.Equal(Render(c => Broadcast93RaceBoard.Caption(c, m, cfg)), full);
        Assert.DoesNotContain(Render(c => caption.Draw(c, m with { Now = 107 })), b => b != 0);
        var quali = Flying() with { Broadcast = BroadcastState.Empty with { SessionSeenT = 100 } };
        Assert.DoesNotContain(Render(c => caption.Draw(c, quali)), b => b != 0);
        quali = quali with { QualiLap = quali.QualiLap! with { OutLap = true, Elapsed = null } };
        Assert.Contains(Render(c => caption.Draw(c, quali)), b => b != 0);
    }
}
