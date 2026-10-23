using Ams2.Core;
using Ams2.Core.Calc;
using Ams2.Core.Calc.GpxSat;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Widgets.GpxSat;
using Ams2.OverlayHost.Widgets.GpxSat.Modes;
using Ams2.Shared.Profiles;

namespace Ams2.Integration.Tests;

/// <summary>Standings de corrida por evento: nada sem evento; lider fecha a volta -> estaticos pela janela -> slot vazio.</summary>
public sealed class GpxLapStandingsTests
{
    static GpxContext Context(GpxSplitTracker split, SessionSnapshot session, double now)
    {
        var model = LayoutPreview.ForWidget("lapcounter");
        model = model with { Session = session, Now = now, Broadcast = BroadcastState.Empty with { SessionSeenT = 0 } };
        return new GpxContext { Model = model, Session = session, Options = new GpxOptions { ShowTimeLeft = false }, Settings = new WidgetSettings { Id = "gpxsat" }, G = new(14), Sky = new(), Split = split };
    }

    public static IEnumerable<object[]> Modes() => [[new ClassicMode()], [new DigitalMode()], [new Digital2004Mode()], [new Digital2010Mode()]];

    [Theory, MemberData(nameof(Modes))]
    public void No_event_means_no_standings_then_leader_lap_shows_them_static_then_slot_empties(IGpxMode mode)
    {
        var baseSession = LayoutPreview.ForWidget("lapcounter").Session! with { Kind = SessionKind.Race };
        var cars = baseSession.Cars.Select(c => c with { Sector = 2 }).ToList();
        var before = baseSession with { Cars = cars };
        int lead = cars.FindIndex(c => c.Position == 1);
        var after = baseSession with { Cars = cars.Select((c, i) => i == lead ? c with { Sector = 0, LapsCompleted = c.LapsCompleted + 1 } : c).ToList() };

        var split = new GpxSplitTracker();
        split.Update(99, before);
        static int Main(IReadOnlyList<GpxScreen> s) => s.Count(x => x.Slot == "main");
        var pre = mode.Dispatch(Context(split, before, 99.5)); Assert.True(Main(pre) == 0, string.Join(",", pre.Select(x => x.Slot + "=" + x.Key)));      // sem evento: nada
        split.Update(100, after);
        var shown = mode.Dispatch(Context(split, after, 101));
        Assert.Equal(1, Main(shown));
        Assert.Equal(shown.Single(x => x.Slot == "main").Key, mode.Dispatch(Context(split, after, 105)).Single(x => x.Slot == "main").Key);   // estatica
        Assert.Equal(0, Main(mode.Dispatch(Context(split, after, 100 + 60))));   // janela acabou: vazio
    }
}
