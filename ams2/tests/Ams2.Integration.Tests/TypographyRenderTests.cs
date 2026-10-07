using Ams2.Core.Calc;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Gfx;
using Ams2.OverlayHost.Theme;
using Ams2.OverlayHost.Widgets;
using Ams2.Shared.Profiles;

namespace Ams2.Integration.Tests;

public class TypographyRenderTests
{
    [Fact]
    public void Names_labels_and_numeric_values_remain_independent_in_2004_widgets()
    {
        using var gfx = DeviceResources.CreateOffscreen(800, 500);
        using var canvas = new ThemeCanvas(gfx, Themes.F1_2004);
        byte[] Render(IWidget widget, string role)
        {
            var settings = new WidgetSettings { Id = widget.Id, ElementFonts = role.Length == 0 ? null : new()
                { [role] = new() { Scale = .6f } } };
            canvas.Theme = ThemeOverrides.Apply(Themes.F1_2004, settings); canvas.ConfigureTypography(settings);
            widget.UseTheme(Themes.F1_2004); widget.Configure(LayoutPreview.Settings(settings));
            gfx.BeginFrame(); canvas.Begin(); widget.Draw(canvas, LayoutPreview.ForWidget(widget.Id)); canvas.End(); gfx.EndFrame();
            return gfx.ReadPixelsBgra();
        }
        static byte[] Region(byte[] pixels, int x, int y, int w, int h)
            => Enumerable.Range(y, h).SelectMany(row => pixels.AsSpan((row * 800 + x) * 4, w * 4).ToArray()).ToArray();
        var stops = new PitStopsWidget();
        var normal = Render(stops, ""); var name = Render(stops, "name"); var value = Render(stops, "value");
        Assert.Equal(Region(normal, 230, 30, 110, 120), Region(name, 230, 30, 110, 120));
        Assert.NotEqual(Region(normal, 230, 30, 110, 120), Region(value, 230, 30, 110, 120));
        var inputs = new InputsWidget();
        normal = Render(inputs, ""); var labels = Render(inputs, "label"); value = Render(inputs, "value");
        Assert.Equal(Region(normal, 300, 175, 38, 38), Region(labels, 300, 175, 38, 38));
        Assert.NotEqual(Region(normal, 300, 175, 38, 38), Region(value, 300, 175, 38, 38));
    }
    [Fact]
    public void All_widgets_apply_font_size_without_changing_their_design_size()
    {
        using var gfx = DeviceResources.CreateOffscreen(2000, 1100);
        using var canvas = new ThemeCanvas(gfx, Themes.F1_1998);
        byte[] Render(IWidget widget, WidgetSettings settings, Theme theme)
        {
            canvas.Theme = ThemeOverrides.Apply(theme, settings);
            canvas.ConfigureTypography(settings);
            widget.Configure(LayoutPreview.Settings(settings));
            gfx.BeginFrame(); canvas.Begin(); widget.Draw(canvas, LayoutPreview.ForWidget(widget.Id)); canvas.End(); gfx.EndFrame();
            return gfx.ReadPixelsBgra();
        }
        foreach (var theme in Themes.All)
        foreach (var definition in WidgetCatalog.ForTheme(theme.Id))
        {
            var widget = WidgetRegistry.Create(definition.Id); widget.UseTheme(theme);
            var settings = new WidgetSettings { Id = definition.Id, Columns = definition.Id == "radar" ? ["always"] : null };
            var normal = Render(widget, settings, theme);
            var size = widget.DesignSize;
            var small = Render(widget, settings with { TextScale = .6f }, theme);
            Assert.Equal(size, widget.DesignSize);
            Assert.False(normal.SequenceEqual(small), theme.Id + "/" + widget.Id);
        }
    }

    [Fact]
    public void Element_font_overrides_do_not_change_other_elements_and_override_literal_weights()
    {
        var settings = new WidgetSettings { Id = "qualiboard", TextScale = .8f, FontWeight = 700,
            ElementFonts = new() { ["name"] = new() { Scale = .5f, Weight = 300 } } };
        var theme = ThemeOverrides.Apply(Themes.F1_1998, settings);
        var name = ThemeOverrides.ResolveFont(theme.Text with { Size = 40, Weight = 900 }, settings);
        var time = ThemeOverrides.ResolveFont(theme.Numbers with { Size = 50, Element = "time", Weight = 400 }, settings);
        Assert.Equal(16, name.Size); Assert.Equal(300, name.Weight);
        Assert.Equal(40, time.Size); Assert.Equal(700, time.Weight);
        using var gfx = DeviceResources.CreateOffscreen(400, 100);
        using var canvas = new ThemeCanvas(gfx, theme);
        canvas.ConfigureTypography(settings);
        float width = canvas.Measure("SCHUMACHER", theme.Text);
        canvas.WidthScale = 2;
        Assert.Equal(width / 2, canvas.Measure("SCHUMACHER", theme.Text), 3);
    }

    [Fact]
    public void Quali98_keeps_slow_finish_reference_then_shows_position_and_centered_cyan_delta()
    {
        using var gfx = DeviceResources.CreateOffscreen(1920, 300);
        var settings = new WidgetSettings { Id = "qualiboard" };
        using var canvas = new ThemeCanvas(gfx, ThemeOverrides.Apply(Themes.F1_1998, settings));
        canvas.ConfigureTypography(settings);
        var widget = new QualiLapWidget(); widget.UseTheme(Themes.F1_1998); widget.Configure(settings);
        var model = LayoutPreview.ForWidget("qualiboard") with { Now = 100 };
        var q = model.QualiLap! with { CarIndex = model.Session!.PlayerCar!.Index, Lap = 2, Sector = 2,
            InPit = false, OutLap = false, Elapsed = 85, LeaderBestLap = 72, LastSplit = null, LastResult = null };
        byte[] Render(QualiLapState lap)
        {
            gfx.BeginFrame(); canvas.Begin(); widget.Draw(canvas, model with { QualiLap = lap }); canvas.End(); gfx.EndFrame();
            return gfx.ReadPixelsBgra();
        }
        var slow = Render(q);
        if (Environment.GetEnvironmentVariable("AMS2_REVIEW_FRAMES") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            PngWriter.SaveFromPremultipliedBgra(Path.Combine(directory, "qualy98-slower-reference.png"), slow, 1920, 300, (45, 50, 55));
        }
        var missing = Render(q with { LeaderBestLap = null });
        bool RegionDiff(byte[] a, byte[] b, int x0, int y0, int w, int h)
        {
            for (int y = y0; y < y0 + h; y++)
                if (!a.AsSpan((y * 1920 + x0) * 4, w * 4).SequenceEqual(b.AsSpan((y * 1920 + x0) * 4, w * 4))) return true;
            return false;
        }
        Assert.True(RegionDiff(slow, missing, 150, 20, 640, 155));
        var result = Render(q with { Elapsed = null, LastResult = new(2, 85, 9, 13, 2, false, false, [], 99.7, true) });
        if (Environment.GetEnvironmentVariable("AMS2_REVIEW_FRAMES") is { Length: > 0 } resultDirectory)
            PngWriter.SaveFromPremultipliedBgra(Path.Combine(resultDirectory, "qualy98-result.png"), result, 1920, 300, (45, 50, 55));
        int cyan = 0;
        for (int y = 100; y < 172; y++)
        for (int x = 700; x < 1220; x++)
        {
            int i = (y * 1920 + x) * 4;
            if (result[i] > 140 && result[i + 1] > 130 && result[i + 2] < 90) cyan++;
        }
        Assert.True(cyan > 100, "The center must contain the cyan delta.");
        Assert.True(RegionDiff(slow, result, 300, 35, 160, 145));
        Assert.Equal((1920f, 300f), widget.DesignSize);
    }
}
