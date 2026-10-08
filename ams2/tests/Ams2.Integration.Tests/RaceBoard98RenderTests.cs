using Ams2.Core.Calc;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Gfx;
using Ams2.OverlayHost.Theme;
using Ams2.OverlayHost.Widgets;
using Ams2.Shared.Profiles;

namespace Ams2.Integration.Tests;

public class RaceBoard98RenderTests
{
    static readonly BoardDriver Player = new(0, 2, "Rubens Barrichello", "Barrichello", "BAR", "Ferrari", "B", true);
    static readonly BoardDriver Neighbor = new(1, 1, "Michael Schumacher", "M Schumacher", "MSC", "Ferrari", "B", false);
    static OverlayModel Model(BoardSectorGap? current, BoardSectorGap? legacy = null)
        => LayoutPreview.ForWidget("board") with { Now = 5,
            Board = new BoardState(BoardMode.SectorGap, 1, 5, true, 1, 10, 5, 1, null, legacy, null, null)
                { SectorGap98 = current } };

    static byte[] Render(BoardWidget board, OverlayModel model, string? artifact = null)
    {
        using var gfx = DeviceResources.CreateOffscreen(1920, 300);
        using var canvas = new ThemeCanvas(gfx, Themes.F1_1998) { Opacity = .7f };
        gfx.BeginFrame(); canvas.Begin(); board.Draw(canvas, model); canvas.End(); gfx.EndFrame();
        Assert.Equal(.7f, canvas.Opacity);
        var pixels = gfx.ReadPixelsBgra();
        if (artifact is not null && Environment.GetEnvironmentVariable("AMS2_REVIEW_FRAMES") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            PngWriter.SaveFromPremultipliedBgra(Path.Combine(directory, artifact + ".png"), pixels, 1920, 300, (45, 50, 55));
        }
        return pixels;
    }

    [Fact]
    public void Legacy_sector_cannot_start_a_1998_counter_before_the_selected_pair_crosses()
    {
        var legacy = new BoardSectorGap(2, Player, Neighbor, true, 8, false, "8.0", 1, 10);
        Assert.Equal(Broadcast98RaceBoard.Mode.Caption, Broadcast98RaceBoard.Select(Model(null, legacy)));
        Assert.Equal(Broadcast98RaceBoard.Mode.Sector, Broadcast98RaceBoard.Select(Model(legacy)));
    }

    [Fact]
    public void Sector_freezes_in_milliseconds_and_keeps_the_same_pair_on_the_full_width_board()
    {
        var board = new BoardWidget(); board.UseTheme(Themes.F1_1998);
        board.Configure(new WidgetSettings { Id = "board", Columns = ["tyre"] });
        var live = new BoardSectorGap(2, Player, Neighbor, true, 1.2, false, "1.2", 1, 10);
        var first = Render(board, Model(live), "sector-live");
        var frozen = Render(board, Model(live with { IsSplit = true }), "sector-frozen");
        Assert.NotEqual(first, frozen);
        // Left driver plate does not change when only the centre counter freezes.
        for (int y = 135; y < 260; y++)
            Assert.Equal(first[(y * 1920 * 4)..((y * 1920 + 700) * 4)], frozen[(y * 1920 * 4)..((y * 1920 + 700) * 4)]);
        Assert.Equal((1920f, 300f), board.DesignSize);
    }
}
