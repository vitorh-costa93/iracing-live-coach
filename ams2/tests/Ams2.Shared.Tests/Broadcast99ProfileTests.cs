using System.Text.Json;
using Ams2.Shared.Profiles;

namespace Ams2.Shared.Tests;

public sealed class Broadcast99ProfileTests
{
    static readonly string[] Supported = ["board", "qualiboard", "inputs", "radar", "gpxsat", "gpxgauges"];

    [Fact]
    public void Theme99_keeps_legacy_id_and_only_four_requested_widgets()
    {
        Assert.Equal("F1 1999", ThemeCatalog.Find("f1-1998")!.DisplayName);
        var defs = WidgetCatalog.ForTheme("f1-1998").ToArray();
        Assert.Equal(Supported.Order(), defs.Select(d => d.Id).Order());
        var profile = ProfileFactory.CreateDefault("1999", "f1-1998").Normalized();
        Assert.Equal(Supported.Order(), profile.Widgets.Select(w => w.Id).Order());
        // GPxSAT substitui os boards antigos; eles continuam disponíveis sem desenhar em duplicidade.
        Assert.All(profile.Widgets, widget => Assert.Equal(widget.Id is not ("board" or "qualiboard"), widget.Visible));
        foreach (var id in new[] { "board", "qualiboard" })
        {
            Assert.Equal(["team", "tyre"], profile.Get(id)!.Columns);
            Assert.Equal((1920f, 300f), WidgetLayout.DesignSizes["f1-1998"][id]);
            Assert.Equal((0, 780), (profile.Get(id)!.X, profile.Get(id)!.Y));
        }
    }

    [Fact]
    public void Default_sessions_show_race_board_in_practice_race_and_quali_board_only_in_qualify()
    {
        var profile = ProfileFactory.CreateDefault("1999", "f1-1998");
        Assert.Equal(["practice", "race"], profile.Get("board")!.EffectiveSessions);
        Assert.Equal(["qualify"], profile.Get("qualiboard")!.EffectiveSessions);
        Assert.Equal(SessionIds.All, profile.Get("inputs")!.EffectiveSessions);
        Assert.Equal(["practice", "race"], profile.Get("radar")!.EffectiveSessions);
        foreach (var session in SessionIds.All)
            Assert.Single(profile.Widgets.Where(w => w.Visible && w.ShowsIn(session) && w.Id == "gpxsat"));
    }

    [Fact]
    public void Schema6_migration_preserves_geometry_fonts_options_and_removes_unsupported_tables()
    {
        var original = new Profile { SchemaVersion = 6, ThemeId = "f1-1998", Name = "Custom", Widgets =
        [
            new() { Id = "board", X = 111, Y = 222, Scale = .8f, WidthScale = 1.2f, HeightScale = .9f,
                Font = "Arial", FontWeight = 600, TextScale = 1.1f, Columns = ["tyre"],
                ElementFonts = new() { ["name"] = new() { Scale = 1.2f, Weight = 500 } } },
            new() { Id = "qualiboard", X = 321, Y = 654, Scale = 1.3f, Font = "Tahoma", Columns = ["team", "tyre"],
                Options = new() { ["compareTo"] = "personal", ["showSpeed"] = "false", ["showFor"] = "9" } },
            new() { Id = "inputs", X = 12, Y = 34, Scale = .7f },
            new() { Id = "radar", X = 55, Y = 66, Scale = .9f, RadarRange = 22, RadarSensitivity = 4 },
            new() { Id = "standings", X = 999 }, new() { Id = "relative" },
            new() { Id = "qualitower" }, new() { Id = "qualilap" }, new() { Id = "qualiresult" }
        ] };
        var migrated = original.Normalized();
        Assert.Equal(Profile.CurrentSchemaVersion, migrated.SchemaVersion);
        Assert.Equal(Supported.Order(), migrated.Widgets.Select(w => w.Id).Order());
        var board = migrated.Get("board")!;
        Assert.Equal((111, 222, .8f, 1.2f, .9f, "Arial", 600, 1.1f),
            (board.X, board.Y, board.Scale, board.WidthScale, board.HeightScale, board.Font, board.FontWeight, board.TextScale));
        Assert.Equal(original.Get("board")!.ElementFonts, board.ElementFonts);
        Assert.Equal(["team", "tyre"], board.Columns);
        var quali = migrated.Get("qualiboard")!;
        Assert.Equal((321, 654, 1.3f, "Tahoma"), (quali.X, quali.Y, quali.Scale, quali.Font));
        Assert.Equal(original.Get("qualiboard")!.Options, quali.Options);
        var radar = migrated.Get("radar")!;
        Assert.Equal((55, 66, .9f, 22, 4), (radar.X, radar.Y, radar.Scale, radar.RadarRange, radar.RadarSensitivity));
        Assert.Equal(["practice", "race"], radar.EffectiveSessions);
        Assert.Equal(JsonSerializer.Serialize(migrated), JsonSerializer.Serialize(migrated.Normalized()));
    }

    [Fact]
    public void Schema6_migration_preserves_explicit_radar_sessions_and_custom_board_columns()
    {
        var profile = new Profile { SchemaVersion = 6, ThemeId = "f1-1998", Widgets =
            [new() { Id = "radar", Sessions = ["qualify"] }, new() { Id = "board", Columns = ["team"] }] }.Normalized();
        Assert.Equal(["qualify"], profile.Get("radar")!.EffectiveSessions);
        Assert.Equal(["team"], profile.Get("board")!.Columns);
    }

    [Theory]
    [InlineData("f1-1993")]
    [InlineData("f1-2004")]
    [InlineData("f1-2012")]
    [InlineData("f1-2018")]
    public void Schema7_migration_does_not_change_other_themes(string theme)
    {
        var current = ProfileFactory.CreateDefault("Other", theme).Normalized();
        var legacy = current with { SchemaVersion = 6 };
        Assert.Equal(JsonSerializer.Serialize(current), JsonSerializer.Serialize(legacy.Normalized()));
        Assert.NotNull(current.Get("standings"));
        Assert.NotNull(current.Get("qualitower"));
    }
}
