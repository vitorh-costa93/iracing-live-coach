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
        Assert.True(profile.Get("standings")!.Visible);
        Assert.True(profile.Get("qualitower")!.Visible);
        Assert.Null(profile.Get("qualiresult"));
        Assert.Null(profile.Get("pittimer"));
        Assert.Null(profile.Get("drivercaption"));
        Assert.Equal(new[] { SessionIds.Qualify }, WidgetCatalog.Find("qualiboard", "f1-1993")!.Sessions);
        Assert.Equal(new[] { SessionIds.Qualify }, WidgetCatalog.Find("qualitower", "f1-1993")!.Sessions);
        Assert.Equal(new[] { SessionIds.Practice, SessionIds.Race }, WidgetCatalog.Find("standings", "f1-1993")!.Sessions);
    }

    [Fact]
    public void Theme_has_only_the_six_requested_widgets_with_1993_names()
    {
        var defs = WidgetCatalog.ForTheme("f1-1993").ToList();
        Assert.Equal(new[] { "board", "qualiboard", "standings", "qualitower", "radar", "inputs" }.Order(), defs.Select(d => d.Id).Order());
        foreach (var id in new[] { "drivercaption", "fuel", "tyres", "weather" })
        {
            Assert.DoesNotContain(id, defs.Select(d => d.Id));
            Assert.True(WidgetCatalog.Find(id, "f1-1998") is not null);   // continuam nos outros temas
        }
        Assert.StartsWith("Race Board", defs.Single(d => d.Id == "board").DisplayName);
        Assert.StartsWith("Qualy Board", defs.Single(d => d.Id == "qualiboard").DisplayName);
        Assert.Equal("Race Standings", defs.Single(d => d.Id == "standings").DisplayName);
        Assert.Equal("Qualy Standings", defs.Single(d => d.Id == "qualitower").DisplayName);
        var profile = ProfileFactory.CreateDefault("1993", "f1-1993").Normalized();
        Assert.Equal(new[] { "graph" }, profile.Get("inputs")!.Columns);
        Assert.Equal(new[] { "sponsor" }, profile.Get("standings")!.Columns);
        Assert.False(profile.Get("standings")!.ColumnVisible("time"));
    }

    [Fact]
    public void Standings_rows_option_is_numeric_bounded_and_scoped_to_1993()
    {
        foreach (var id in new[] { "standings", "qualitower" })
        {
            var rows = WidgetCatalog.OptionsFor("f1-1993", id).Single(o => o.Id == "rows");
            Assert.Equal(OptionKind.Number, rows.Kind);
            Assert.Equal(("6", 3.0, 20.0), (rows.Default, rows.Min, rows.Max));
            Assert.Equal("20", new WidgetSettings { Id = id, Options = new() { ["rows"] = "99" } }.Normalized("f1-1993").Option("rows"));
            Assert.Equal("3", new WidgetSettings { Id = id, Options = new() { ["rows"] = "1" } }.Normalized("f1-1993").Option("rows"));
            Assert.Null(new WidgetSettings { Id = id, Options = new() { ["rows"] = "6" } }.Normalized("f1-1993").Options);
        }
        Assert.Empty(WidgetCatalog.OptionsFor("f1-1998", "standings"));
    }

    [Fact]
    public void Car_number_comes_from_the_livery_name_and_ambiguous_drivers_stay_unknown()
    {
        Assert.Equal("1", Ams2.Shared.Liveries.DriverCountryResolver.NumberOfLivery("1986 McLaren #1 - A. Prost"));
        Assert.Equal("", Ams2.Shared.Liveries.DriverCountryResolver.NumberOfLivery("Sem numero"));
        var resolver = new Ams2.Shared.Liveries.DriverCountryResolver(
        [
            new("1986 McLaren #1 - A. Prost", "Alain Prost", "FRA", "McLaren", "F-Classic_Gen1"),
            new("1988 Lotus #12 - A. Senna", "Ayrton Senna", "BRA", "Lotus", "F-Classic_Gen2"),
            new("1990 Ferrari #1 - A. Senna", "Ayrton Senna", "BRA", "Ferrari", "F-Classic_Gen2"),
        ]);
        Assert.Equal("1", resolver.ResolveNumber("Alain PROST"));
        Assert.Equal("", resolver.ResolveNumber("Ayrton Senna"));   // numeros diferentes = ambiguo
        Assert.Equal("", resolver.ResolveNumber("Desconhecido"));
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
