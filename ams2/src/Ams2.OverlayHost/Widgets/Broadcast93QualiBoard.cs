using System.Globalization;
using Ams2.Core.Calc;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Theme;
using Ams2.Shared.Profiles;

namespace Ams2.OverlayHost.Widgets;

/// <summary>1993 lower plate: current driver left, retained reference right, cyan comparison.</summary>
public sealed class Broadcast93QualiBoard
{
    public const float Width = 1920, Height = 300;
    Ams2.Core.CarSnapshot? _reference;
    int _generation = -1, _player = -1;
    string? _track;
    double _lastNow = double.NegativeInfinity;
    public sealed record View(QualiLapState Lap, QualiSplit? Split, QualiLapResult? Result,
        double? ReferenceTime, double? Delta, int ReferenceIndex, bool Personal);

    public static View? Resolve(OverlayModel m, WidgetSettings cfg)
    {
        if (!Broadcast93RaceBoard.Visible(m) || m.QualiLap is not { CarIndex: >= 0 } q) return null;
        bool personal = string.Equals(cfg.OptionOr("compareTo", "leader"), "personal", StringComparison.OrdinalIgnoreCase);
        double hold = double.TryParse(cfg.OptionOr("showFor", "6"), NumberStyles.Float, CultureInfo.InvariantCulture, out var h) && double.IsFinite(h) ? Math.Clamp(h, 3, 15) : 6;
        var result = q.LastResult is { } r && m.Now - r.At is >= 0 && m.Now - r.At < hold ? r : null;
        bool running = !q.InPit && !q.OutLap && q.Elapsed is { } elapsed && double.IsFinite(elapsed) && elapsed >= 0;
        if (result is null && !running) return null;
        var split = result is null && q.LastSplit is { } sp && m.Now - sp.At is >= 0 and < QualiLapWidget.SplitHold ? sp : null;
        double? delta = result is not null ? personal ? result.DeltaPersonal : result.GapToFirst
            : split is not null ? personal ? split.DeltaPersonal : split.DeltaLeader : null;
        double? reference = result is not null ? delta is { } d ? result.LapTime - d : null
            : split is not null ? QualiBoardTiming.ReferenceTime(q, split.Sector, personal, split)
            : personal ? q.PersonalBestLap : q.LeaderBestLap;
        if (reference is not { } time || !double.IsFinite(time) || time <= 0) reference = null;
        if (delta is { } diff && !double.IsFinite(diff)) delta = null;
        return new(q, split, result, reference, delta, personal ? q.CarIndex : q.LeaderIndex, personal);
    }

    public static string Time(double seconds, int digits = 3)
    {
        if (!double.IsFinite(seconds) || seconds < 0) return "--";
        double factor = Math.Pow(10, digits);
        double rounded = Math.Round(seconds * factor, MidpointRounding.AwayFromZero) / factor;
        int minutes = (int)(rounded / 60);
        return minutes > 0 ? minutes.ToString(CultureInfo.InvariantCulture) + "'" + (rounded - minutes * 60).ToString("00." + new string('0', digits), CultureInfo.InvariantCulture)
            : rounded.ToString("0." + new string('0', digits), CultureInfo.InvariantCulture);
    }

    public void Draw(ThemeCanvas c, OverlayModel m, WidgetSettings cfg)
    {
        if (!m.Connected || m.Session is { GameState: 3 })
        { _reference = null; _generation = _player = -1; _track = null; _lastNow = double.NegativeInfinity; }
        if (!Broadcast93RaceBoard.Visible(m)) return;
        var q = m.QualiLap;
        if (q is not null && (_generation != q.SessionGeneration || _player != q.CarIndex || _track != m.Session!.Track || m.Now < _lastNow))
            _reference = null;
        _generation = q?.SessionGeneration ?? -1; _player = q?.CarIndex ?? -1; _track = m.Session!.Track; _lastNow = m.Now;
        if (Resolve(m, cfg) is not { } v)
        {
            if (m.QualiLap is { InPit: false, OutLap: true }) Broadcast93RaceBoard.Caption(c, m, cfg, qualifying: true);
            return;
        }
        var player = m.Session!.PlayerCar!;
        Broadcast93RaceBoard.Band(c, 30, 195);
        Broadcast93RaceBoard.Text(c, cfg.Name(player, BroadcastUi.ShortName(player, m.Session.Cars)).ToUpperInvariant(), 165, 42, 590, "name", 38, c.Theme.TextColor);
        string clock = v.Result is { } finish ? Time(finish.LapTime) : v.Lap.Elapsed is { } live ? Time(live, 1) : "--";
        if (cfg.Fmt.LapTime is not null) clock = cfg.Fmt.FormatLapTime(v.Result?.LapTime ?? v.Lap.Elapsed ?? 0);
        Broadcast93RaceBoard.Text(c, clock, 210, 125, 500, "time", 50, c.Theme.ValueColor);
        if (v.Result is { Position: > 0 } result)
            Broadcast93RaceBoard.Text(c, "(" + result.Position.ToString(CultureInfo.InvariantCulture) + ")", 770, 42, 380, "position", 40, c.Theme.ReadoutColor, HAlign.Center);
        if (v.ReferenceTime is { } reference)
        {
            var refCar = m.Session.Cars.FirstOrDefault(car => car.Index == v.ReferenceIndex);
            // The result delta belongs to the preceding reference, even when this lap takes pole.
            if (v.Result is null) _reference = refCar;
            else if (!v.Personal && v.Result.ReferenceCar is { } resultReference) refCar = resultReference;
            else if (_reference is not null && Math.Abs(_reference.BestLapTime - reference) < .001) refCar = _reference;
            else if (!v.Personal) refCar = m.Session.Cars.FirstOrDefault(car => !car.IsPlayer && Math.Abs(car.BestLapTime - reference) < .001);
            else if (!v.Personal && refCar?.IsPlayer == true) refCar = null;
            string name = refCar is not null ? cfg.Name(refCar, BroadcastUi.ShortName(refCar, m.Session.Cars)).ToUpperInvariant() : v.Personal ? "PERSONAL BEST" : "REFERENCE";
            Broadcast93RaceBoard.Text(c, name, 1220, 42, 535, "name", 38, c.Theme.TextColor);
            Broadcast93RaceBoard.Text(c, cfg.Fmt.LapTime is null ? Time(reference) : cfg.Fmt.FormatLapTime(reference), 1220, 125, 535, "time", 50, c.Theme.ValueColor);
        }
        string? comparison = v.Result?.Invalid == true ? "INVALID" : v.Delta is { } delta ? cfg.Fmt.FormatGap(delta) : null;
        if (comparison is not null)
            Broadcast93RaceBoard.Text(c, comparison, 765, 125, 390, "gap", 50, c.Theme.ReadoutColor, HAlign.Center);
        if (v.Split is { } partial)
            Broadcast93RaceBoard.Text(c, cfg.Fmt.LapTime is null ? Time(partial.Elapsed) : cfg.Fmt.FormatLapTime(partial.Elapsed), 710, 230, 500, "time", 50, c.Theme.ValueColor, HAlign.Center);
    }
}
