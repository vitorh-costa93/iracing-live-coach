using Ams2.Shared.Profiles;

namespace Ams2.Shared.Tests;

public class Broadcast93ProfileTests
{
    [Fact]
    public void Theme_exposes_compact_boards_without_full_screen_classification()
    {
        Assert.True(ThemeCatalog.Find("f1-1993")!.Available);
        var profile = ProfileFactory.CreateDefault("1993", "f1-1993").Normalized();
        Assert.True(profile.Get("board")!.Visible);
        Assert.True(profile.Get("qualiboard")!.Visible);
        Assert.Null(profile.Get("qualitower"));
        Assert.Null(profile.Get("qualiresult"));
        Assert.Null(profile.Get("standings"));
        Assert.Null(profile.Get("pittimer"));
        Assert.False(profile.Get("drivercaption")!.Visible);
        Assert.Equal(new[] { SessionIds.Qualify }, WidgetCatalog.Find("qualiboard", "f1-1993")!.Sessions);
    }

    [Fact]
    public void Gap_marker_and_hold_are_bounded_and_scoped_to_1993()
    {
        var settings = new WidgetSettings { Id = "board", Options = new()
        { ["gapPointPercent"] = "110", ["gapHoldSeconds"] = "0" } };
        var normalized = settings.Normalized("f1-1993");
        Assert.Equal("99.9", normalized.Option("gapPointPercent"));
        Assert.Equal("3", normalized.Option("gapHoldSeconds"));
        Assert.Null(settings.Normalized("f1-1998").Options);
        Assert.Equal("0", WidgetCatalog.OptionsFor("f1-1993", "board").Single(o => o.Id == "gapPointPercent").Default);
    }
}
