using Ams2.Shared.Profiles;

namespace Ams2.Shared.Tests;

public class GpxSatProfileTests
{
    [Fact]
    public void Gpxsat_exists_in_the_four_gpx_themes_with_full_screen_size()
    {
        foreach (var t in new[] { "f1-1998", "f1-1999-digital", "f1-2004", "f1-2012" })
        {
            Assert.NotNull(WidgetCatalog.Find("gpxsat", t));
            Assert.Equal((1920f, 1080f), WidgetLayout.DesignSizes[t]["gpxsat"]);
            Assert.Contains(WidgetCatalog.OptionsFor(t, "gpxsat"), o => o.Id == "fadingSpeed" && o.Default == "10");
        }
        Assert.Null(WidgetCatalog.Find("gpxsat", "f1-2018"));
        Assert.Null(WidgetCatalog.Find("gpxsat", "f1-1993"));
        Assert.Equal("F1 1999 Digital", ThemeCatalog.Find("f1-1999-digital")!.DisplayName);
    }

    [Fact]
    public void Default_profiles_use_the_four_implemented_modes()
    {
        var d04 = ProfileFactory.CreateDefault("x", "f1-2004");
        Assert.True(d04.Get("gpxsat")!.Visible);
        foreach (var id in WidgetLayout.GpxSatReplaces["f1-2004"]) Assert.False(d04.Get(id)!.Visible, id);
        foreach (var id in new[] { "gpxgauges", "inputgraph", "radar", "fuel", "tyres", "weather", "qualiresult" }) Assert.True(d04.Get(id)!.Visible, id);

        var dig = ProfileFactory.CreateDefault("x", "f1-1999-digital");
        Assert.True(dig.Get("gpxsat")!.Visible);
        Assert.False(dig.Get("board")!.Visible);
        Assert.True(dig.Get("inputs")!.Visible && dig.Get("radar")!.Visible);
        Assert.Equal(["team", "tyre"], dig.Get("qualiboard")!.Columns);

        foreach (var t in new[] { "f1-1998", "f1-2012" })
            Assert.True(ProfileFactory.CreateDefault("x", t).Get("gpxsat")!.Visible, t);
    }

    [Theory]
    [InlineData("f1-1998")]
    [InlineData("f1-1999-digital")]
    [InlineData("f1-2004")]
    [InlineData("f1-2012")]
    public void Schema8_keeps_graph_and_radar_and_preserves_gpx_settings(string theme)
    {
        var profile=ProfileFactory.CreateDefault("Saved",theme);
        profile=profile with { SchemaVersion=8, Widgets=profile.Widgets.Select(w=>w.Id switch
        {
            "gpxsat" => w with { Options=new() { ["splitSeconds"]="11", ["originalFonts"]="false" } },
            "radar" => w with { X=123,Y=456,Scale=.7f,Visible=false,Sessions=["qualify"] },
            "inputgraph" => w with { X=333,Y=444,Scale=.8f,ColumnWidths=new() { ["graph"]=222 } },
            _ => w,
        }).ToList() };
        var migrated=profile.Normalized();
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(profile.Get("radar")!.Normalized(theme)),System.Text.Json.JsonSerializer.Serialize(migrated.Get("radar")));
        if (profile.Get("inputgraph") is { } graph) Assert.Equal(System.Text.Json.JsonSerializer.Serialize(graph.Normalized(theme)),System.Text.Json.JsonSerializer.Serialize(migrated.Get("inputgraph")));
        Assert.Equal("11",migrated.Get("gpxsat")!.Option("splitSeconds"));
        Assert.Equal("false",migrated.Get("gpxsat")!.Option("originalFonts"));
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(migrated),System.Text.Json.JsonSerializer.Serialize(migrated.Normalized()));
    }

    [Fact]
    public void Schema8_migration_turns_gpxsat_on_once_for_saved_2004_profiles()
    {
        var old = new Profile
        {
            SchemaVersion = 7, ThemeId = "f1-2004",
            Widgets = [new() { Id = "board", X = 10, Y = 20 }, new() { Id = "fuel", X = 30, Y = 40 }],
        };
        var m = old.Normalized();
        Assert.False(m.Get("board")!.Visible);
        Assert.Equal((10, 20), (m.Get("board")!.X, m.Get("board")!.Y));   // geometria preservada
        Assert.True(m.Get("fuel")!.Visible);
        Assert.True(m.Get("gpxsat")!.Visible);

        // Depois da migracao a escolha do usuario vale: religar o board nao e desfeito.
        var again = (m with { Widgets = m.Widgets.Select(w => w.Id == "board" ? w with { Visible = true } : w).ToList() }).Normalized();
        Assert.True(again.Get("board")!.Visible);
    }
}
