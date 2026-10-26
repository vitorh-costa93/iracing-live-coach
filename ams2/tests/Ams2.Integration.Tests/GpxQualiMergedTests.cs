using Ams2.Core;
using Ams2.Core.Calc;
using Ams2.Core.Calc.GpxSat;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Widgets.GpxSat;
using Ams2.OverlayHost.Widgets.GpxSat.Modes;

namespace Ams2.Integration.Tests;

/// <summary>Qualy Board mesclado (ESPECIFICACAO-TEMAS 3.4): tabela so na volta de saida, 'FIRST LAP OUT OF PITS', parcial 3 s e resultado 6 s.</summary>
public class GpxQualiMergedTests
{
    static GpxContext Ctx(IGpxMode mode, double now, QualiLapState lap, GpxOptions options)
    {
        var cars = Enumerable.Range(1, 10).Select(p => new CarSnapshot(p, "Driver " + p, "Car", "F1", p, p, 3, 4, 1000, 1, 81 + p / 10d, 82, 60,
            PitState.None, RaceState.Racing, false, p == 3, "M", CarNumber: p.ToString())).ToList();
        var player = new PlayerSnapshot(3, new(0, 0, 0, 0, 0, 0, 0, 0), 3, 7000, 10000, 60, 50, 100, [], .5);
        var session = new SessionSnapshot(1, 1, true, SessionKind.Qualify, "Track", "", 5000, 0, 600, 0, new(20, 30, 0, 0, 0, 0, 0, 0), cars, player);
        var model = OverlayModel.Empty with { Connected = true, Now = now, Session = session, Quali = QualiTable.Build(session), QualiLap = lap, PlayerDriving = true };
        return new() { Model = model, Session = session, Options = options, Settings = new(), G = new(mode.Spec.FH), Sky = new(), Split = new() };
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Table_only_on_out_lap_then_first_lap_caption(bool classic)
    {
        IGpxMode mode = classic ? new ClassicMode() : new DigitalMode();
        var o = new GpxOptions { ShowTimeLeft = false, QualiStandings = true };
        var outlap = QualiLapState.Empty with { CarIndex = 3, OutLap = true };
        var first = Assert.Single(mode.Dispatch(Ctx(mode, 100, outlap, o)));
        Assert.Contains("stq", first.Key);
        var later = Assert.Single(mode.Dispatch(Ctx(mode, 130, outlap, o)));
        Assert.Contains(":card:", later.Key); Assert.Contains(later.Boxes, b => b.Text == "FIRST LAP OUT OF PITS");
    }

    [Fact]
    public void Split_lasts_3s_and_result_6s_and_never_a_continuous_table()
    {
        var mode = new ClassicMode(); var o = new GpxOptions { ShowTimeLeft = false, QualiStandings = true };
        var finish = new QualiLapResult(4, 80.5, 1, -.2, -.1, true, false, [], 100, false);
        var lap = QualiLapState.Empty with { CarIndex = 3, Lap = 4, Elapsed = 20, LastSplit = new(1, 25, .1, .2, 100), LastResult = null };
        Assert.Contains(Assert.Single(mode.Dispatch(Ctx(mode, 102, lap, o))).Boxes, b => b.Text == "Intermediate 1");
        Assert.DoesNotContain(mode.Dispatch(Ctx(mode, 104, lap, o)), s => s.Boxes.Any(b => b.Text == "Intermediate 1"));
        var res = QualiLapState.Empty with { CarIndex = 3, Lap = 5, Elapsed = 1, LastResult = finish };
        Assert.Contains(Assert.Single(mode.Dispatch(Ctx(mode, 105.5, res, o))).Boxes, b => b.Text == "Finish Line");
        Assert.DoesNotContain(mode.Dispatch(Ctx(mode, 107, res, o)), s => s.Boxes.Any(b => b.Text == "Finish Line"));
    }
}
