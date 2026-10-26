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
        foreach (var id in new[] { "inputs", "inputgraph", "radar", "fuel", "tyres", "weather", "qualiresult" }) Assert.True(d04.Get(id)!.Visible, id);
        Assert.False(d04.Get("gpxgauges")!.Visible);   // 2004: velocimetro dos prints; GPxCInfo opcional
        Assert.True(ProfileFactory.CreateDefault("x", "f1-2012").Get("gpxgauges")!.Visible);
        Assert.False(ProfileFactory.CreateDefault("x", "f1-2012").Get("inputs")!.Visible);

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

    static Profile Saved2004(int schema, bool inputs, bool? gauges)
    {
        var p = ProfileFactory.CreateDefault("Saved", "f1-2004");
        var list = p.Widgets.Where(w => gauges is not null || w.Id != "gpxgauges")
            .Select(w => w.Id switch { "inputs" => w with { Visible = inputs, X = 1111 }, "gpxgauges" => w with { Visible = gauges!.Value }, _ => w }).ToList();
        return p with { SchemaVersion = schema, Widgets = list };
    }

    [Theory]
    [InlineData(9, false, true, true, false)]    // par da v9 (GPxCInfo no lugar do velocimetro): troca
    [InlineData(8, false, null, true, false)]    // v8 sem gpxgauges salvo: troca
    [InlineData(9, true, true, true, true)]      // usuario religou o inputs: mantem os dois
    [InlineData(9, false, false, false, false)]  // usuario desligou os dois: mantem
    [InlineData(7, false, null, false, false)]   // antes da GPxSAT: inputs e escolha do usuario; gpxgauges nasce desligado
    public void Schema10_restores_print_speedo_on_2004_only_from_the_stock_pair(int schema, bool inputs, bool? gauges, bool expInputs, bool expGauges)
    {
        var m = Saved2004(schema, inputs, gauges).Normalized();
        Assert.Equal(Profile.CurrentSchemaVersion, m.SchemaVersion);
        Assert.Equal(expInputs, m.Get("inputs")!.Visible);
        Assert.Equal(expGauges, m.Get("gpxgauges")!.Visible);
        Assert.Equal(1111, m.Get("inputs")!.X);   // geometria preservada
        // Depois da migracao a escolha vale: religar o GPxCInfo nao e desfeito.
        var again = (m with { Widgets = m.Widgets.Select(w => w.Id == "gpxgauges" ? w with { Visible = true } : w.Id == "inputs" ? w with { Visible = false } : w).ToList() }).Normalized();
        Assert.True(again.Get("gpxgauges")!.Visible);
        Assert.False(again.Get("inputs")!.Visible);
    }

    [Theory]
    [InlineData("f1-2012")]
    [InlineData("f1-1998")]
    [InlineData("f1-1999-digital")]
    public void Schema10_does_not_touch_speedos_of_other_gpx_themes(string theme)
    {
        var p = ProfileFactory.CreateDefault("Saved", theme);
        var m = (p with { SchemaVersion = 9 }).Normalized();
        Assert.Equal(p.Get("inputs")!.Visible, m.Get("inputs")!.Visible);
        Assert.Equal(p.Get("gpxgauges")!.Visible, m.Get("gpxgauges")!.Visible);
    }
}
