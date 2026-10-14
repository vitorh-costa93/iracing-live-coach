using Ams2.Core;
using Ams2.Core.Calc;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Gfx;
using Ams2.OverlayHost.Theme;
using Ams2.OverlayHost.Widgets;
using Ams2.Shared.Profiles;

namespace Ams2.Integration.Tests;

public class QualiBoardRenderTests
{
    static OverlayModel Model(double now)
    {
        var rows = Enumerable.Range(0, 16).Select(i =>
        {
            var car = new CarSnapshot(i, "Driver " + i, "car", "class", i + 1, i + 1, 1, 2, 0, 0,
                71.92 + i * .1, 71.92 + i * .1, 50, PitState.None, RaceState.Racing, false, i == 0);
            return new QualiRow(car, i + 1, car.BestLapTime, i * .1, QualiStatus.TimeSet, i == 0);
        }).ToArray();
        return OverlayModel.Empty with { Connected = true, Now = now, Quali = new(rows, 500),
            QualiLap = QualiLapState.Empty with { CarIndex = 0, OutLap = true, Lap = 1 } };
    }

    static byte[] Render(IWidget widget, OverlayModel model, float opacity = 1, string? artifact = null, Theme? theme = null)
    {
        using var gfx = DeviceResources.CreateOffscreen(1920, 300);
        using var canvas = new ThemeCanvas(gfx, theme ?? Themes.F1_1998) { Opacity = opacity };
        gfx.BeginFrame(); canvas.Begin(); widget.Draw(canvas, model); canvas.End(); gfx.EndFrame();
        Assert.Equal(opacity, canvas.Opacity);
        var pixels = gfx.ReadPixelsBgra();
        if (artifact is not null && Environment.GetEnvironmentVariable("AMS2_REVIEW_FRAMES") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            PngWriter.SaveFromPremultipliedBgra(Path.Combine(directory, artifact + ".png"), pixels, 1920, 300, (45, 50, 55));
        }
        return pixels;
    }

    [Theory]
    [InlineData("f1-1998")]
    [InlineData("f1-2004")]
    [InlineData("f1-2018")]
    public void First_flying_lap_is_visible_without_reference_and_late_reference_updates(string themeId)
    {
        var theme = themeId == "f1-1998" ? Themes.F1_1998 : themeId == "f1-2004" ? Themes.F1_2004 : Themes.F1_2018;
        var model = LayoutPreview.ForWidget("qualilap") with { Now = 100 };
        var q = model.QualiLap! with { InPit = false, OutLap = false, Elapsed = .5, LastResult = null, LastSplit = null,
            PersonalBestLap = null, LeaderBestLap = null, LeaderIndex = -1, Sector = 2,
            PersonalBestSectors = [null, null, null], OverallBestSectors = [null, null, null], Sectors = [null, null, null] };
        var lap = new QualiLapWidget(); lap.UseTheme(theme);
        Assert.Contains(Render(lap, model with { QualiLap = q }, artifact: themeId + "-quali-first-no-reference", theme: theme), b => b != 0);
        q = q with { Elapsed = 73 };
        var without = Render(lap, model with { QualiLap = q }, theme: theme);
        var withReference = Render(lap, model with { QualiLap = q with { LeaderBestLap = 78, LeaderIndex = 1 } }, theme: theme);
        Assert.NotEqual(without, withReference);
        var tower = new QualiTowerWidget(); tower.UseTheme(theme);
        foreach (var elapsed in new[] { .5, 5, 8, 11.9, 12, 73 })
            Assert.DoesNotContain(Render(tower, model with { QualiLap = q with { Elapsed = elapsed } }, theme: theme), b => b != 0);
    }

    [Theory]
    [InlineData("f1-1998")]
    [InlineData("f1-2004")]
    [InlineData("f1-2018")]
    public void Standalone_tower_finishes_one_pass_then_caption_updates_leader_gap(string themeId)
    {
        var theme = themeId == "f1-1998" ? Themes.F1_1998 : themeId == "f1-2004" ? Themes.F1_2004 : Themes.F1_2018;
        var tower = new QualiTowerWidget(); tower.UseTheme(theme);
        var model = LayoutPreview.ForWidget("qualitower") with { Now = 100 };
        model = model with { QualiLap = model.QualiLap! with { OutLap = true, InPit = false, Elapsed = null } };
        Render(tower, model, theme: theme);
        var caption = Render(tower, model with { Now = 200 }, artifact: themeId + "-quali-outlap-caption", theme: theme);
        Assert.Contains(caption, b => b != 0);
        foreach (uint gameState in new uint[] { 1, 4, 5, 6 })
            Assert.DoesNotContain(Render(tower, model with { Now = 210, QualiLap = null,
                Session = model.Session! with { InSession = false, GameState = gameState } }, theme: theme), b => b != 0);
        Assert.Equal(caption, Render(tower, model with { Now = 220 }, theme: theme));
        var changed = model with { Now = 221, Quali = model.Quali! with
        {
            Rows = model.Quali.Rows.Select(r => r.IsPlayer ? r with { GapToFirst = 2.345 } : r).ToArray()
        } };
        Assert.NotEqual(caption, Render(tower, changed, theme: theme));
    }

    [Fact]
    public void Board99_outlap_has_no_pagination_and_flying_lap_has_all_five_states()
    {
        var board = new QualiBoardWidget(); board.UseTheme(Themes.F1_1998);
        var cfg = new WidgetSettings { Id = "qualiboard" };
        board.Configure(cfg);
        var model = LayoutPreview.ForWidget("qualilap") with { Now = 100, PlayerDriving = true };
        var q = model.QualiLap! with { OutLap = true, InPit = false, LastResult = null, LastSplit = null };
        var outlap = model with { QualiLap = q };
        var caption = Render(board, outlap, artifact: "quali99-outlap");
        Assert.Contains(caption, b => b != 0);
        Assert.Equal(caption, Render(board, outlap with { Now = 120, Quali = null }));
        Assert.Equal((1920f, 300f), board.DesignSize);
        var reference = new QualiTimingReference(model.Session!.Cars.First(c => !c.IsPlayer), [24.402, 55, 90], [85.14, 70, 72]);
        q = q with { OutLap = false, Elapsed = 24, Sector = 0, LeaderReference = reference, LeaderBestLap = 90 };
        var approach = Render(board, model with { QualiLap = q }, artifact: "quali99-approach");
        Assert.NotEqual(caption, approach);
        q = q with { LastSplit = new(1, 24.673, null, .271, 99) { LeaderReference = reference, SpeedMps = 83.39 }, Sector = 1 };
        var split = Render(board, model with { QualiLap = q }, artifact: "quali99-split");
        Assert.NotEqual(approach, split);
        q = q with { LastSplit = null, Elapsed = 30 };
        var live = Render(board, model with { QualiLap = q }, artifact: "quali99-live");
        Assert.NotEqual(split, live);
        q = q with { LastResult = new(1, 90.65, 5, .65, null, false, false, [null, null, null], 99, false) { SpeedMps = 73.64 } };
        var finish = Render(board, model with { QualiLap = q }, artifact: "quali99-finish");
        Assert.NotEqual(live, finish);
        Assert.DoesNotContain(Render(board, model with { QualiLap = q, PlayerDriving = false }), b => b != 0);
        Assert.DoesNotContain(Render(board, model with { QualiLap = q, Session = model.Session with { InSession = false, GameState = 5 } }), b => b != 0);
    }

    [Fact]
    public void Standalone_sizes_and_opacity_match_the_lower_board()
    {
        IWidget[] widgets = [new QualiLapWidget(), new QualiTowerWidget(), new QualiBoardWidget()];
        foreach (var widget in widgets)
        {
            widget.UseTheme(Themes.F1_1998);
            Assert.Equal((1920f, 300f), widget.DesignSize);
        }
        var tower = new QualiTowerWidget();
        var full = Render(tower, Model(20));
        var half = Render(tower, Model(20), .5f);
        Assert.NotEqual(full, half);
    }
}
