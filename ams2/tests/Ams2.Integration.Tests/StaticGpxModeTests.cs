using Ams2.Core;
using Ams2.Core.Calc;
using Ams2.Core.Calc.GpxSat;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Widgets.GpxSat;
using Ams2.OverlayHost.Widgets.GpxSat.Modes;
using Ams2.Shared.Profiles;

namespace Ams2.Integration.Tests;

public class StaticGpxModeTests
{
    static GpxContext Context(IGpxMode mode, GpxOptions? options = null, bool race = true, bool pit = false, QualiLapState? lap = null)
    {
        var cars = Enumerable.Range(1, 10).Select(p => new CarSnapshot(p, "Driver " + p, "Car", "F1", p, p, 3, 4, 1000, 1, 81 + p / 10d, 82, 60,
            pit && p == 3 ? PitState.InPit : PitState.None, RaceState.Racing, false, p == 3, "M", CarNumber: p.ToString())).ToList();
        var player = new PlayerSnapshot(3, new(0, 0, 0, 0, 0, 0, 0, 0), 3, 7000, 10000, 60, 50, 100, [], .5);
        var session = new SessionSnapshot(1, 1, true, race ? SessionKind.Race : SessionKind.Qualify, "Track", "", 5000, race ? 20 : 0, 600, 0, new(20, 30, 0, 0, 0, 0, 0, 0), cars, player);
        var model = OverlayModel.Empty with { Connected = true, Now = 1, Session = session, Quali = QualiTable.Build(session), QualiLap = lap, PlayerDriving = true };
        return new() { Model = model, Session = session, Options = options ?? new(), Settings = new(), G = new(mode.Spec.FH), Sky = new(), Split = new() };
    }
    [Fact]
    public void Original_fonts_and_pagination_are_mode_specific()
    {
        var classic = new ClassicMode(); var digital = new DigitalMode();
        Assert.True(classic.Implemented); Assert.True(digital.Implemented);
        Assert.Equal("Mañana", classic.Spec.Normal.Families[0]); Assert.Equal("Futura-Condensed-Bold", digital.Spec.Normal.Families[0]);
        Assert.Equal(8, classic.CarsPerPage); Assert.Equal(6, digital.CarsPerPage);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Every_preview_is_instant_and_has_finite_geometry(bool classic)
    {
        IGpxMode mode = classic ? new ClassicMode() : new DigitalMode();
        foreach (string preview in new[] { "time", "fl", "out", "pit", "pitt", "dt", "win", "ride", "split", "splitgreen", "card", "gapa", "gapb", "trap", "str", "stq", "stp" })
        {
            var screen = Assert.Single(mode.Dispatch(Context(mode, new() { PreviewScreen = preview })));
            Assert.Equal(GpxAnimKind.Instant, screen.Anim); Assert.NotEmpty(screen.Boxes);
            Assert.All(screen.Boxes, b => { Assert.True(float.IsFinite(b.Rect.L)); Assert.True(b.Rect.W >= 0); Assert.True(b.Rect.H > 0); });
            Assert.DoesNotContain(screen.Boxes, b => b.Text?.Contains("GPxSAT") == true);
        }
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Disabled_mode_is_empty_and_pit_has_priority_over_clock_and_standings(bool classic)
    {
        IGpxMode mode = classic ? new ClassicMode() : new DigitalMode();
        Assert.Empty(mode.Dispatch(Context(mode, new() { ShowMode = "none" }, pit: true)));
        var screen = Assert.Single(mode.Dispatch(Context(mode, new() { RaceStandings = "standings", ShowTimeLeft = true }, pit: true)));
        Assert.Contains(":pitt:", screen.Key); Assert.Equal("main", screen.Slot); Assert.Equal(120, screen.Priority);
    }
    [Fact]
    public void Classic_clock_is_exclusive_digital_clock_overlays_standings()
    {
        var classic = new ClassicMode(); var digital = new DigitalMode(); var options = new GpxOptions { ShowTimeLeft = true, RaceStandings = "standings", Rotation = false };
        Assert.Single(classic.Dispatch(Context(classic, options)));
        var screens = digital.Dispatch(Context(digital, options)); Assert.Equal(2, screens.Count); Assert.Contains(screens, s => s.Slot == "top"); Assert.Contains(screens, s => s.Key == "digital:str");
    }
    [Fact]
    public void Standings_layouts_use_original_two_column_geometry_and_assets()
    {
        var classic = new ClassicMode(); var digital = new DigitalMode();
        var cb = Assert.Single(classic.Dispatch(Context(classic, new() { PreviewScreen = "str" }))).Boxes;
        Assert.Contains(cb, b => b.Rect.L == 0 && b.Rect.T == 686 && b.Rect.W == 1024 && b.Alpha == 100 / 255f);
        Assert.Contains(cb, b => b.Image == GpxImage.Position && b.Rect.L == 520 && b.Rect.T == 692);
        var db = Assert.Single(digital.Dispatch(Context(digital, new() { PreviewScreen = "str" }))).Boxes;
        Assert.Contains(db, b => b.Text == "Classification" && b.Rect.L == 18 && b.Rect.T == 676);
        Assert.Contains(db, b => b.Image == GpxImage.Position && b.Rect.L == 512 && b.Rect.T == 696);
        Assert.All(db.Where(b => b.Image == GpxImage.Position), b => { Assert.Equal("digital_yellow", b.Asset); Assert.NotNull(b.SourceRect); });
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Outlap_shows_card_without_invented_split_and_finish_result_wins_older_split(bool classic)
    {
        IGpxMode mode = classic ? new ClassicMode() : new DigitalMode();
        var options = new GpxOptions { ShowTimeLeft = false, QualiStandings = false };
        var outlap = QualiLapState.Empty with { CarIndex = 3, OutLap = true };
        var card = Assert.Single(mode.Dispatch(Context(mode, options, race: false, lap: outlap)));
        Assert.Contains(":card:", card.Key); Assert.Contains(card.Boxes, b => b.Text == "Warm-up lap");
        var finish = new QualiLapResult(4, 80.5, 1, -.2, -.1, true, false, [], .8, false);
        var flying = QualiLapState.Empty with { CarIndex = 3, LastSplit = new(2, 53.1, .1, .2, .5), LastResult = finish };
        var split = Assert.Single(mode.Dispatch(Context(mode, options, race: false, lap: flying)));
        Assert.Contains(split.Boxes, b => b.Text == "Finish Line"); Assert.Contains(split.Boxes, b => b.Text == "1:20.500");
    }
}
