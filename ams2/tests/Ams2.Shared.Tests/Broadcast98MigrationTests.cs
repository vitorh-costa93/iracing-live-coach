using Ams2.Shared.Profiles;

namespace Ams2.Shared.Tests;

public class Broadcast98MigrationTests
{
    [Fact]
    public void Stock_footer_migrates_and_keeps_customization()
    {
        var p = new Profile { SchemaVersion = 3, ThemeId = "f1-1998", Widgets =
        [new() { Id = "board", X = 660, Y = 872, Scale = 1, Font = "Arial", TextScale = 1.5f },
         new() { Id = "qualitower", X = 32, Y = 24, Scale = 1, Visible = true },
         new() { Id = "qualilap", X = 640, Y = 900, Scale = 1, Visible = true, Font = "Arial", TextScale = 1.2f,
             Options = new() { ["compareTo"] = "personal", ["showSpeed"] = "false" } }] }.Normalized();
        var board = p.Get("board")!;
        Assert.Equal((0, 780, 1.5f), (board.X, board.Y, board.Scale));
        Assert.Equal("Arial", board.Font); Assert.Null(board.TextScale);
        Assert.Null(p.Get("qualitower")); Assert.Null(p.Get("qualilap"));
        Assert.True(p.Get("qualiboard")!.Visible);
        Assert.Equal("Arial", p.Get("qualiboard")!.Font);
        Assert.Null(p.Get("qualiboard")!.TextScale);
        Assert.Equal(1.2f, p.Get("qualiboard")!.Scale);
        Assert.Equal("personal", p.Get("qualiboard")!.Option("compareTo"));
        Assert.Equal("false", p.Get("qualiboard")!.Option("showSpeed"));
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(p), System.Text.Json.JsonSerializer.Serialize(p.Normalized()));
    }

    [Fact]
    public void Custom_placement_is_preserved_without_adding_a_duplicate_board()
    {
        var p = new Profile { SchemaVersion = 3, ThemeId = "f1-1998", Widgets =
        [new() { Id = "board", X = 120, Y = 150, Scale = .7f, Font = "Arial" },
         new() { Id = "qualitower", X = 100, Y = 100, Scale = .8f, Visible = true }] }.Normalized();
        var board = p.Get("board")!;
        Assert.Equal((120, 150, .7f, "Arial"), (board.X, board.Y, board.Scale, board.Font));
        Assert.Null(p.Get("qualitower"));
        Assert.False(p.Get("qualiboard")!.Visible);
        Assert.Null(ProfileFactory.CreateDefault("x", "f1-2018").Get("qualiboard"));
    }

    [Fact]
    public void Lower_boards_share_the_bottom_area_and_only_one_is_visible_per_session()
    {
        var p = ProfileFactory.CreateDefault("x", "f1-1998");
        foreach (var id in new[] { "board", "qualiboard" })
        {
            Assert.Equal((1920f, 300f), WidgetLayout.DesignSizes["f1-1998"][id]);
            Assert.Equal((0, 780), (p.Get(id)!.X, p.Get(id)!.Y));
        }
        foreach (var session in new[] { "race", "qualify" })
            Assert.Single(p.Widgets.Where(w => w.Visible && w.ShowsIn(session) && w.Id is "board" or "qualiboard"));
    }
}
