using Ams2.OverlayHost.Data;
using Ams2.Core.Calc;
using Ams2.OverlayHost.Gfx;
using Ams2.OverlayHost.Theme;
using Ams2.OverlayHost.Widgets;
using Ams2.Shared.Profiles;

namespace Ams2.Integration.Tests;

public class Broadcast04AnimationRenderTests
{
    // One device, sequential frames, no windows/game/fake host. Deliberately bounded after the earlier freeze.
    [Fact]
    public void All_2004_widgets_render_and_stop_release_does_not_replay_entry()
    {
        const int width = 1100, height = 700;
        using var gfx = DeviceResources.CreateOffscreen(width, height);
        using var canvas = new ThemeCanvas(gfx, Themes.F1_2004) { Opacity = .7f };
        byte[] Render(IWidget widget, OverlayModel model)
        {
            gfx.BeginFrame(); canvas.Begin(); widget.Draw(canvas, model); canvas.End(); gfx.EndFrame();
            Assert.Equal(.7f, canvas.Opacity);
            return gfx.ReadPixelsBgra();
        }
        foreach (var definition in WidgetCatalog.ForTheme("f1-2004"))
        {
            var widget = WidgetRegistry.Create(definition.Id);
            widget.UseTheme(Themes.F1_2004);
            widget.Configure(LayoutPreview.Settings(new WidgetSettings { Id = definition.Id }));
            var pixels = Render(widget, LayoutPreview.ForWidget(definition.Id));
            Assert.Equal(width * height * 4, pixels.Length);
            Assert.True(widget.DesignSize.Width > 0 && widget.DesignSize.Height > 0);
        }
        var timer = new PitTimerWidget(); timer.UseTheme(Themes.F1_2004);
        timer.Configure(new WidgetSettings { Id = "pittimer", Columns = [] });
        var model = LayoutPreview.ForWidget("pittimer");
        var stop = model.Broadcast! with { PlayerStopped = true, PlayerStopStartT = 100, PlayerStopEndT = double.NegativeInfinity };
        var hidden = Render(timer, model with { Now = 100, Broadcast = stop });
        Assert.DoesNotContain(hidden, b => b != 0);
        var entered = Render(timer, model with { Now = 100.08, Broadcast = stop });
        Assert.Contains(entered, b => b != 0);
        var stopped = Render(timer, model with { Now = 102, Broadcast = stop });
        var released = Render(timer, model with { Now = 102, Broadcast = stop with { PlayerStopped = false, PlayerStopEndT = 102, PlayerStopSeconds = 2 } });
        Assert.Equal(stopped, released);
        Assert.DoesNotContain(Render(timer, model with { Now = 106, Broadcast = stop with { PlayerStopped = false, PlayerStopEndT = 102, PlayerStopSeconds = 2 } }), b => b != 0);
    }

    [Fact]
    public void Right_column_arrives_without_reanimating_the_completed_left_column()
    {
        const int width = 592, height = 164;
        using var gfx = DeviceResources.CreateOffscreen(width, height);
        using var canvas = new ThemeCanvas(gfx, Themes.F1_2004);
        var board = new BoardWidget(); board.UseTheme(Themes.F1_2004);
        board.Configure(new WidgetSettings { Id = "board", Columns = [] });
        string[] names = ["Klien", "Button", "Zonta", "Massa", "Baumgartner", "Panis", "Heidfeld"];
        var entries = names.Select((name, i) => new BoardTowerEntry(i + 1, i / 4, i % 4, i + 9, i + 9,
            i, name, name, name[..3], BoardGapKind.Time, 11.833 + i * 2, 0, "", false, "", i < 4 ? 1 : 10)).ToArray();
        var tower = new BoardTower(7, 1, 2, 8, entries[..4], 12, 16, false, 1, 20);
        var model = LayoutPreview.ForWidget("board") with { Now = 10,
            Board = new BoardState(BoardMode.LineTower, 1, 10, true, 1, 20, 10, 4, tower, null, null, null) };
        byte[] Render(OverlayModel frame, string name)
        {
            gfx.BeginFrame(); canvas.Begin(); board.Draw(canvas, frame); canvas.End(); gfx.EndFrame();
            var pixels = gfx.ReadPixelsBgra();
            if (Environment.GetEnvironmentVariable("AMS2_REVIEW_FRAMES") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                PngWriter.SaveFromPremultipliedBgra(Path.Combine(directory, name + ".png"), pixels, width, height, (45, 50, 55));
            }
            return pixels;
        }
        var left = Render(model, "2004-tower-left");
        var full = Render(model with { Now = 10.4, Board = model.Board! with { Now = 10.4, Tower = tower with { Entries = entries }, ItemCount = 7 } }, "2004-tower-both");
        for (int y = 0; y < height; y++)
            Assert.True(left.AsSpan(y * width * 4, 284 * 4).SequenceEqual(full.AsSpan(y * width * 4, 284 * 4)));
        Assert.NotEqual(left, full);
        Assert.Equal((592f, 164f), board.DesignSize);
    }

    [Fact]
    public void Quali_delta_cell_is_green_for_faster_partial_and_result_against_selected_reference()
    {
        const int width = 350, height = 126;
        using var gfx = DeviceResources.CreateOffscreen(width, height);
        using var canvas = new ThemeCanvas(gfx, Themes.F1_2004);
        var model = LayoutPreview.ForWidget("qualilap") with { Now = 10.2 };
        var q = model.QualiLap! with { InPit = false, OutLap = false, Elapsed = 24.2,
            LastSplit = new QualiSplit(1, 24.2, -.471, .685, 10), LastResult = null };
        foreach (bool result in new[] { false, true })
        foreach (bool personal in new[] { false, true })
        {
            var widget = new QualiLapWidget(); widget.UseTheme(Themes.F1_2004);
            widget.Configure(new WidgetSettings { Id = "qualilap", Options = new() { ["compareTo"] = personal ? "personal" : "leader" } });
            var frame = model with { QualiLap = result ? q with { LastResult = new QualiLapResult(2, 78.917, 7, .685, -.471, true, false, [], 10, true) } : q };
            gfx.BeginFrame(); canvas.Begin(); widget.Draw(canvas, frame); canvas.End(); gfx.EndFrame();
            var pixels = gfx.ReadPixelsBgra();
            int offset = (88 * width + 70) * 4;
            Assert.Equal(personal, pixels[offset + 1] > pixels[offset + 2]); // green > red vs orange red > green
            if (result && personal && Environment.GetEnvironmentVariable("AMS2_REVIEW_FRAMES") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                PngWriter.SaveFromPremultipliedBgra(Path.Combine(directory, "2004-quali-faster.png"), pixels, width, height, (45, 50, 55));
            }
        }
    }
}
