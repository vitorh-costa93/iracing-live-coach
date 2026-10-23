using Ams2.Core;
using Ams2.Core.Calc;
using Ams2.Core.Calc.GpxSat;
using Ams2.OverlayHost.Data;

namespace Ams2.OverlayHost.Widgets.GpxSat.Modes;

/// <summary>Despacho dos dois modos estaticos originais, sem passar pelo animador do Digital 2004.</summary>
public abstract class StaticGpxModeBase : IGpxMode
{
    public abstract string Id { get; }
    public abstract int CarsPerPage { get; }
    public abstract GpxModeSpec Spec { get; }
    public bool Implemented => true;
    protected virtual bool ExclusiveClock => false;
    protected const uint White = GpxInk.White, Black = GpxInk.Black, Yellow = 0xfff7dc42, Cyan = 0xff00ffff;
    protected const string Clear = "static_clear", Dark = "static_dark", Blue = "static_blue", Grey = "static_grey", Gold = "static_gold", Red = "static_red";

    protected StaticGpxModeBase()
    {
        foreach (var (name, color) in new (string, uint)[] { (Clear, 0), (Dark, 0xff000000), (Blue, 0xff1675a5), (Grey, 0xffd4d0c8), (Gold, Yellow), (Red, 0xfffe0000) })
            GpxTextures.Define(name, [new(0, color), new(1, color)]);
    }

    protected static GpxBox Box(float l, float t, float r, float b, string texture = Clear, string? text = null, GpxAlign align = GpxAlign.Left, uint ink = White, float indent = 0)
        => new(new(l, t, r - l, b - t), texture, text, align, ink, Indent: indent);
    protected static GpxBox Tile(float l, float t, float size, int pos) => Box(l, t, l + size, t + size, Gold, pos.ToString(), GpxAlign.Center, Black) with { Image = GpxImage.Position };
    protected GpxScreen Screen(string key, IReadOnlyList<GpxBox> boxes, string slot = "main", int priority = 30, int page = 0)
        => new(slot, Id + ":" + key, boxes.Select(Artwork).ToList(), GpxAnimKind.Instant, page, priority);
    GpxBox Artwork(GpxBox box)
    {
        string prefix = Id == "classic" ? "classic" : "digital";
        string? asset = box.Image switch
        {
            GpxImage.Position => prefix + "_yellow", GpxImage.Flag => prefix + "_flag", GpxImage.CarNumber => "blue",
            GpxImage.Winner => "winner", GpxImage.BigOne => "big1", GpxImage.TyreM => prefix + "_m", GpxImage.TyreB => prefix + "_b", _ => null,
        };
        if (asset is null) return box;
        GpxRect? src = null;
        if (box.Image is GpxImage.Position or GpxImage.CarNumber && int.TryParse(box.Text, out int number))
        {
            int index = box.Image == GpxImage.Position ? number - 1 : number;
            if (index < 0 || index > (box.Image == GpxImage.Position ? 21 : 99)) return box;
            src = new(index % 8 * 64, index / 8 * 64, 64, 64);
        }
        return box with { Asset = asset, SourceRect = src };
    }
    protected static void Tyre(List<GpxBox> boxes, CarSnapshot car, float l, float t, float size)
    {
        if (car.TyreSupplier is "M" or "B") boxes.Add(Box(l, t, l + size, t + size) with { Image = car.TyreSupplier == "M" ? GpxImage.TyreM : GpxImage.TyreB });
    }
    protected static bool Out(CarSnapshot car) => car.RaceState is RaceState.Dnf or RaceState.Retired or RaceState.Disqualified;
    protected static double PitSeconds(GpxContext c) => c.Broadcast.PlayerStopped ? c.Broadcast.PlayerStopNow(c.Now)
        : c.Broadcast.PlayerStopEndT >= c.Broadcast.PlayerPitLaneStartT ? c.Broadcast.PlayerStopSeconds : 0;
    protected static string SplitLabel(int split) => split == 0 ? GpxFormat.FinishLine : GpxFormat.Intermediate(split);
    protected static string GapText(GpxContext c, GpxGapWindow w) => w.Laps > 0 ? GpxFormat.LapsBehind(w.Laps)
        : w.Gap is { } gap ? GpxFormat.SignedGap(gap) : GpxFormat.Tenths(w.Running(c.Now));
    protected static int QualiPosition(GpxContext c, CarSnapshot car) => c.IsRace ? car.Position : c.Model.Quali?.Row(car.Index) is { HasTime: true } r ? r.Rank : -1;

    public IReadOnlyList<GpxScreen> Dispatch(GpxContext c)
    {
        if (c.Previewing) return Preview(c) is { } p ? [p] : [];
        if (c.Options.ShowMode == "none") return [];
        var screens = new List<GpxScreen>();
        // Pit timer precedes both the clock and sky messages, even in sky-only mode.
        if (c.IsRace && c.Focus is { InPitLane: true } f) return [PitTimer(c, f, PitSeconds(c)) with { Priority = 120 }];
        if (c.ShowSky && Sky(c) is { } sky) screens.Add(sky);
        if (ExclusiveClock)
        {
            if (!c.ShowAll) return screens;
            if (c.Options.ShowTimeLeft && Clock(c) is { } clock) { screens.Add(clock); return screens; }
        }
        else if (c.Options.ShowTimeLeft && Clock(c) is { } clock) screens.Add(clock);
        if (!c.ShowAll) return screens;
        GpxScreen? main = null;
        if (c.IsRace)
        {
            if (c.Options.DirectorsCut) main = DirectorsCut(c);
            else if (c.Options.Rotation) main = ConfiguredSplit(c) ?? LapStandings(c);
            else if (c.Options.RaceStandings is "standings" or "pitstops") main = Standings(c, c.Options.RaceStandings == "pitstops" ? "stp" : "str");
            else main = ConfiguredSplit(c);
        }
        // A parcial/resultado da volta (janela SplitSeconds) tem prioridade sobre a tabela; fora da janela a tabela volta.
        else if (c.Options.SpeedSplitQuali != "trap" && QualiSplitWindow(c) is { } liveSplit) main = liveSplit;
        else if (c.Options.QualiStandings && c.Model.Quali?.Rows.Any(r => r.HasTime) == true) main = Standings(c, "stq");
        else if (c.Options.SpeedSplitQuali == "trap") main = c.Split.Trap is { } t && t.ActiveAt(c.Now) ? Trap(c, t) : null;
        else main = QualiNow(c);
        if (main is not null) screens.Add(main);
        return screens;
    }

    GpxScreen? ConfiguredSplit(GpxContext c) => c.Options.SpeedSplitRace switch
    {
        "trap" => c.Split.Trap is { } t && t.ActiveAt(c.Now) ? Trap(c, t) : null,
        "behind" => c.Split.Behind is { } b && b.ActiveAt(c.Now) ? Gap(c, b) : null,
        _ => c.Split.Ahead is { } a && a.ActiveAt(c.Now) ? Gap(c, a) : null,
    };

    /// <summary>Standings da corrida orientados a evento: so na janela aberta quando o lider fecha a volta (estaticos, uma passagem por pagina); fora dela, slot vazio.</summary>
    GpxScreen? LapStandings(GpxContext c)
    {
        string mode = c.Options.RaceStandings == "pitstops" ? "stp" : "str";
        int pages = GpxRotation.PagesOf(Rows(c, mode).Count, CarsPerPage);
        return GpxRotation.EventPage(c.Split.LeaderLapT, c.Now, c.Options.StandingsPageSeconds, pages) is { } page ? Standings(c, mode, page) : null;
    }

    GpxScreen? Sky(GpxContext c)
    {
        if (c.IsRace && c.Broadcast.Winner is { } w && c.Now - w.FinishedT >= 0 && c.Now - w.FinishedT < c.Options.SkySeconds)
            return Winner(c, w) with { Priority = 110 };
        if (c.Sky.Active(c.Now) is not { } e) return null;
        return (e.Kind switch
        {
            GpxSkyKind.FastestLap => Fastest(c, e.Car, e.LapTime),
            GpxSkyKind.OutOfRace => OutOfRace(c, e.Car),
            GpxSkyKind.InPits => c.Focus?.Index == e.Car.Index ? PitTimer(c, e.Car, PitSeconds(c)) : InPits(c, e.Car),
            _ => Riding(c, e.Car),
        }) with { Priority = 100 };
    }

    int _directorPass = int.MinValue;
    double _directorStart;
    bool _directorFinished;
    string? _session;
    GpxScreen? DirectorsCut(GpxContext c)
    {
        string session = c.Session.Track + ":" + c.Session.Kind;
        if (_session != session || c.Now < _directorStart) { _session = session; _directorPass = int.MinValue; _directorFinished = false; }
        int pass = c.Focus is { } f ? f.LapsCompleted * 3 + f.Sector : -1;
        // Tracker split 0 is the finish line (the original binary calls it split 2).
        if (pass != _directorPass) { _directorPass = pass; _directorStart = c.Now; _directorFinished = false; }
        if (pass >= 3 && pass % 3 == 0 && !_directorFinished)
        {
            int pages = Math.Max(1, (Math.Min(22, c.Session.Cars.Count) + CarsPerPage - 1) / CarsPerPage);
            int page = (int)((c.Now - _directorStart) / 6);
            if (page < pages)
            {
                int lap = c.Session.Cars.FirstOrDefault(x => x.Position == 1)?.LapsCompleted ?? 0;
                return Standings(c, lap % 5 == 0 ? "stp" : "str", page);
            }
            _directorFinished = true;
        }
        var a = c.Split.Ahead is { } ga && ga.ActiveAt(c.Now) ? ga : null;
        var b = c.Split.Behind is { } gb && gb.ActiveAt(c.Now) ? gb : null;
        var t = c.Split.Trap is { } tr && tr.ActiveAt(c.Now) ? tr : null;
        if ((uint)(pass * 2654435761u) % 10 < 3) return t is null ? null : Trap(c, t);
        if (a is not null && b is not null) return Gap(c, (b.Gap ?? b.Running(c.Now)) < (a.Gap ?? double.MaxValue) ? b : a);
        return a is not null ? Gap(c, a) : b is not null ? Gap(c, b) : null;
    }

    GpxScreen? QualiNow(GpxContext c)
    {
        if (c.Focus is not { } f || f.InPitLane || f.InGarage) return null;
        var q = c.Model.QualiLap;
        if (q is null || q.CarIndex != f.Index || q.OutLap) return Card(c, f);
        return QualiSplitWindow(c);
    }

    /// <summary>Parcial ou resultado da volta do carro em foco dentro da janela SplitSeconds; senao null.</summary>
    GpxScreen? QualiSplitWindow(GpxContext c)
    {
        if (c.Focus is not { } f || f.InPitLane || f.InGarage) return null;
        var q = c.Model.QualiLap;
        if (q is null || q.CarIndex != f.Index || q.OutLap) return null;
        var sp = q.LastSplit is { } s && c.Now >= s.At && c.Now - s.At < c.Options.SplitSeconds ? s : null;
        var lr = q.LastResult is { } r && c.Now >= r.At && c.Now - r.At < c.Options.SplitSeconds ? r : null;
        if (lr is not null && (sp is null || lr.At >= sp.At)) return Split(c, f, GpxFormat.FinishLine, lr.LapTime, lr.Invalid ? null : lr.GapToFirst, lr.Position);
        if (sp is not null) return Split(c, f, GpxFormat.Intermediate(sp.Sector), sp.Elapsed, sp.DeltaLeader, QualiPosition(c, f));
        return null;
    }

    protected sealed record Row(CarSnapshot Car, int? Position, string Value);
    protected static IReadOnlyList<Row> Rows(GpxContext c, string kind)
    {
        if (kind == "stq") return (c.Model.Quali ?? QualiTable.Build(c.Session)).Rows.Take(22).Select(r => new Row(r.Car, r.HasTime ? r.Rank : null,
            !r.HasTime ? "" : r.Rank == 1 ? GpxFormat.LapTime(r.BestLap) : GpxFormat.SignedGap(r.GapToFirst ?? 0))).ToList();
        var race = c.Model.Standings.Count > 0 ? c.Model.Standings : c.Session.Cars.Where(x => x.Position > 0).OrderBy(x => x.Position).Select(x => new StandingRow(x, null, 0, x.IsPlayer)).ToList();
        return race.Take(22).Select(r => new Row(r.Car, Out(r.Car) ? null : r.Car.Position,
            (kind == "stp" ? GpxFormat.Stops(c.Broadcast.StopsOf(r.Car.Index)) : r.Car.Position == 1 ? GpxFormat.Lap(Math.Max(1, r.Car.CurrentLap))
            : r.LapsBehind > 0 ? GpxFormat.LapsBehind(r.LapsBehind) : r.GapToLeader is { } g ? GpxFormat.LapTime(g) : "") + (Out(r.Car) ? GpxFormat.Out : ""))).ToList();
    }

    GpxScreen? Preview(GpxContext c)
    {
        var cars = c.Session.Cars.OrderBy(x => x.Position).ToList();
        if (cars.Count == 0) return null;
        CarSnapshot At(int p) => cars[Math.Clamp(p - 1, 0, cars.Count - 1)];
        var f = c.Focus ?? cars[0];
        return c.Options.PreviewScreen!.ToLowerInvariant() switch
        {
            "time" => Clock(c, true), "fl" => Fastest(c, At(1), At(1).BestLapTime > 0 ? At(1).BestLapTime : 81.443),
            "out" => OutOfRace(c, At(2)), "pit" => InPits(c, At(3)), "pitt" => PitTimer(c, f, 12.3), "dt" => Penalty(c, f),
            "ride" => Riding(c, f), "card" => Card(c, f),
            "win" => Winner(c, c.Broadcast.Winner ?? new(At(1), 5534.532, 305.28, 198.721, c.Now)),
            "split" => Split(c, f, GpxFormat.Intermediate(1), 28.112, .204, 4), "splitgreen" => Split(c, f, GpxFormat.FinishLine, 80.874, -.112, 1),
            "gapa" => Gap(c, new(true, At(3) with { Position = 3 }, At(2) with { Position = 2 }, 7, c.Now - 1, c.Now + 7, 1.842, 0, -.12)),
            "gapb" => Gap(c, new(false, At(3) with { Position = 3 }, At(4) with { Position = 4 }, 7, c.Now - .9, c.Now + 7, null, 0, null)),
            "trap" => Trap(c, new(f, 0, 298.4 / 3.6, 305.1 / 3.6, At(1), 312.7 / 3.6, c.Now - 1, c.Now + 7)),
            "str" or "stq" or "stp" => Standings(c, c.Options.PreviewScreen.ToLowerInvariant()), _ => null,
        };
    }

    protected abstract GpxScreen? Clock(GpxContext c, bool force = false);
    protected abstract GpxScreen Fastest(GpxContext c, CarSnapshot car, double lap);
    protected abstract GpxScreen OutOfRace(GpxContext c, CarSnapshot car);
    protected abstract GpxScreen InPits(GpxContext c, CarSnapshot car);
    protected abstract GpxScreen PitTimer(GpxContext c, CarSnapshot car, double seconds);
    protected abstract GpxScreen Penalty(GpxContext c, CarSnapshot car);
    protected abstract GpxScreen Winner(GpxContext c, WinnerInfo winner);
    protected abstract GpxScreen Riding(GpxContext c, CarSnapshot car);
    protected abstract GpxScreen Card(GpxContext c, CarSnapshot car);
    protected abstract GpxScreen Split(GpxContext c, CarSnapshot car, string label, double? time, double? delta, int position);
    protected abstract GpxScreen Gap(GpxContext c, GpxGapWindow window);
    protected abstract GpxScreen Trap(GpxContext c, GpxTrapWindow window);
    protected abstract GpxScreen Standings(GpxContext c, string kind, int? page = null);
}
