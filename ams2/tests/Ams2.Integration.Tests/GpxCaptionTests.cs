using Ams2.Core;
using Ams2.Core.Calc;
using Ams2.Core.Calc.GpxSat;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Widgets.GpxSat;
using Ams2.OverlayHost.Widgets.GpxSat.Modes;
using Ams2.Shared.Profiles;

namespace Ams2.Integration.Tests;

/// <summary>Legenda recorrente do piloto: o slot principal nunca fica vazio entre eventos (periodica, configuravel, menor prioridade).</summary>
public sealed class GpxCaptionTests
{
    static SessionSnapshot RaceSession(Func<CarSnapshot, CarSnapshot>? edit = null)
    {
        var s = LayoutPreview.ForWidget("lapcounter").Session! with { Kind = SessionKind.Race };
        return s with { Cars = s.Cars.Select(c => c.IsPlayer ? edit?.Invoke(c with { PitState = PitState.None }) ?? c with { PitState = PitState.None } : c).ToList() };
    }

    static GpxContext Context(SessionSnapshot session, double now, GpxSplitTracker? split = null, GpxOptions? options = null)
    {
        var model = LayoutPreview.ForWidget("lapcounter") with { Session = session, Now = now, Broadcast = BroadcastState.Empty with { SessionSeenT = 0 } };
        return new GpxContext { Model = model, Session = session, Options = options ?? new GpxOptions { ShowTimeLeft = false }, Settings = new WidgetSettings { Id = "gpxsat" }, G = new(14), Sky = new(), Split = split ?? new() };
    }

    public static IEnumerable<object[]> Modes() => [[new ClassicMode()], [new DigitalMode()], [new Digital2004Mode()], [new Digital2010Mode()]];

    static GpxScreen[] Main(IGpxMode mode, GpxContext c) => mode.Dispatch(c).Where(s => s.Slot == "main").ToArray();

    [Theory, MemberData(nameof(Modes))]
    public void Caption_fills_the_empty_main_slot_periodically(IGpxMode mode)
    {
        var session = RaceSession();
        var open = Main(mode, Context(session, 100));      // 100 % 25 = 0 -> janela aberta (8 s)
        var cap = Assert.Single(open);
        Assert.Contains(":cap:", cap.Key);
        Assert.Equal("caption", GpxPieces.Of(cap, null));
        Assert.Contains(cap.Boxes, b => b.Text == session.PlayerCar!.Name || b.Text == GpxFormat.DriverName(session.PlayerCar.Name, true) || b.Text?.Contains(session.PlayerCar.Name.Split(' ')[^1], StringComparison.OrdinalIgnoreCase) == true);
        Assert.Empty(Main(mode, Context(session, 112)));    // 112 % 25 = 12 -> fechada
        Assert.Single(Main(mode, Context(session, 125 + 7)));   // proximo ciclo, ainda dentro dos 8 s
    }

    [Theory, MemberData(nameof(Modes))]
    public void Caption_is_continuous_when_duration_covers_the_interval_and_off_in_garage(IGpxMode mode)
    {
        var always = new GpxOptions { ShowTimeLeft = false, CaptionSeconds = 30, CaptionEvery = 20 };
        Assert.Single(Main(mode, Context(RaceSession(), 112, options: always)));
        Assert.Empty(Main(mode, Context(RaceSession(c => c with { PitState = PitState.InGarage }), 100)));
    }

    [Theory, MemberData(nameof(Modes))]
    public void Event_screens_beat_the_caption(IGpxMode mode)
    {
        var baseSession = RaceSession();
        var cars = baseSession.Cars.Select(c => c with { Sector = 2 }).ToList();
        var before = baseSession with { Cars = cars };
        int lead = cars.FindIndex(c => c.Position == 1);
        var after = baseSession with { Cars = cars.Select((c, i) => i == lead ? c with { Sector = 0, LapsCompleted = c.LapsCompleted + 1 } : c).ToList() };
        var split = new GpxSplitTracker();
        split.Update(99, before); split.Update(100, after);
        var main = Assert.Single(Main(mode, Context(after, 101, split)));   // janela da legenda aberta (101 % 25 = 1) e standings do cruzamento do lider
        Assert.DoesNotContain(":cap:", main.Key);
    }

    [Fact]
    public void Caption_durations_come_from_the_exhibition_timings()
    {
        var def = GpxOptions.From(new WidgetSettings { Id = "gpxsat" });
        Assert.Equal((ShowCatalog.CaptionSeconds, ShowCatalog.CaptionEvery), (def.CaptionSeconds, def.CaptionEvery));
        var custom = GpxOptions.From(new WidgetSettings { Id = "gpxsat", Show = new ShowSettings { Timings = new() { ["caption"] = 12, ["captionEvery"] = 40 } } });
        Assert.Equal((12d, 40d), (custom.CaptionSeconds, custom.CaptionEvery));
        var clamped = GpxOptions.From(new WidgetSettings { Id = "gpxsat", Show = new ShowSettings { Timings = new() { ["caption"] = 999, ["captionEvery"] = 1 } } });
        Assert.Equal((60d, 5d), (clamped.CaptionSeconds, clamped.CaptionEvery));
    }

    [Fact]
    public void Caption_piece_exists_and_can_be_turned_off_in_every_gpxsat_theme()
    {
        foreach (var theme in new[] { "f1-1998", "f1-1999-digital", "f1-2004", "f1-2012" })
        {
            Assert.Contains(ShowCatalog.For(theme, "gpxsat").Pieces, p => p.Id == "caption");
            var off = new WidgetSettings { Id = "gpxsat", Show = new ShowSettings { Pieces = new() { ["caption"] = new ShowSettings { Enabled = false } } } }.Normalized(theme);
            Assert.False(ShowPlan.For(off, theme).PieceEnabled("caption"));
        }
    }
}
