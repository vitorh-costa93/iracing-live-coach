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
        => LayoutPreview.ForWidget("board") with { Now = 5, PlayerDriving = true,
            Board = new BoardState(BoardMode.SectorGap, 1, 5, true, 1, 10, 5, 1, null, legacy, null, null)
                { SectorGap98 = current, Race99 = new(current is { IsSplit: true } ? Broadcast99RaceMode.Gap : Broadcast99RaceMode.Caption,
                    3, 1, [], current is { IsSplit: true } ? current : null, null, null, null) } };

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
    public void Legacy_sector_and_live_counter_cannot_start_1999_comparison()
    {
        var legacy = new BoardSectorGap(2, Player, Neighbor, true, 8, false, "8.0", 1, 10);
        Assert.Equal(Broadcast98RaceBoard.Mode.Caption, Broadcast98RaceBoard.Select(Model(null, legacy)));
        Assert.Equal(Broadcast98RaceBoard.Mode.Caption, Broadcast98RaceBoard.Select(Model(legacy)));
        Assert.Equal(Broadcast98RaceBoard.Mode.Sector, Broadcast98RaceBoard.Select(Model(legacy with { IsSplit = true })));
    }

    [Fact]
    public void Sector_freezes_in_milliseconds_and_keeps_the_same_pair_on_the_full_width_board()
    {
        var board = new BoardWidget(); board.UseTheme(Themes.F1_1998);
        board.Configure(new WidgetSettings { Id = "board", Columns = ["tyre"] });
        var live = new BoardSectorGap(3, Player, Neighbor, true, 1.2, true, "1.2", 1, 10) { PreviousGapSeconds = 11.407 };
        var first = Render(board, Model(live), "1999-gap");
        var frozen = Render(board, Model(live with { GapSeconds = .7 }), "1999-gap-updated");
        Assert.NotEqual(first, frozen);
        // Left driver plate does not change when only the centre counter freezes.
        for (int y = 135; y < 260; y++)
            Assert.Equal(first[(y * 1920 * 4)..((y * 1920 + 700) * 4)], frozen[(y * 1920 * 4)..((y * 1920 + 700) * 4)]);
        Assert.Equal((1920f, 300f), board.DesignSize);
    }

    [Theory]
    [InlineData(Broadcast99RaceMode.Caption)]
    [InlineData(Broadcast99RaceMode.Table)]
    [InlineData(Broadcast99RaceMode.Pit)]
    [InlineData(Broadcast99RaceMode.Fastest)]
    [InlineData(Broadcast99RaceMode.Remaining)]
    public void All_1999_modes_render_one_full_width_panel(Broadcast99RaceMode mode)
    {
        var board = new BoardWidget(); board.UseTheme(Themes.F1_1998);
        board.Configure(new WidgetSettings { Id = "board", Columns = ["tyre", "team"] });
        var model = Model(null);
        var car = model.Session!.PlayerCar!;
        var rows = Enumerable.Range(1, 8).Select(i => new Broadcast99RaceRow(Player with { Position = i, CarIndex = i }, i == 1 ? 0 : i * 1.21, 0)).ToArray();
        var state = new Broadcast99RaceState(mode, 15, 1, rows, null, 39, Neighbor, new FastestLapEvent(car with { LapsCompleted = 7 }, 92.319, 1));
        model = model with { Board = model.Board! with { Race99 = state } };
        var pixels = Render(board, model, "1999-" + mode.ToString().ToLowerInvariant());
        Assert.Contains(pixels, b => b > 0);
    }

    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 4)]
    [InlineData(true, 5)]
    public void Hidden_player_pause_and_replay_render_nothing(bool driving, uint gameState)
    {
        var model = Model(null);
        model = model with { PlayerDriving = driving, Session = model.Session! with { GameState = gameState } };
        Assert.Equal(Broadcast98RaceBoard.Mode.None, Broadcast98RaceBoard.Select(model));
    }
}
