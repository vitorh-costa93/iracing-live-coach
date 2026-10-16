using Ams2.Core;
using Ams2.Core.Calc;
using Ams2.Core.Calc.GpxSat;

namespace Ams2.OverlayHost.Widgets.GpxSat.Modes;

/// <summary>GPxSAT Digital original: Futura 18, cores solidas, telas instantaneas.</summary>
public sealed class DigitalMode : StaticGpxModeBase
{
    public override string Id => "digital";
    public override int CarsPerPage => 6;
    public override GpxModeSpec Spec { get; } = new(18, new(["Futura-Condensed-Bold", "Futura"], 700, 1), new(["Futura-Condensed-Bold", "Futura"], 700, 3));
    const float F = 18, W = 1024, H = 768, C = 512, Third = 341;
    static float Y(int n) => H - n * F;
    static GpxBox B(float l, float r, int row, string tex, string? text = null, GpxAlign al = GpxAlign.Left, uint ink = White)
        => Box(l, Y(row), r, Y(row) + F, tex, text, al, ink, F / 3);
    static GpxBox Trans(float l, float r, int row, string? text = null, GpxAlign al = GpxAlign.Left, uint ink = White, bool twice = false)
        => B(l, r, row, Dark, text, al, ink) with { Alpha = twice ? 128 / 255f : 64 / 255f };
    static List<GpxBox> Team(CarSnapshot car)
    {
        var b = new List<GpxBox> { B(0, C, 3, Grey), B(2 * F, C, 3, Clear, GpxContext.Team(car), ink: Black) };
        Tyre(b, car, F, Y(3), F); return b;
    }
    static void Name(List<GpxBox> b, GpxContext c, CarSnapshot car, int pos, float right = Third)
    {
        if (pos > 0) b.Add(Tile(2 * F, Y(2), F, pos)); else b.Add(B(0, W / 4, 2, Grey));
        b.Add(B(pos > 0 ? 3 * F : W / 4, pos > 0 ? right : C, 2, Blue, c.Name(car)));
    }
    protected override GpxScreen? Clock(GpxContext c, bool force = false)
    {
        string? text = null;
        if (c.IsRace && c.Session.LapsInEvent > 0)
        {
            int lap = c.Session.Cars.FirstOrDefault(x => x.Position == 1)?.CurrentLap ?? 0;
            text = $"{(lap == 0 ? c.Session.LapsInEvent : Math.Max(0, c.Session.LapsInEvent - lap + 1))}/{c.Session.LapsInEvent}";
        }
        else if (c.Session.TimeRemainingSeconds is { } rem) text = rem <= 0 ? "Session over" : GpxFormat.Clock(rem);
        else if (force) text = GpxFormat.Clock(5025);
        return text is null ? null : Screen("time", [Box(0, F, W, 2 * F, text: text, align: GpxAlign.Center) with { Shadow = true }], "top");
    }
    protected override GpxScreen Fastest(GpxContext c, CarSnapshot car, double lap)
    {
        var b = Team(car); b.Add(B(3 * F, Third, 2, Blue, c.Name(car))); b.Add(Trans(Third, C, 2, GpxFormat.LapTime(lap), GpxAlign.Right));
        b.Add(B(C, W - 102, 3, Gold, "Fastest Lap", ink: Black)); b.Add(B(C, W - 102, 3, Clear, GpxFormat.Lap(Math.Max(1, car.LapsCompleted)), GpxAlign.Right, Black));
        return Screen("fl:" + car.Index, b);
    }
    protected override GpxScreen OutOfRace(GpxContext c, CarSnapshot car)
    {
        var b = Team(car); b.Add(B(3 * F, C, 2, Blue, c.Name(car))); b.Add(B(C, W - 102, 3, Gold, "OUT OF THE RACE", ink: Black));
        return Screen("out:" + car.Index, b);
    }
    protected override GpxScreen InPits(GpxContext c, CarSnapshot car)
    {
        if (c.Focus?.Index == car.Index) return PitTimer(c, car, PitSeconds(c));
        var b = Team(car); Name(b, c, car, c.PositionOf(car), C); b.Add(B(C, W - 102, 3, Gold, "In the pits", ink: Black)); return Screen("pit:" + car.Index, b);
    }
    protected override GpxScreen PitTimer(GpxContext c, CarSnapshot car, double seconds) => Screen("pitt:" + car.Index,
        [B(3 * F, Third, 3, Red, "Pit stop"), B(3 * F, Third, 3, Clear, GpxFormat.Tenths(seconds), GpxAlign.Right), B(3 * F, Third, 2, Blue, c.Name(car))]);
    protected override GpxScreen Winner(GpxContext c, WinnerInfo winner)
    {
        var b = Team(winner.Car); Name(b, c, winner.Car, 1); b.Add(Box(2 * F, Y(6), 5 * F, Y(3)) with { Image = GpxImage.Flag });
        b.Add(Trans(Third, C, 2, GpxFormat.LapTime(winner.TotalSeconds), GpxAlign.Right));
        b.Add(B(C, W - Third, 2, Grey, GpxFormat.AvgSpeed(winner.AvgKmh), GpxAlign.Center, Black) with { Indent = 0 });
        b.Add(Trans(W - Third, W - 102, 2, GpxFormat.Distance(winner.DistanceKm), GpxAlign.Center, Yellow)); b.Add(B(C, W - 102, 3, Gold, "Race winner", ink: Black));
        return Screen("win:" + winner.Car.Index, b);
    }
    protected override GpxScreen Riding(GpxContext c, CarSnapshot car)
    {
        var b = Team(car); int pos = QualiPosition(c, car); b.Add(B(W / 4, C, 2, Blue));
        if (pos > 0) b.Add(Tile(W / 4, Y(2), F, pos)); else b.Add(B(0, W / 4, 2, Grey));
        b.Add(B(W / 4 + (pos > 0 ? F : 0), C, 2, Clear, c.Name(car))); return Screen("ride:" + car.Index, b);
    }
    protected override GpxScreen Card(GpxContext c, CarSnapshot car)
    {
        var b = Team(car); var q = c.Model.Quali?.Row(car.Index); Name(b, c, car, QualiPosition(c, car));
        b.Add(Trans(C, W, 3, $"{car.LapsCompleted} Laps completed", ink: Yellow, twice: true));
        b.Add(Trans(Third, C, 2, c.Model.QualiLap?.OutLap == true ? "Warm-up lap" : GpxFormat.LapTime(q?.BestLap), GpxAlign.Right, twice: true));
        b.Add(Trans(C, W, 2, q is { Rank: > 1, GapToFirst: { } gap } ? GpxFormat.SignedGap(gap) : "", ink: Yellow)); return Screen("card:" + car.Index, b);
    }
    protected override GpxScreen Split(GpxContext c, CarSnapshot car, string label, double? time, double? delta, int position)
    {
        var b = new List<GpxBox> { B(0, Third, 3, Grey, label, GpxAlign.Right, Black), B(3 * F, Third, 2, Blue, c.Name(car)), Trans(Third, C, 2, GpxFormat.LapTime(time), GpxAlign.Right, twice: true) };
        if (position > 0) b.Add(Tile(2 * F, Y(2), F, position));
        if (position == 1 && c.Session.Kind == SessionKind.Qualify)
        {
            b.Add(B(C, W - 102, 3, Gold, "Pole position", ink: Black)); b.Add(Box(C - 3 * F, Y(6), C, Y(3), Gold, "1", GpxAlign.Center, Black) with { Image = GpxImage.BigOne });
        }
        else if (c.Model.Quali?.Leader is { } leader)
        {
            b.Add(Tile(C, Y(3), F, 1)); b.Add(B(C + F, C + W / 4, 3, Blue, c.Name(leader.Car)));
            double? reference = time is { } t && delta is { } d ? t - d : null; b.Add(Trans(Third, C, 3, GpxFormat.LapTime(reference), GpxAlign.Right));
        }
        b.Add(Trans(C, W - Third, 2, delta is { } gap ? GpxFormat.SignedGap(gap) : "", ink: Yellow) with { Indent = F }); return Screen("split:" + car.Index + ":" + label, b);
    }
    protected override GpxScreen Gap(GpxContext c, GpxGapWindow w)
    {
        var b = new List<GpxBox>(); var left = w.Ahead ? w.Other : w.Focus; var right = w.Ahead ? w.Focus : w.Other;
        b.Add(Tile(2 * F, Y(3), F, left.Position)); b.Add(B(3 * F, Third, 3, Blue, c.Name(left)));
        b.Add(Trans(Third, C, 3, w.Laps == 0 ? GapText(c, w) : "", GpxAlign.Center, twice: true));
        b.Add(Trans(Third, C, 2, w.Laps > 0 ? GpxFormat.LapsBehind(w.Laps) : w.Change is { } change ? GpxFormat.Change(change) : "", GpxAlign.Center));
        b.Add(Tile(C, Y(2), F, right.Position)); b.Add(B(C + F, W - W / 4, 2, Blue, c.Name(right))); return Screen((w.Ahead ? "gapa:" : "gapb:") + w.Focus.Index + ":" + w.Pass, b);
    }
    protected override GpxScreen Trap(GpxContext c, GpxTrapWindow w)
    {
        var b = new List<GpxBox>
        {
            Box(F, Y(4) - 3, Third, Y(3) - 3, Blue, w.Top is { } top ? c.Name(top) : "", indent: F / 3), Trans(F, Third, 3, w.TopMps > 0 ? GpxFormat.Speed(w.TopMps) : "", ink: 0xffffff00),
            B(Third, W - Third, 3, Grey, "Top Speeds", GpxAlign.Center, Black), Box(Third, Y(2) + 3, W - Third, Y(1) + 3, Grey, "Current Speed", GpxAlign.Center, Black),
            Box(W - Third, Y(4) - 3, W - F, Y(3) - 3, Blue, c.Name(w.Focus), GpxAlign.Right, indent: F / 3), Trans(W - Third, W - F, 3, w.FocusBestMps > 0 ? GpxFormat.Speed(w.FocusBestMps) : "", GpxAlign.Right, Yellow),
        };
        if (w.SpeedMps > 0) b.Add(Box(W - Third, Y(2) + 3, W - F, Y(1) + 3, Dark, GpxFormat.Speed(w.SpeedMps), GpxAlign.Right, Yellow, F / 3) with { Alpha = 64 / 255f });
        return Screen("trap:" + w.Focus.Index + ":" + w.OpenT, b);
    }
    protected override GpxScreen Penalty(GpxContext c, CarSnapshot car)
    {
        bool jump = c.IsRace && car.LapsCompleted < 1; int upper = jump ? 4 : 3;
        var b = new List<GpxBox> { Box(0, Y(upper), C, Y(upper) + 2 * F, Grey), B(2 * F, C, upper, Clear, GpxContext.Team(car), ink: Black), B(W / 4, C, upper - 1, Blue, c.Name(car)) with { Indent = F / 2 } };
        Tyre(b, car, F / 2, Y(upper), F);
        if (jump) { b.Add(Trans(0, W - W / 4, 2, "Jump Start", GpxAlign.Right, Yellow)); b.Add(B(W - W / 4, W, 3, Red, "10 Seconds") with { Indent = F / 2 }); }
        else { b.Add(B(C, W, 3, Red, "Drive Through Penalty", GpxAlign.Right) with { Indent = 2 * F }); b.Add(Trans(C, W, 2)); }
        return Screen("dt:" + car.Index, b);
    }
    protected override GpxScreen Standings(GpxContext c, string kind, int? page = null)
    {
        var rows = Rows(c, kind); int p = page ?? c.PageOf(rows.Count, CarsPerPage), r = (CarsPerPage + 1) / 2;
        var b = new List<GpxBox> { Box(F, H - (r + 2) * F - 2, C, H - (r + 1) * F - 2, Grey, "Classification", ink: Black, indent: F / 3) };
        var slice = rows.Skip(p * CarsPerPage).Take(CarsPerPage).ToList();
        for (int i = 0; i < slice.Count; i++)
        {
            float xo = i / r * C, padL = i < r ? F : 0, padR = i < r ? 0 : F, y = H - (r + 1) * F + i % r * (F + 2); var row = slice[i];
            if (row.Position is { } pos) b.Add(Tile(xo + padL, y, F, pos));
            b.Add(Box(xo + padL + F + 2, y, xo + Third - padR, y + F, Blue)); b.Add(Box(xo + Third - padR, y, C + xo - padR, y + F, Dark) with { Alpha = 64 / 255f });
            b.Add(Box(xo + padL + F, y, C + xo - padR, y + F, text: c.Name(row.Car), indent: F / 3) with { TopAligned = true });
            b.Add(Box(xo + padL + F, y, C + xo - padR, y + F, text: row.Value, align: GpxAlign.Right, indent: F / 3) with { TopAligned = true });
        }
        return Screen(kind, b, page: p, priority: 40);
    }
}
