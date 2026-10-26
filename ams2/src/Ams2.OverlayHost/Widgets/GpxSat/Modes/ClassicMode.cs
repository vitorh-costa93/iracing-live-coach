using Ams2.Core;
using Ams2.Core.Calc;
using Ams2.Core.Calc.GpxSat;

namespace Ams2.OverlayHost.Widgets.GpxSat.Modes;

/// <summary>GPxSAT Classic original: Mañana, faixas translucidas, sem animacao (spec_classic.md).</summary>
public sealed class ClassicMode : StaticGpxModeBase
{
    public override string Id => "classic";
    public override int CarsPerPage => 8;
    protected override bool ExclusiveClock => true;
    public override GpxModeSpec Spec { get; } = new(18, new(["Mañana", "Manana"], 700, 1), new(["Mañana", "Manana"], 700, 1.25f));
    const float F = 18, W = 1024, H = 768, C = 512;
    const uint Y = 0xffffff00;
    const string FadeA = "classic_fade_a", FadeB = "classic_fade_b", FadeC = "classic_fade_c", FadeD = "classic_fade_d";
    public ClassicMode()
    {
        GpxTextures.Define(FadeA, [new(0, Black), new(1, Yellow)]); GpxTextures.Define(FadeB, [new(0, Yellow), new(1, White)]);
        GpxTextures.Define(FadeC, [new(0, White), new(1, Yellow)]); GpxTextures.Define(FadeD, [new(0, Yellow), new(1, Black)]);
    }
    static float Top(int lines) => H - lines * F - 10;
    static float Origin(int lines = 4) => Top(lines) + 5;
    static List<GpxBox> Band(int lines = 4, bool fixedAlpha = false) => [Box(0, Top(lines), W, H, Dark) with { Alpha = (fixedAlpha ? 100 : 64) / 255f }];
    static GpxBox Text(float l, float t, float r, string text, GpxAlign al = GpxAlign.Left, uint ink = White, float height = F)
        => Box(l, t, r, t + height, text: text, align: al, ink: ink) with { TopAligned = true };
    static void Identity(List<GpxBox> b, GpxContext c, CarSnapshot car, float y)
    {
        if (!string.IsNullOrWhiteSpace(car.CarNumber)) b.Add(Box(F / 2, y, 1.5f * F, y + F, text: car.CarNumber, align: GpxAlign.Center, ink: Cyan) with { Image = GpxImage.CarNumber });
        Tyre(b, car, F / 2, y + F + 5, F); b.Add(Text(2 * F, y, C, c.Name(car))); b.Add(Text(2 * F, y + F + 5, C, GpxContext.Team(car), ink: Cyan));
    }
    protected override GpxScreen? Clock(GpxContext c, bool force = false)
    {
        if (c.IsRace && c.Session.LapsInEvent > 0)
        {
            var leader = c.Session.Cars.FirstOrDefault(x => x.Position == 1);
            int lap = leader?.CurrentLap ?? 0, left = lap <= 0 ? c.Session.LapsInEvent : Math.Max(0, c.Session.LapsInEvent - lap + 1);
            var b = Band(3); float y = Top(3) + F / 2;
            b.Add(Text(0, y, W, left == 0 ? "Race over" : left == 1 ? "Final lap" : $"{left} Laps remaining", GpxAlign.Center));
            if (leader is not null) b.Add(Text(0, y + 4 * F / 3, W, "Leader - " + c.Name(leader), GpxAlign.Center));
            return Screen("time:race", b, priority: 80);
        }
        if (c.Session.TimeRemainingSeconds is not { } rem && !force) return null;
        double remaining = c.Session.TimeRemainingSeconds ?? 5025;
        var boxes = Band(2); boxes.Add(Box(0, Top(2), W, H, text: remaining <= 0 ? "Session over" : GpxFormat.Clock(remaining), align: GpxAlign.Center, ink: Y));
        return Screen("time", boxes, priority: 80);
    }
    protected override GpxScreen Fastest(GpxContext c, CarSnapshot car, double lap)
    {
        var b = Band(); float y = Origin();
        // Original columns depend on measured "Fastest Lap" and "Lap". The dispatcher has no font metrics; reserve 9FH and 4FH.
        float cw1 = 9 * F, cw2 = 4 * F;
        b.Add(Text(64, y, C, c.Name(car))); b.Add(Text(64, y + F + 8, C, GpxContext.Team(car), ink: Cyan));
        b.Add(Text(W - cw1 - cw2 - 32, y, W - cw1 - 32, "Lap", GpxAlign.Center, Y));
        b.Add(Text(W - cw1 - cw2 - 32, y + F + 8, W - cw1 - 32, Math.Max(1, car.LapsCompleted).ToString(), GpxAlign.Center, Y));
        b.Add(Text(W - cw1, y, W - 32, "Fastest Lap", GpxAlign.Center, Cyan)); b.Add(Text(W - cw1, y + F + 8, W - 32, GpxFormat.LapTime(lap), GpxAlign.Center, Y));
        return Screen("fl:" + car.Index, b);
    }
    protected override GpxScreen OutOfRace(GpxContext c, CarSnapshot car)
    {
        var b = Band(); b.Add(Text(C, Origin(), W - 32, car.Name + " Is out of the race", GpxAlign.Right));
        // AMS2 does not expose the retirement cause; leave the reason line empty.
        return Screen("out:" + car.Index, b);
    }
    protected override GpxScreen InPits(GpxContext c, CarSnapshot car)
    {
        if (c.Focus?.Index == car.Index) return PitTimer(c, car, PitSeconds(c));
        var b = Band(); float y = Origin(); if (c.PositionOf(car) > 0) b.Add(Tile(2 * F, y + F, 2 * F, c.PositionOf(car)));
        b.Add(Text(C, y, W - 32, car.Name + " Is in the pits", GpxAlign.Right)); return Screen("pit:" + car.Index, b);
    }
    protected override GpxScreen PitTimer(GpxContext c, CarSnapshot car, double seconds)
    {
        var b = Band(3); float y = Origin(3); b.Add(Text(0, y + F, W, "Pit stop", GpxAlign.Center, Y));
        b.Add(Text(C, y, W - 4 * F, car.Name, GpxAlign.Right)); b.Add(Text(C, y + F, W - 4 * F, GpxFormat.Tenths(seconds), GpxAlign.Right, Y)); return Screen("pitt:" + car.Index, b);
    }
    protected override GpxScreen Penalty(GpxContext c, CarSnapshot car)
    {
        var b = Band(fixedAlpha: true); float t = Top(4), y = t + F / 2;
        if (!string.IsNullOrWhiteSpace(car.CarNumber)) b.Add(Box(W / 10, y, W / 10 + F, y + F, text: car.CarNumber) with { Image = GpxImage.CarNumber });
        Tyre(b, car, W / 10, y + F + 5, F);
        b.Add(Box(W / 7, y, W, y + F, text: c.Name(car))); b.Add(Box(W / 7, y + F + 5, W, y + 2 * F + 5, text: GpxContext.Team(car), ink: Cyan));
        b.Add(Text(W / 6, t + F / 4, W, c.IsRace && car.LapsCompleted < 1 ? "Jump Start" : "Drive Through", GpxAlign.Center));
        b.Add(Text(W / 6, t + F, W, "P E N A L T Y", GpxAlign.Center, height: F * 1.25f) with { Font = GpxFontRole.Big, Shadow = true });
        return Screen("dt:" + car.Index, b);
    }
    protected override GpxScreen Winner(GpxContext c, WinnerInfo winner)
    {
        var b = Band(); float y = Origin(); b.Add(Box(C - 3 * F, y - 2 * F, C + 3 * F, y + 3 * F) with { Image = GpxImage.Flag }); Identity(b, c, winner.Car, y);
        b.Add(Box(W - 9 * F, Top(4) + F / 4, W - 2 * F, y + 2 * F, text: "WINNER", align: GpxAlign.Center) with { Image = GpxImage.Winner });
        b.Add(Text(2 * F, y + 2 * F + 5, C, GpxFormat.LapTime(winner.TotalSeconds), ink: Y));
        b.Add(Text(W / 4, y + 2 * F + 5, W - W / 4, GpxFormat.AvgSpeed(winner.AvgKmh), GpxAlign.Center, Y));
        b.Add(Text(C, y + 2 * F + 5, W - 2 * F, GpxFormat.Distance(winner.DistanceKm), GpxAlign.Right, Y)); return Screen("win:" + winner.Car.Index, b);
    }
    protected override GpxScreen Riding(GpxContext c, CarSnapshot car)
    {
        // Width approximation until the dispatcher can query DWrite: 0.58 FH per character.
        float width = Math.Min(W - 2 * F, car.Name.Length * F * .58f + F * (c.IsRace ? 4 : 3)), left = W - F - width;
        var b = new List<GpxBox> { Box(left, F, W - F, 2 * F, Dark) with { Alpha = 64 / 255f } };
        if (c.IsRace && car.Position > 0) b.Add(Tile(left, F, F, car.Position));
        b.Add(Box(left + (c.IsRace ? 3 * F : 2 * F), F, W - 2 * F, 2 * F, text: car.Name, align: GpxAlign.Right));
        return Screen("ride:" + car.Index, b, "riding", 100);
    }
    protected override GpxScreen Card(GpxContext c, CarSnapshot car)
    {
        var b = Band(); float y = Origin(); Identity(b, c, car, y); var q = c.Model.Quali?.Row(car.Index); int pos = QualiPosition(c, car);
        if (pos > 0) b.Add(Tile(W - 2 * 7 * F - 2 * F, y, 2 * F, pos));
        b.Add(Text(C, y, W - F / 2, c.Model.QualiLap?.OutLap == true ? "Warm-up lap" : GpxFormat.LapTime(q?.BestLap), GpxAlign.Right, Y));
        b.Add(Text(C, y + F + 5, W - F / 2, q is { Rank: > 1, GapToFirst: { } gap } ? GpxFormat.SignedGap(gap) : "", GpxAlign.Right, Y)); return Screen("card:" + car.Index, b);
    }
    protected override GpxScreen Caption(GpxContext c, CarSnapshot car)
    {
        var b = Band(); float y = Origin(); Identity(b, c, car, y); int pos = QualiPosition(c, car);
        if (pos > 0) b.Add(Tile(W - 2.5f * F, y, 2 * F, pos));
        CountryFlag(b, car, W - 2.5f * F - 3.2f * F, y + F / 4, W - 2.5f * F - F / 2, y + 1.5f * F);
        return Screen("cap:" + car.Index, b, priority: 10);
    }
    protected override GpxScreen Split(GpxContext c, CarSnapshot car, string label, double? time, double? delta, int position)
    {
        var b = Band(); float y = Origin(); var leader = c.Model.Quali?.Leader;
        if (position > 0) b.Add(Tile(W / 4 - F, y + F, 2 * F, position));
        else if (leader is not null)
        {
            b.Add(Text(0, y, C, c.Name(leader.Car), GpxAlign.Center)); double? reference = time is { } t && delta is { } d ? t - d : null;
            b.Add(Text(0, y + F, C, GpxFormat.LapTime(reference), GpxAlign.Center, Y));
        }
        b.Add(Text(0, y, W, label, GpxAlign.Center, Y)); b.Add(Text(0, y + F, W, delta is { } gap ? GpxFormat.SignedGap(gap) : "", GpxAlign.Center, Cyan));
        b.Add(Text(C, y, W, c.Name(car), GpxAlign.Center)); b.Add(Text(C, y + F, W, GpxFormat.LapTime(time), GpxAlign.Center, Y)); return Screen("split:" + car.Index + ":" + label, b);
    }
    protected override GpxScreen Gap(GpxContext c, GpxGapWindow w)
    {
        var b = Band(fixedAlpha: true); float y = Origin(), width = W / 5, half = width / 2;
        var left = w.Ahead ? w.Other : w.Focus; var right = w.Ahead ? w.Focus : w.Other;
        b.Add(Box(4 * F, y + F / 2, 4 * F + half, y + 4 * F / 3, FadeA) with { Horizontal = true });
        b.Add(Box(4 * F + half, y + F / 2, 4 * F + width, y + 4 * F / 3, FadeB) with { Horizontal = true });
        b.Add(Box(W - 4 * F - width, y + F / 2, W - 4 * F - half, y + 4 * F / 3, FadeC) with { Horizontal = true });
        b.Add(Box(W - 4 * F - half, y + F / 2, W - 4 * F, y + 4 * F / 3, FadeD) with { Horizontal = true });
        b.Add(Tile(F, y + F / 2, 2 * F, left.Position)); b.Add(Text(4 * F, y + 1.5f * F, C, c.Name(left)));
        b.Add(Tile(W - 3 * F, y + F / 2, 2 * F, right.Position)); b.Add(Text(C, y + 1.5f * F, W - 4 * F, c.Name(right), GpxAlign.Right));
        b.Add(Text(0, y + F / 2, W, w.Laps == 0 ? GapText(c, w) : "", GpxAlign.Center, Y));
        b.Add(Text(0, y + 1.5f * F, W, w.Laps > 0 ? GpxFormat.LapsBehind(w.Laps) : w.Change is { } change ? GpxFormat.Change(change) : "", GpxAlign.Center, Y));
        b.Add(Text(0, y + 3 * F, W, SplitLabel(w.Split), GpxAlign.Center, Y)); return Screen((w.Ahead ? "gapa:" : "gapb:") + w.Focus.Index + ":" + w.Pass, b);
    }
    protected override GpxScreen Trap(GpxContext c, GpxTrapWindow w)
    {
        var b = Band(fixedAlpha: true); float y = Origin();
        if (w.Top is { } top) b.Add(Text(0, y, C, c.Name(top), GpxAlign.Center));
        b.Add(Text(0, y + F, C, w.TopMps > 0 ? GpxFormat.Speed(w.TopMps) : "", GpxAlign.Center, Y));
        b.Add(Text(0, y + F, W, "Speed", GpxAlign.Center)); b.Add(Text(0, y + 2 * F, W, "Current", GpxAlign.Center));
        b.Add(Text(C, y, W, c.Name(w.Focus), GpxAlign.Center)); b.Add(Text(C, y + F, W, GpxFormat.Speed(w.SpeedMps), GpxAlign.Center, Y));
        float speedWidth = 7 * F * .58f, speedCenter = C + W / 4;
        b.Add(Box(speedCenter - speedWidth / 2, y + F, speedCenter + speedWidth / 2, y + 2 * F) with { Stroke = White });
        if (w.FocusBestMps > 0) b.Add(Text(C, y + 2 * F, W, GpxFormat.Speed(w.FocusBestMps), GpxAlign.Center, Y)); return Screen("trap:" + w.Focus.Index + ":" + w.OpenT, b);
    }
    protected override GpxScreen Standings(GpxContext c, string kind, int? page = null)
    {
        var rows = Rows(c, kind); int p = page ?? c.PageOf(rows.Count, CarsPerPage), r = (CarsPerPage + 1) / 2; var b = Band(r, true); float y = Origin(r);
        var slice = rows.Skip(p * CarsPerPage).Take(CarsPerPage).ToList();
        for (int i = 0; i < slice.Count; i++)
        {
            float xo = i / r * C, yy = y + i % r * F; var row = slice[i];
            if (row.Position is { } pos) b.Add(Box(xo + 8, yy + 1, xo + F + 5, yy + F - 1, Gold, pos.ToString(), GpxAlign.Center, Black) with { Image = GpxImage.Position });
            b.Add(Text(xo + 2 * F - 5, yy, xo + C - F, c.Name(row.Car))); b.Add(Text(xo + 2 * F - 5, yy, xo + C - F, row.Value, GpxAlign.Right, Y));
        }
        return Screen(kind, b, page: p, priority: 40);
    }
}
