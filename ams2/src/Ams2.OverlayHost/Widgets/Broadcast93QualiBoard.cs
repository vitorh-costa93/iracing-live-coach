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
    readonly FastestLapDetector _fastest = new();
    int _generation = -1, _player = -1;
    string? _track;
    double _lastNow = double.NegativeInfinity;
    readonly ShowRef _show = new();
    public sealed record View(QualiLapState Lap, QualiSplit? Split, QualiLapResult? Result,
        double? ReferenceTime, double? Delta, int ReferenceIndex, bool Personal);

    public static View? Resolve(OverlayModel m, WidgetSettings cfg, ShowPlan? show = null)
    {
        if (!Broadcast93RaceBoard.Visible(m) || m.QualiLap is not { CarIndex: >= 0 } q) return null;
        bool personal = string.Equals(cfg.OptionOr("compareTo", "leader"), "personal", StringComparison.OrdinalIgnoreCase);
        show ??= ShowPlan.For(cfg, "f1-1993");
        double hold = ResultHold(show);
        double splitHold = show.PieceEnabled("split") ? show.PieceDuration("split", QualiLapWidget.SplitHold) : 0;
        var result = q.LastResult is { } r && m.Now - r.At >= 0 && m.Now - r.At < hold ? r : null;
        bool running = !q.InPit && !q.OutLap && q.Elapsed is { } elapsed && double.IsFinite(elapsed) && elapsed >= 0;
        if (result is null && !running) return null;
        var split = result is null && q.LastSplit is { } sp && m.Now - sp.At >= 0 && m.Now - sp.At < splitHold ? sp : null;
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
        { _reference = null; _generation = _player = -1; _track = null; _lastNow = double.NegativeInfinity; _fastest.Reset(); }
        if (!Broadcast93RaceBoard.Visible(m)) return;
        var q = m.QualiLap;
        if (q is not null && (_generation != q.SessionGeneration || _player != q.CarIndex || _track != m.Session!.Track || m.Now < _lastNow))
            _reference = null;
        _generation = q?.SessionGeneration ?? -1; _player = q?.CarIndex ?? -1; _track = m.Session!.Track; _lastNow = m.Now;
        _fastest.Update(m.Now, m.Session, _generation);

        // A (resultado da volta) > B (fastest lap) > C (legenda do piloto, padrao).
        // Pecas (Exibicao): cada uma liga/desliga e tem o proprio tempo de tela; o padrao e o board de sempre.
        var show = _show.Get(cfg, ThemeStyle.Broadcast93);
        var view = Resolve(m, cfg, show);
        // Durante a volta lancada guarda o lider anterior: se esta volta tomar a pole, o delta continua contra ele.
        if (view is { Result: null }) _reference = m.Session.Cars.FirstOrDefault(car => car.Index == view.ReferenceIndex);
        if (view?.Result is not null) { LapResult(c, m, cfg, view); return; }
        if (show.PieceEnabled("fastest") && _fastest.Latest is { } fastest)
        {
            // A propria volta que bateu o recorde primeiro mostra o resultado (A); a placa de FASTEST LAP vem logo depois.
            double notBefore = q?.LastResult is { } last && Math.Abs(last.At - fastest.At) < 3 ? last.At + ResultHold(show) : double.NegativeInfinity;
            if (fastest.ActiveAt(m.Now, show.PieceDuration("fastest", FastestLapDetector.DefaultHold), notBefore))
            { Broadcast93RaceBoard.Fastest(c, fastest.Car, m.Session.Cars, cfg, m.Session.TrackLength); return; }
        }
        // Mesma cadencia do Qualy Board 1999 (QualiBoard99Presentation): out lap, relogio, aproximacao (5 s), intermediaria (3 s).
        var live = Broadcast99QualiBoard.Resolve(m, cfg, show);
        if (show.PieceEnabled("live") && live.Stage is QualiBoard99Stage.Running or QualiBoard99Stage.Approach or QualiBoard99Stage.Split) { Live(c, m, cfg, live); return; }
        if (!show.PieceEnabled("caption")) return;
        Broadcast93RaceBoard.Caption(c, m, cfg, qualifying: true);
        if (live.Stage == QualiBoard99Stage.OutLap)
            Broadcast93RaceBoard.Text(c, "FIRST LAP OUT OF PITS", 880, 68, 400, "label", 34, Broadcast93RaceBoard.Yellow, HAlign.Right);
    }

    /// <summary>Relogio a esquerda; referencia a direita na aproximacao; na intermediaria, delta ciano e "INTERMEDIATE n" no centro.</summary>
    static void Live(ThemeCanvas c, OverlayModel m, WidgetSettings cfg, QualiBoard99Presentation.View v)
    {
        var player = m.Session!.PlayerCar!;
        bool crossed = v.Stage == QualiBoard99Stage.Split;
        Broadcast93RaceBoard.Band(c, 30, 195);
        Broadcast93RaceBoard.Text(c, cfg.Name(player, BroadcastUi.ShortName(player, m.Session.Cars)).ToUpperInvariant(), 165, 42, 590, "name", 38, c.Theme.TextColor);
        if (v.Time is { } elapsed)
            Broadcast93RaceBoard.Text(c, crossed && cfg.Fmt.LapTime is null ? Time(elapsed) : cfg.Fmt.LapTime is not null ? cfg.Fmt.FormatLapTime(elapsed) : Tenths(elapsed), 210, 125, 500, "time", 50, c.Theme.ValueColor);
        if (v.Stage == QualiBoard99Stage.Running) return;
        bool personal = string.Equals(cfg.OptionOr("compareTo", "leader"), "personal", StringComparison.OrdinalIgnoreCase);
        var refCar = v.ReferenceCar ?? m.Session.Cars.FirstOrDefault(car => car.Index == (personal ? player.Index : m.QualiLap!.LeaderIndex));
        string name = refCar is not null ? cfg.Name(refCar, BroadcastUi.ShortName(refCar, m.Session.Cars)).ToUpperInvariant() : personal ? "PERSONAL BEST" : "REFERENCE";
        Broadcast93RaceBoard.Text(c, name, 1220, 42, 535, "name", 38, c.Theme.TextColor);
        if (v.ReferenceTime is { } reference)
            Broadcast93RaceBoard.Text(c, cfg.Fmt.LapTime is null ? Time(reference) : cfg.Fmt.FormatLapTime(reference), 1220, 125, 535, "time", 50, c.Theme.ValueColor);
        if (!crossed) return;
        if (v.Delta is { } delta) Broadcast93RaceBoard.Text(c, cfg.Fmt.FormatGap(delta), 765, 125, 390, "gap", 50, Broadcast93RaceBoard.Cyan, HAlign.Center);
        Broadcast93RaceBoard.Text(c, "INTERMEDIATE " + v.Mark.ToString(CultureInfo.InvariantCulture), 765, 185, 390, "label", 34, Broadcast93RaceBoard.Yellow, HAlign.Center);
    }

    /// <summary>Relogio em andamento com uma casa (44.9; 1'07.9), como o cronometro da transmissao.</summary>
    static string Tenths(double seconds) => Time(Math.Floor(seconds * 10) / 10, 1);

    static double ResultHold(ShowPlan show) => show.PieceEnabled("result") ? show.PieceDuration("result", 6) : 0;

    /// <summary>A: "SOBRENOME 1'12.266 | (1) azul | -0.267 ciano | SOBRENOME 1'12.533"; volta invalida mostra INVALID no lugar do delta.</summary>
    void LapResult(ThemeCanvas c, OverlayModel m, WidgetSettings cfg, View v)
    {
        var player = m.Session!.PlayerCar!;
        var result = v.Result!;
        Broadcast93RaceBoard.Band(c, 30, 195);
        Broadcast93RaceBoard.Text(c, cfg.Name(player, BroadcastUi.ShortName(player, m.Session.Cars)).ToUpperInvariant(), 165, 42, 590, "name", 38, c.Theme.TextColor);
        string clock = cfg.Fmt.LapTime is not null ? cfg.Fmt.FormatLapTime(result.LapTime) : Time(result.LapTime);
        Broadcast93RaceBoard.Text(c, clock, 210, 125, 500, "time", 50, c.Theme.ValueColor);
        if (result.Position > 0)
            Broadcast93RaceBoard.Text(c, "(" + result.Position.ToString(CultureInfo.InvariantCulture) + ")", 770, 42, 380, "position", 44, Broadcast93RaceBoard.Blue, HAlign.Center);
        if (v.ReferenceTime is { } reference)
        {
            var refCar = m.Session.Cars.FirstOrDefault(car => car.Index == v.ReferenceIndex);
            // The result delta belongs to the preceding reference, even when this lap takes pole.
            if (!v.Personal && result.ReferenceCar is { } resultReference) refCar = resultReference;
            else if (_reference is not null && Math.Abs(_reference.BestLapTime - reference) < .001) refCar = _reference;
            else if (!v.Personal) refCar = m.Session.Cars.FirstOrDefault(car => !car.IsPlayer && Math.Abs(car.BestLapTime - reference) < .001);
            string name = refCar is not null ? cfg.Name(refCar, BroadcastUi.ShortName(refCar, m.Session.Cars)).ToUpperInvariant() : v.Personal ? "PERSONAL BEST" : "REFERENCE";
            Broadcast93RaceBoard.Text(c, name, 1220, 42, 535, "name", 38, c.Theme.TextColor);
            Broadcast93RaceBoard.Text(c, cfg.Fmt.LapTime is null ? Time(reference) : cfg.Fmt.FormatLapTime(reference), 1220, 125, 535, "time", 50, c.Theme.ValueColor);
        }
        string? comparison = result.Invalid ? "INVALID" : v.Delta is { } delta ? cfg.Fmt.FormatGap(delta) : null;
        if (comparison is not null)
            Broadcast93RaceBoard.Text(c, comparison, 765, 125, 390, "gap", 50, Broadcast93RaceBoard.Cyan, HAlign.Center);
    }
}
