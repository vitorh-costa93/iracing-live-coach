using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace IracingLiveCoach.Core.Coach;

/// <summary>One car as seen by the pit-rejoin prediction. Progress = LapCompleted + LapDistPct (laps).
/// RemainingLossSec = pit time this car still has to lose (cars already on pit road), 0 for cars racing.
/// LapTimeSec = reference lap of the car's class (CarClassEstLapTime); 0 = same as the player's.</summary>
public sealed record RejoinCar(int CarIdx, string Number, string Name, int ClassId, int LapCompleted, double LapDistPct,
    bool OnPitRoad = false, double RemainingLossSec = 0, string ClassName = "", string? ClassColorHex = null,
    int ClassRank = 0, double LapTimeSec = 0)
{
    public double Progress => LapCompleted + LapDistPct;
}

/// <summary>Car next to the player at the pit exit. GapSec is always positive (seconds of track between the
/// two cars: ahead = at the player's reference lap, behind = at that car's class reference lap, i.e. how long
/// until it gets to the player). Laps: race order = whole laps between the two (1 = a lap up/down); on-track
/// traffic = laps the car is up (+) or down (-) on the player. Faster = its class laps quicker than the player's.</summary>
public sealed record RejoinNeighbour(int CarIdx, string Number, string Name, double GapSec, int Laps, bool InPit,
    int ClassId = 0, string ClassName = "", string? ClassColorHex = null, int ClassRank = 0, bool Faster = false);

/// <summary>Closest car of another class ahead / behind on track at the pit exit (multiclass).</summary>
public sealed record RejoinClassTraffic(int ClassId, string ClassName, string? ClassColorHex, int ClassRank, bool Faster,
    RejoinNeighbour? Ahead, RejoinNeighbour? Behind);

/// <summary>"If you pit now" (or, on pit road, "where you are coming out"). RaceOrder: positions are the
/// race order (class and overall, race); false = only the traffic on track around the exit (practice).</summary>
public sealed record PitRejoinPrediction
{
    public bool Available { get; init; }
    /// <summary>Why it is unavailable (or empty).</summary>
    public string Reason { get; init; } = "";
    public bool RaceOrder { get; init; }
    /// <summary>Position in the player's class now / at the exit.</summary>
    public int CurrentPosition { get; init; }
    public int PredictedPosition { get; init; }
    /// <summary>Overall position (all classes, by lap progress) now / at the exit.</summary>
    public int OverallCurrentPosition { get; init; }
    public int OverallPredictedPosition { get; init; }
    /// <summary>Same-class neighbours at the exit (race order), or the closest cars on track (practice).</summary>
    public RejoinNeighbour? Ahead { get; init; }
    public RejoinNeighbour? Behind { get; init; }
    /// <summary>More than one class in the field.</summary>
    public bool MultiClass { get; init; }
    public int ClassId { get; init; }
    public string ClassName { get; init; } = "";
    public string? ClassColorHex { get; init; }
    public int ClassRank { get; init; }
    /// <summary>Other classes, nearest on track first.</summary>
    public IReadOnlyList<RejoinClassTraffic> OtherClasses { get; init; } = Array.Empty<RejoinClassTraffic>();
    /// <summary>Loss used for the projection (remaining loss when already on pit road).</summary>
    public double LossSec { get; init; } = double.NaN;
    public double LaneLossSec { get; init; } = double.NaN;
    public double ServiceSec { get; init; } = double.NaN;
    public double FuelToAddL { get; init; } = double.NaN;
    /// <summary>Some part of the loss is not learned from real stops of this session (see Note).</summary>
    public bool Estimated { get; init; }
    public string Note { get; init; } = "";
    /// <summary>The player is already on pit road (the loss is what is still left of the stop).</summary>
    public bool InPit { get; init; }

    /// <summary>Nearest faster-class car coming from behind at the exit.</summary>
    public RejoinNeighbour? FasterBehind => OtherClasses.Where(c => c.Faster && c.Behind != null)
        .Select(c => c.Behind!).OrderBy(n => n.GapSec).FirstOrDefault();
    /// <summary>Nearest slower-class car on track ahead at the exit (traffic).</summary>
    public RejoinNeighbour? SlowerAhead => OtherClasses.Where(c => !c.Faster && c.Ahead != null)
        .Select(c => c.Ahead!).OrderBy(n => n.GapSec).FirstOrDefault();

    public static PitRejoinPrediction Unavailable(string reason) => new() { Available = false, Reason = reason };
}

/// <summary>Pit time loss split in pit-lane loss (vs racing past the pit lane) and stationary service.</summary>
public sealed record PitLossEstimate(bool Available, double LaneLossSec, double ServiceSec, bool Estimated, string Note)
{
    public double TotalSec => LaneLossSec + ServiceSec;
    public static PitLossEstimate None(string why) => new(false, double.NaN, double.NaN, true, why);
}

/// <summary>Pure arithmetic of the pit-rejoin prediction (unit-tested with exact numbers).
///
/// Formula: the player loses L seconds; meanwhile every other car keeps racing at its class reference lap,
/// so at the moment the player rejoins car i is at p_i + (L - r_i) / lap_i (lap progress; r_i = what is left
/// of its own pit-lane loss when it is on pit road) while the player is at p (its progress now). Same class:
/// lap_i = lap_player, identical to shifting the player back by L / lap. Class position = 1 + cars of the
/// class with a larger projected progress; overall = same over every class. Same-class neighbours: closest
/// projected cars, gap = progress difference x player's reference lap (same basis as the Relative/engineer
/// gaps). Other classes (and practice): closest cars on track by wrapped lap fraction; ahead gap at the
/// player's lap, behind gap at that car's class lap. Assumes the relative pace holds from now to the exit
/// (the time to drive to the pit entry is not modelled).</summary>
public static class PitRejoinPredictor
{
    /// <summary>A class counts as faster when its reference lap is this much quicker.</summary>
    public const double FasterClassRatio = 0.995;

    public static PitRejoinPrediction Predict(RejoinCar? player, IReadOnlyList<RejoinCar>? field, double lapTimeSec,
        double playerLossSec, bool raceOrder)
    {
        try
        {
            if (player == null) return PitRejoinPrediction.Unavailable("no player car");
            if (!(lapTimeSec > 1) || double.IsInfinity(lapTimeSec)) return PitRejoinPrediction.Unavailable("no reference lap time");
            if (!(playerLossSec >= 0) || double.IsInfinity(playerLossSec)) return PitRejoinPrediction.Unavailable("no pit loss");
            if (!InWorld(player)) return PitRejoinPrediction.Unavailable("player not on track");

            double me = player.Progress;
            var rivals = new List<(RejoinCar Car, double Now, double Proj, double Lap)>();
            foreach (var c in field ?? Array.Empty<RejoinCar>())
            {
                if (c == null || c.CarIdx == player.CarIdx || !InWorld(c)) continue;
                double lap = c.ClassId == player.ClassId || !(c.LapTimeSec > 1) || double.IsInfinity(c.LapTimeSec) ? lapTimeSec : c.LapTimeSec;
                double r = c.RemainingLossSec > 0 && !double.IsInfinity(c.RemainingLossSec) ? c.RemainingLossSec : 0;
                rivals.Add((c, c.Progress, c.Progress + (playerLossSec - r) / lap, lap));
            }

            RejoinNeighbour? ahead = null, behind = null;
            int cur = 0, pred = 0, curAll = 0, predAll = 0;
            if (raceOrder)
            {
                var mine = rivals.Where(x => x.Car.ClassId == player.ClassId).ToList();
                cur = 1 + mine.Count(x => x.Now > me);
                pred = 1 + mine.Count(x => x.Proj > me);
                curAll = 1 + rivals.Count(x => x.Now > me);
                predAll = 1 + rivals.Count(x => x.Proj > me);
                var a = mine.Where(x => x.Proj > me).OrderBy(x => x.Proj).ThenBy(x => x.Car.CarIdx).FirstOrDefault();
                var b = mine.Where(x => x.Proj <= me).OrderByDescending(x => x.Proj).ThenBy(x => x.Car.CarIdx).FirstOrDefault();
                if (a.Car != null) ahead = Neighbour(a.Car, (a.Proj - me) * lapTimeSec, (int)Math.Floor(a.Proj - me + 1e-9), false);
                if (b.Car != null) behind = Neighbour(b.Car, (me - b.Proj) * lapTimeSec, (int)Math.Floor(me - b.Proj + 1e-9), false);
            }
            else
            {
                var (a, b) = Nearest(rivals, me, lapTimeSec, false);
                ahead = a; behind = b;
            }

            var others = new List<RejoinClassTraffic>();
            foreach (var g in rivals.Where(x => x.Car.ClassId != player.ClassId).GroupBy(x => x.Car.ClassId))
            {
                var first = g.First();
                bool faster = first.Lap < lapTimeSec * FasterClassRatio;
                var (a, b) = Nearest(g.ToList(), me, lapTimeSec, faster);
                others.Add(new RejoinClassTraffic(g.Key, first.Car.ClassName ?? "", first.Car.ClassColorHex, first.Car.ClassRank, faster, a, b));
            }
            others.Sort((x, y) => Closest(x).CompareTo(Closest(y)));

            return new PitRejoinPrediction
            {
                Available = true, RaceOrder = raceOrder, CurrentPosition = cur, PredictedPosition = pred,
                OverallCurrentPosition = curAll, OverallPredictedPosition = predAll,
                Ahead = ahead, Behind = behind, LossSec = playerLossSec, InPit = player.OnPitRoad,
                MultiClass = others.Count > 0, ClassId = player.ClassId, ClassName = player.ClassName ?? "", ClassColorHex = player.ClassColorHex,
                ClassRank = player.ClassRank, OtherClasses = others,
            };
        }
        catch (Exception ex) { return PitRejoinPrediction.Unavailable("error: " + ex.GetType().Name); }
    }

    private static double Closest(RejoinClassTraffic t) =>
        Math.Min(t.Ahead?.GapSec ?? double.MaxValue, t.Behind?.GapSec ?? double.MaxValue);

    /// <summary>Closest cars on track ahead / behind the player's exit point (wrapped lap fraction).</summary>
    private static (RejoinNeighbour? Ahead, RejoinNeighbour? Behind) Nearest(
        List<(RejoinCar Car, double Now, double Proj, double Lap)> cars, double me, double lapTimeSec, bool faster)
    {
        double mePct = Frac(me);
        (RejoinCar? Car, double D, double Proj, double Lap) a = default, b = default;
        foreach (var x in cars)
        {
            double d = Wrap(Frac(x.Proj) - mePct); // > 0 = ahead on track
            if (d > 0) { if (a.Car == null || d < a.D || (d == a.D && x.Car.CarIdx < a.Car.CarIdx)) a = (x.Car, d, x.Proj, x.Lap); }
            else if (b.Car == null || d > b.D || (d == b.D && x.Car.CarIdx < b.Car.CarIdx)) b = (x.Car, d, x.Proj, x.Lap);
        }
        RejoinNeighbour? ahead = a.Car == null ? null
            : Neighbour(a.Car, a.D * lapTimeSec, (int)Math.Round(a.Proj - me - a.D), faster);
        RejoinNeighbour? behind = b.Car == null ? null
            : Neighbour(b.Car, -b.D * b.Lap, (int)Math.Round(b.Proj - me - b.D), faster);
        return (ahead, behind);
    }

    private static RejoinNeighbour Neighbour(RejoinCar c, double gapSec, int laps, bool faster) =>
        new(c.CarIdx, c.Number ?? "", c.Name ?? "", gapSec, laps, c.OnPitRoad, c.ClassId, c.ClassName ?? "", c.ClassColorHex,
            c.ClassRank, faster);

    private static bool InWorld(RejoinCar c) =>
        !double.IsNaN(c.LapDistPct) && c.LapDistPct >= 0 && c.LapDistPct <= 1.0001 && c.LapCompleted >= -1;

    public static double Frac(double x) => x - Math.Floor(x);

    /// <summary>Lap fraction wrapped to (-0.5, 0.5].</summary>
    public static double Wrap(double d)
    {
        d -= Math.Floor(d);
        return d > 0.5 ? d - 1 : d;
    }

    /// <summary>Lap fraction wrapped to [0, 1).</summary>
    public static double Wrap01(double d) => d - Math.Floor(d);

    /// <summary>"80.00 kph" / "45.00 mph" / "22.2 m/s" (WeekendInfo.TrackPitSpeedLimit) in m/s; NaN when unknown.</summary>
    public static double ParseSpeedMs(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return double.NaN;
        var parts = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || !(v > 0)) return double.NaN;
        string unit = parts.Length > 1 ? parts[1].ToLowerInvariant() : "kph";
        return unit switch
        {
            "kph" or "km/h" or "kmh" => v / 3.6,
            "mph" => v * 0.44704,
            "m/s" => v,
            _ => double.NaN,
        };
    }
}

/// <summary>Learns the pit loss of the current track from real pit-road visits (CarIdxOnPitRoad edges seen at
/// ~6 Hz) and estimates the loss of the player's next stop.
///
/// Lane loss of a visit = (time on pit road - time stationary) - (lap fraction covered x lapRef): what driving
/// through the pit lane costs vs racing past it. Every car's visit counts (drive-throughs too). Stationary time is
/// "LapDistPct did not move between two samples". The player's own stops also give the refuel rate (liters added /
/// stationary s, stops without tyres) and the tyre-change time (tyres without fuel). Pure: no SDK access.</summary>
public sealed class PitLossModel
{
    public const int MaxSamples = 15;
    public const double StillPct = 2e-5;
    public const double MinLaneLossSec = -10, MaxLaneLossSec = 120;
    public const double MinStationarySec = 1.0;

    private sealed class Visit
    {
        public double EnterT, EnterPct, LastT, LastPct, StationarySec, EnterFuel = -1;
        public bool Tires;
    }

    private readonly Dictionary<int, bool> _lastPit = new();
    private readonly Dictionary<int, Visit> _visits = new();
    private readonly List<double> _laneLoss = new(), _otherStops = new(), _fillRate = new(), _tireSec = new();
    private readonly List<double> _entryPct = new(), _exitPct = new();
    private double _lastT = double.NegativeInfinity;

    /// <summary>One line per learned sample (coach-engineer.log).</summary>
    public event Action<string>? Log;

    public int LaneSamples => _laneLoss.Count;
    public double? LearnedLaneLossSec => Median(_laneLoss);
    public double? LearnedFillRateLps => Median(_fillRate);
    public double? LearnedTireSec => Median(_tireSec);
    public double? TypicalStopSec => Median(_otherStops);

    /// <summary>Forgets everything (other track).</summary>
    public void Reset()
    {
        ResetVisits();
        _laneLoss.Clear(); _otherStops.Clear(); _fillRate.Clear(); _tireSec.Clear(); _entryPct.Clear(); _exitPct.Clear();
    }

    /// <summary>Session change / time jump: drops the visits in progress, keeps what was learned.</summary>
    public void ResetVisits() { _lastPit.Clear(); _visits.Clear(); _lastT = double.NegativeInfinity; }

    /// <summary>Call once per refresh with the session time, before the <see cref="Observe"/> calls.</summary>
    public void BeginTick(double t)
    {
        if (t < _lastT - 1.0) ResetVisits();
        _lastT = t;
    }

    /// <summary>Feeds one car. fuelL / tiresRequested only matter for the player (refuel/tyre learning).</summary>
    public void Observe(int carIdx, bool onPitRoad, double lapDistPct, double t, double lapTimeSec,
        bool isPlayer = false, double fuelL = -1, bool tiresRequested = false)
    {
        if (double.IsNaN(lapDistPct) || lapDistPct < 0) { _lastPit.Remove(carIdx); _visits.Remove(carIdx); return; } // not in world
        bool known = _lastPit.TryGetValue(carIdx, out var was);
        _lastPit[carIdx] = onPitRoad;
        if (!known) return;

        if (onPitRoad && !was)
        {
            Add(_entryPct, lapDistPct);
            _visits[carIdx] = new Visit { EnterT = t, EnterPct = lapDistPct, LastT = t, LastPct = lapDistPct,
                EnterFuel = isPlayer ? fuelL : -1, Tires = isPlayer && tiresRequested };
            return;
        }
        if (onPitRoad && _visits.TryGetValue(carIdx, out var v))
        {
            double dt = t - v.LastT;
            if (dt > 0 && dt < 2.0 && Math.Abs(lapDistPct - v.LastPct) < StillPct) v.StationarySec += dt;
            v.LastT = t; v.LastPct = lapDistPct;
            if (isPlayer && tiresRequested) v.Tires = true;
            return;
        }
        if (!onPitRoad && was)
        {
            Add(_exitPct, lapDistPct);
            if (!_visits.Remove(carIdx, out var visit)) return; // left the stall (session start): exit point only
            double total = t - visit.EnterT;
            double frac = PitRejoinPredictor.Wrap01(lapDistPct - visit.EnterPct);
            if (frac >= 0.5 || total < 2 || total > 600 || !(lapTimeSec > 1)) return; // tow/garage/odd sample
            double lane = (total - visit.StationarySec) - frac * lapTimeSec;
            if (lane >= MinLaneLossSec && lane <= MaxLaneLossSec)
            {
                Add(_laneLoss, lane);
                Log?.Invoke(Inv($"pit-loss car={carIdx} road {total:F1} s stationary {visit.StationarySec:F1} s frac {frac:F4} -> lane loss {lane:F2} s (median {LearnedLaneLossSec:F2} over {_laneLoss.Count})"));
            }
            if (visit.StationarySec < MinStationarySec) return;
            if (!isPlayer) { Add(_otherStops, visit.StationarySec); return; }
            double added = fuelL >= 0 && visit.EnterFuel >= 0 ? fuelL - visit.EnterFuel : double.NaN;
            if (added >= 1 && !visit.Tires)
            {
                Add(_fillRate, added / visit.StationarySec);
                Log?.Invoke(Inv($"pit-loss player refuel {added:F1} L in {visit.StationarySec:F1} s -> {added / visit.StationarySec:F3} L/s"));
            }
            else if (visit.Tires && !(added >= 0.5))
            {
                Add(_tireSec, visit.StationarySec);
                Log?.Invoke(Inv($"pit-loss player tyres {visit.StationarySec:F1} s"));
            }
        }
    }

    /// <summary>Visit in progress of a car (entry time/point and stationary time so far).</summary>
    public bool TryGetVisit(int carIdx, out double enterT, out double enterPct, out double stationarySec)
    {
        if (_visits.TryGetValue(carIdx, out var v)) { enterT = v.EnterT; enterPct = v.EnterPct; stationarySec = v.StationarySec; return true; }
        enterT = enterPct = stationarySec = double.NaN;
        return false;
    }

    /// <summary>Time already lost on pit road: elapsed - (lap fraction covered x lapRef) (stationary included).</summary>
    public static double LostSoFar(double enterT, double enterPct, double t, double pct, double lapTimeSec) =>
        (t - enterT) - PitRejoinPredictor.Wrap01(pct - enterPct) * lapTimeSec;

    /// <summary>Pit-lane loss a car still has ahead on its visit (its unknown service time is not counted).
    /// 0 when no visit is being tracked (e.g. it was already in the pits when we connected).</summary>
    public double RemainingLaneLoss(int carIdx, double pct, double t, double lapTimeSec, double laneLossSec)
    {
        if (!(laneLossSec > 0) || !TryGetVisit(carIdx, out var et, out var ep, out var st)) return 0;
        double laneLostSoFar = LostSoFar(et, ep, t, pct, lapTimeSec) - st;
        return Math.Max(0, laneLossSec - laneLostSoFar);
    }

    /// <summary>Pit-lane length as a lap fraction from the observed entry/exit points (circular medians); NaN when unknown.</summary>
    public double LaneFraction()
    {
        double e = CircularMedian(_entryPct), x = CircularMedian(_exitPct);
        if (double.IsNaN(e) || double.IsNaN(x)) return double.NaN;
        double f = PitRejoinPredictor.Wrap01(x - e);
        return f > 0 && f < 0.5 ? f : double.NaN;
    }

    /// <summary>Loss of the player's next stop.
    /// Lane: learned median, else (estimated) pit-lane length / pit speed limit - fraction x lapRef, with the
    /// length = (exit - entry) x track length (assumes the lane runs alongside the track; no accel/decel).
    /// Service: fuel / learned refuel rate (tyres: max with the learned tyre time, they run in parallel) + the
    /// mandatory repair time the SDK reports; without own data the median stationary time of other cars' stops
    /// (estimated); without any data 0 and flagged. fuelToAddL NaN = unknown.</summary>
    public PitLossEstimate Estimate(double fuelToAddL, bool tires, double repairSec, double lapTimeSec,
        double trackLengthM, double pitLimitMs)
    {
        if (!(lapTimeSec > 1)) return PitLossEstimate.None("no reference lap time");
        var notes = new List<string>();
        bool est = false;
        double lane;
        if (LearnedLaneLossSec is double learned) lane = learned;
        else
        {
            double f = LaneFraction();
            if (double.IsNaN(f) || !(trackLengthM > 0) || !(pitLimitMs > 0)) return PitLossEstimate.None("no pit lane data yet");
            lane = Math.Max(0, f * trackLengthM / pitLimitMs - f * lapTimeSec);
            est = true; notes.Add("lane from pit speed limit");
        }

        double service = 0;
        bool fuelUnknown = double.IsNaN(fuelToAddL);
        double fuel = fuelUnknown ? 0 : Math.Max(0, fuelToAddL);
        bool needsService = fuelUnknown || fuel > 0.05 || tires;
        if (needsService)
        {
            double? fuelSec = fuel > 0.05 && LearnedFillRateLps is double r && r > 0 ? fuel / r : fuel <= 0.05 && !fuelUnknown ? 0 : null;
            double? tireSec = tires ? LearnedTireSec : 0;
            if (fuelSec is double fs && tireSec is double ts) service = Math.Max(fs, ts);
            else if (TypicalStopSec is double typical)
            {
                service = Math.Max(typical, Math.Max(fuelSec ?? 0, tireSec ?? 0));
                est = true; notes.Add("service from other cars' stops");
            }
            else
            {
                service = Math.Max(fuelSec ?? 0, tireSec ?? 0);
                est = true;
                if (fuelSec == null) notes.Add(fuelUnknown ? "fuel amount unknown" : "refuel time unknown");
                if (tireSec == null) notes.Add("tyres not included");
            }
        }
        if (repairSec > 0 && !double.IsInfinity(repairSec)) service += repairSec;
        return new PitLossEstimate(true, lane, service, est, string.Join(", ", notes));
    }

    private static void Add(List<double> list, double v)
    {
        list.Add(v);
        if (list.Count > MaxSamples) list.RemoveAt(0);
    }

    private static double? Median(List<double> list)
    {
        if (list.Count == 0) return null;
        var s = list.OrderBy(x => x).ToList();
        int n = s.Count;
        return n % 2 == 1 ? s[n / 2] : (s[n / 2 - 1] + s[n / 2]) / 2.0;
    }

    /// <summary>Median of lap fractions around the first sample (handles points that straddle the line).</summary>
    private static double CircularMedian(List<double> list)
    {
        if (list.Count == 0) return double.NaN;
        double anchor = list[0];
        var offsets = list.Select(p => PitRejoinPredictor.Wrap(p - anchor)).ToList();
        return PitRejoinPredictor.Wrap01(anchor + Median(offsets)!.Value);
    }

    private static string Inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
}
