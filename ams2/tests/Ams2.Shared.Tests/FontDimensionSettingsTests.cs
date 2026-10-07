using System.Text.Json;
using Ams2.Shared.Profiles;

namespace Ams2.Shared.Tests;

public sealed class FontDimensionSettingsTests
{
    [Fact]
    public void Independent_dimensions_normalize_invalid_default_and_out_of_range_values()
    {
        var s = new WidgetSettings { Id = "standings", Scale = .8f, TextScale = .4f, WidthScale = 4, HeightScale = .1f }.Normalized();
        Assert.Equal((2.4f, .2f), (s.ScaleX, s.ScaleY));
        Assert.Equal(.4f, s.TextScale);
        var invalid = (s with { WidthScale = float.NaN, HeightScale = 1, TextScale = float.PositiveInfinity }).Normalized();
        Assert.Null(invalid.WidthScale); Assert.Null(invalid.HeightScale); Assert.Null(invalid.TextScale);
    }

    [Fact]
    public void Font_elements_are_canonical_clamped_and_empty_overrides_are_removed()
    {
        var s = new WidgetSettings { Id = "standings", ElementFonts = new()
        {
            ["NAME"] = new() { Scale = .1f, Weight = 640 },
            ["time"] = new() { Scale = 4, Weight = 5000 },
            ["gap"] = new() { Scale = 1, Weight = 0 },
            ["label"] = new() { Scale = float.NaN, Weight = -10 },
            ["unknown"] = new() { Scale = 1.5f }
        } }.Normalized();
        Assert.Equal(2, s.ElementFonts!.Count);
        Assert.Equal(new ElementFontSettings { Scale = .4f, Weight = 600 }, s.ElementFonts["name"]);
        Assert.Equal(new ElementFontSettings { Scale = 2, Weight = 900 }, s.ElementFonts["time"]);
    }

    [Fact]
    public void Patch_merge_keeps_dimensions_and_atomically_replaces_or_resets_element_fonts()
    {
        var first = new WidgetPatch { WidthScale = 1.5f, ElementFonts = new() { ["name"] = new() { Scale = .8f } } };
        var last = new WidgetPatch { HeightScale = .7f, ElementFonts = new() { ["time"] = new() { Weight = 700 } } };
        var merged = WidgetPatch.Merge(first, last).ApplyTo(new WidgetSettings { Id = "standings" });
        Assert.Equal((1.5f, .7f), (merged.WidthScale, merged.HeightScale));
        Assert.Single(merged.ElementFonts!); Assert.Equal(700, merged.ElementFonts!["time"].Weight);
        var reset = WidgetPatch.Merge(first, new WidgetPatch { WidthScale = 1, HeightScale = 1, ElementFonts = [] }).ApplyTo(merged);
        Assert.Null(reset.WidthScale); Assert.Null(reset.HeightScale); Assert.Null(reset.ElementFonts);
    }

    [Theory]
    [InlineData(.8f, 1.25f)]
    [InlineData(.5f, .6f)]
    [InlineData(3f, 2f)]
    public void Legacy_text_window_scale_migrates_once_without_changing_effective_dimensions(float scale, float text)
    {
        var p = new Profile { SchemaVersion = 5, ThemeId = "f1-2004", Widgets = [new() { Id = "inputs", Scale = scale, TextScale = text }] }.Normalized();
        var w = p.Get("inputs")!;
        Assert.Equal(scale * text, w.Scale * (w.TextScale ?? 1f), 4);
        Assert.Equal(scale * text, w.ScaleX, 4); Assert.Equal(scale * text, w.ScaleY, 4);
        Assert.Equal(JsonSerializer.Serialize(p), JsonSerializer.Serialize(p.Normalized()));
    }

    [Fact]
    public void New_settings_roundtrip_without_legacy_conversion()
    {
        using var t = new TempStore();
        var p = ProfileFactory.CreateDefault("Fonts", "f1-2018");
        var widget = p.Get("standings")! with { WidthScale = 1.4f, HeightScale = .8f, TextScale = .6f,
            ElementFonts = new() { ["name"] = new() { Scale = .7f, Weight = 500 } } };
        t.Store.Save(p.WithWidget(widget));
        var loaded = t.Store.Load("f1-2018", "Fonts")!.Get("standings")!;
        Assert.Equal((widget.Scale, .6f, 1.4f, .8f), (loaded.Scale, loaded.TextScale, loaded.WidthScale, loaded.HeightScale));
        Assert.Equal(widget.ElementFonts!["name"], loaded.ElementFonts!["name"]);
    }
}
