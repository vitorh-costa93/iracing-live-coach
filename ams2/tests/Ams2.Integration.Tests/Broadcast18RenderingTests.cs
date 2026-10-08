using Ams2.Core.Calc;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Gfx;
using Ams2.OverlayHost.Theme;
using Ams2.OverlayHost.Widgets;
using Ams2.Shared.Profiles;

namespace Ams2.Integration.Tests;

public class Broadcast18RenderingTests
{
    // Bounded, sequential, one device, without a game or host windows.
    [Fact]
    public void All_2018_widgets_render_and_pit_lane_release_keeps_the_plate_visible()
    {
        const int width = 1100, height = 1000;
        using var gfx = DeviceResources.CreateOffscreen(width, height);
        using var canvas = new ThemeCanvas(gfx, Themes.F1_2018) { Opacity = .7f };
        byte[] Render(IWidget widget, OverlayModel model, string? name = null)
        {
            gfx.BeginFrame(); canvas.Begin(); widget.Draw(canvas, model); canvas.End(); gfx.EndFrame();
            Assert.Equal(.7f, canvas.Opacity);
            var pixels = gfx.ReadPixelsBgra();
            if (name is not null && Environment.GetEnvironmentVariable("AMS2_REVIEW_FRAMES") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                PngWriter.SaveFromPremultipliedBgra(Path.Combine(directory, name + ".png"), pixels, width, height, (45, 50, 55));
            }
            return pixels;
        }
        foreach (var definition in WidgetCatalog.ForTheme("f1-2018"))
        {
            var widget = WidgetRegistry.Create(definition.Id);
            widget.UseTheme(Themes.F1_2018);
            widget.Configure(LayoutPreview.Settings(new WidgetSettings { Id = definition.Id,
                Columns = definition.Id == "radar" ? ["always"] : null }));
            var pixels = Render(widget, LayoutPreview.ForWidget(definition.Id), "2018-" + definition.Id);
            Assert.True(pixels.Any(b => b != 0), definition.Id + " should be visible in the configured preview");
        }
        var timer = new PitTimerWidget(); timer.UseTheme(Themes.F1_2018);
        timer.Configure(new WidgetSettings { Id = "pittimer", Columns = [] });
        var model = LayoutPreview.ForWidget("pittimer");
        var state = model.Broadcast! with { SessionSeenT = 1, PlayerInPitLane = false, PlayerStopped = false,
            PlayerStopStartT = double.NegativeInfinity, PlayerStopEndT = double.NegativeInfinity };
        Assert.DoesNotContain(Render(timer, model with { Now = 100, Broadcast = state }), b => b != 0);
        state = state with { PlayerInPitLane = true, PlayerPitLaneStartT = 101 };
        Assert.DoesNotContain(Render(timer, model with { Now = 101, Broadcast = state }), b => b != 0);
        var entry = Render(timer, model with { Now = 101.12, Broadcast = state }, "2018-pit-entry");
        Assert.Contains(entry, b => b != 0); // visible before the car stops
        state = state with { PlayerStopped = true, PlayerStopStartT = 102 };
        Render(timer, model with { Now = 104, Broadcast = state });
        var released = Render(timer, model with { Now = 104, Broadcast = state with { PlayerStopped = false, PlayerStopEndT = 104, PlayerStopSeconds = 2 } }, "2018-pit-release");
        Assert.Contains(released, b => b != 0);
        state = state with { PlayerInPitLane = false, PlayerStopped = false, PlayerStopEndT = 104, PlayerStopSeconds = 2 };
        Assert.Contains(Render(timer, model with { Now = 105, Broadcast = state }), b => b != 0);
        Assert.DoesNotContain(Render(timer, model with { Now = 109.21, Broadcast = state }), b => b != 0);
        // A scoped reveal must not leak the clip into the next widget/frame.
        var speed = new LiveSpeedWidget();
        speed.Configure(LayoutPreview.Settings(new WidgetSettings { Id = "livespeed" }));
        Assert.Contains(Render(speed, LayoutPreview.ForWidget("livespeed")), b => b != 0);
        // Rendering uses the retained number too, rather than only testing the Core cache.
        var tower = new StandingsWidget(); tower.UseTheme(Themes.F1_2018);
        tower.Configure(new WidgetSettings { Id = "standings", Columns = ["pos", "name", "gap"],
            Options = new() { ["mode"] = "interval", ["battle"] = "false" } });
        var standings = LayoutPreview.ForWidget("standings") with { Now = 200 };
        var original = Render(tower, standings, "2018-interval-held");
        var changedRows = standings.Standings.Select((r, i) => i == 1 ? r with { GapToLeader = r.GapToLeader + .4 } : r).ToArray();
        var changed = standings with { Now = 200.9, Standings = changedRows };
        Assert.Equal(original, Render(tower, changed));
        Assert.NotEqual(original, Render(tower, changed with { Now = 201 }, "2018-interval-updated"));
        var pitRows = changedRows.Select((r, i) => i == 1 ? r with { Car = r.Car with { PitState = Ams2.Core.PitState.InPit } } : r).ToArray();
        Assert.NotEqual(Render(tower, changed with { Now = 201.1 }), Render(tower, changed with { Now = 201.1, Standings = pitRows }));
        var quali = new QualiLapWidget(); quali.UseTheme(Themes.F1_2018);
        quali.Configure(new WidgetSettings { Id = "qualilap", Options = new() { ["always"] = "true", ["compareTo"] = "personal" } });
        var qualModel = LayoutPreview.ForWidget("qualilap") with { Now = 300 };
        var result = new QualiLapResult(2, 78.9, 7, .6, -.4, false, false, [], 300, true);
        qualModel = qualModel with { QualiLap = qualModel.QualiLap! with { LastResult = result } };
        int GreenPixels(byte[] pixels)
        {
            int count = 0;
            for (int y = 84; y < 122; y++)
                for (int x = 215; x < 340; x++)
                {
                    int offset = (y * width + x) * 4;
                    if (pixels[offset + 1] > pixels[offset + 2] + 15 && pixels[offset + 1] > pixels[offset] + 15) count++;
                }
            return count;
        }
        Assert.True(GreenPixels(Render(quali, qualModel, "2018-quali-faster")) > 0);
        Assert.Equal(0, GreenPixels(Render(quali, qualModel with { QualiLap = qualModel.QualiLap! with { LastResult = result with { Invalid = true } } })));
    }
}
