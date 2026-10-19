using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace IracingLiveCoach.Core.Coach;

/// <summary>Which engineer call families are on (mirrors CoachSettings.EngineerEnabled/FuelCalls/...).</summary>
public sealed class EngineerOptions
{
    public bool Enabled { get; set; } = true;
    public bool Fuel { get; set; } = true;
    public bool Flags { get; set; } = true;
    public bool Gaps { get; set; } = true;
    public bool Pit { get; set; } = true;
    /// <summary>"If you pit now, you rejoin P9..." calls (races).</summary>
    public bool Rejoin { get; set; } = true;
}

/// <summary>Strategy numbers the engineer works with, also used to answer voice questions.
/// NaN / -1 = unknown.</summary>
public sealed record EngineerStatus(double FuelL, double AvgPerLap, double LapsLeft, int LapsToGo,
    double ShortL, int AddL, bool BoxNow, int Position, double GapAheadSec, double GapBehindSec)
{
    public static readonly EngineerStatus Empty = new(-1, double.NaN, double.NaN, -1, double.NaN, -1, false, 0, double.NaN, double.NaN);
    /// <summary>Last pit-rejoin prediction (null = not computed).</summary>
    public PitRejoinPrediction? Rejoin { get; init; }
}

/// <summary>Pure fuel / race-distance arithmetic (kept apart so the numbers can be unit-tested exactly).</summary>
public static class EngineerMath
{
    /// <summary>Reserve added on top of the fuel needed to finish, in laps of average consumption.</summary>
    public const double ReserveLaps = 0.5;
    /// <summary>SessionLapsRemain above this is the "unlimited" sentinel (32767).</summary>
    public const int MaxKnownLaps = 5000;

    /// <summary>Distance left to the checkered flag in laps (1 - lapPct to the next line plus the laps after it).
    /// Uses SessionLapsRemain (counts the current lap) when known, else SessionTimeRemain with the average lap
    /// time (the leader takes the flag at the first line crossing after the clock hits zero). Null if unknown.</summary>
    public static double? DistanceToFinishLaps(int lapsRemain, double timeRemainSec, double lapPct, double avgLapSec)
    {
        double toLine = 1.0 - Math.Clamp(lapPct, 0, 1);
        if (lapsRemain >= 0 && lapsRemain <= MaxKnownLaps)
            return lapsRemain == 0 ? 0 : toLine + (lapsRemain - 1);
        if (timeRemainSec >= 0 && timeRemainSec < 86400 && avgLapSec > 1)
        {
            double toLineSec = toLine * avgLapSec;
            if (timeRemainSec <= toLineSec) return toLine;
            double x = (timeRemainSec - toLineSec) / avgLapSec;
            return toLine + Math.Floor(x) + 1;
        }
        return null;
    }

    /// <summary>Line crossings left including the flag (the "laps to go" a driver counts).</summary>
    public static int CrossingsToFinish(double distanceLaps, double lapPct) =>
        Math.Max(0, (int)Math.Round(distanceLaps + Math.Clamp(lapPct, 0, 1)));

    /// <summary>Liters to add: shortfall plus half a lap of reserve, rounded up to whole liters and limited to the
    /// room left in the tank when the capacity is known. Never negative.</summary>
    public static int AddFuelL(double shortL, double avgPerLap, double capacityL, double fuelL)
    {
        double raw = Math.Ceiling(shortL + ReserveLaps * avgPerLap - 1e-9);
        if (capacityL > 0 && fuelL >= 0) raw = Math.Min(raw, Math.Floor(capacityL - fuelL + 1e-9));
        return (int)Math.Max(0, raw);
    }

    /// <summary>Last lap (1-based) at whose end the car can still pit with the fuel it has.</summary>
    public static int LastPitLap(int lap, double lapPct, double lapsLeft) =>
        lap + (int)Math.Floor(lapsLeft + Math.Clamp(lapPct, 0, 1)) - 1;
}

/// <summary>Proactive race engineer: fuel, pit, flags, race position/gaps, car and weather calls, with
/// hysteresis and cooldowns. Pure logic; fed by <see cref="CoachEngine"/> with each sample that carries
/// <see cref="EngineerData"/>. Silent for replay/spectating and (strategy calls) outside races.</summary>
public sealed class RaceEngineer
{
    // ---- thresholds (see the report / docs) ----
    public const int FuelAverageLaps = 5;
    public const int FuelMinLapsForAverage = 2;
    public const double MinLapFuelL = 0.05;
    public const double BoxMarginLaps = 0.15;
    public const double GapAlertSec = 1.0;
    public const double GapRearmSec = 1.6;
    public const double GapHoldSec = 2.0;
    public const double GapCooldownSec = 90.0;
    public const double PositionHoldSec = 3.0;
    public const double PositionCooldownSec = 10.0;
    public const double RaceStartQuietSec = 10.0;
    public const double HeavyContactIncidents = 4;
    public const double WaterHighC = 118, WaterClearC = 110, OilHighC = 150, OilClearC = 140;
    public const double TrackTempChangeC = 4.0;
    public const double MinPitStaySec = 3.0;
    public static readonly int[] LapMilestones = { 10, 5, 3, 2 };
    /// <summary>Rejoin calls: pit window "close" = fuel short and at most this many laps of fuel left (leaves above
    /// RejoinWindowExitLaps); repeated every RejoinEveryLaps laps, at least RejoinCooldownSec apart.</summary>
    public const double RejoinWindowLaps = 3.5, RejoinWindowExitLaps = 4.5, RejoinCooldownSec = 60, RejoinEventCooldownSec = 15;
    public const int RejoinEveryLaps = 2;
    /// <summary>Other-class cars are mentioned only within this many seconds of the exit.</summary>
    public const double RejoinTrafficSec = 10;

    private const int FlagCheckered = 0x1, FlagWhite = 0x2, FlagYellow = 0x8, FlagRed = 0x10, FlagBlue = 0x20,
        FlagYellowWaving = 0x100, FlagCaution = 0x4000, FlagCautionWaving = 0x8000,
        FlagBlack = 0x10000, FlagDisqualify = 0x20000, FlagRepair = 0x100000;
    private const int FlagStartGo = unchecked((int)0x80000000);
    private const int YellowMask = FlagYellow | FlagYellowWaving | FlagCaution | FlagCautionWaving;
    private const int StateRacing = 4, StateCoolDown = 6;
    private const int WarnWater = 0x1, WarnPitLimiter = 0x10;

    private readonly List<CoachMessage> _out = new();
    private readonly Dictionary<string, double> _lastSpoken = new();
    private readonly List<double> _lapFuel = new();
    private readonly List<double> _lapTimes = new();

    private bool _primed;
    private double _t = double.NegativeInfinity;
    private EngineerStatus _status = EngineerStatus.Empty;

    // lap tracking
    private int _lap = -1;
    private double _lapStartFuel = -1, _lapStartTime;
    private bool _lapClean;

    // fuel / pit state
    private int _fuelStage, _finishState; // finishState: 0 none, 1 short, 2 enough
    private int _finishAdd;
    private int _boxLap = -1;
    private bool _prevPit;
    private double _pitEnterTime, _pitEnterFuel = -1;
    private double _limiterCheckAt = double.NaN;
    private bool _rejoinWindow;
    private int _rejoinLap = -1;

    // flags / race state
    private int _prevFlags, _prevState;
    private bool _startSpoken;
    private double _racingSince = double.NegativeInfinity;
    private readonly HashSet<int> _milestones = new();

    // position / gaps
    private int _stablePos, _candPos;
    private double _candSince;
    private bool _aheadArmed = true, _behindArmed = true;
    private double _aheadSince = double.NaN, _behindSince = double.NaN;

    // car / weather
    private int _incidents = -1;
    private bool _waterArmed = true, _oilArmed = true, _rainArmed = true;
    private double _trackTempBase = double.NaN;

    public EngineerOptions Options { get; } = new();

    /// <summary>One line per decision (laps used/ignored, calls issued) for coach-engineer.log.</summary>
    public event Action<string>? Log;

    public EngineerStatus Status => _status;

    /// <summary>Forgets everything (new session/context).</summary>
    public void Reset()
    {
        _out.Clear(); _lastSpoken.Clear(); _lapFuel.Clear(); _lapTimes.Clear();
        _primed = false; _t = double.NegativeInfinity; _status = EngineerStatus.Empty;
        _lap = -1; _lapStartFuel = -1; _lapClean = false;
        _fuelStage = 0; _finishState = 0; _finishAdd = 0; _boxLap = -1;
        _prevPit = false; _pitEnterFuel = -1; _limiterCheckAt = double.NaN; _rejoinWindow = false; _rejoinLap = -1;
        _prevFlags = 0; _prevState = 0; _startSpoken = false; _racingSince = double.NegativeInfinity;
        _milestones.Clear();
        _stablePos = 0; _candPos = 0; _aheadArmed = _behindArmed = true; _aheadSince = _behindSince = double.NaN;
        _incidents = -1; _waterArmed = _oilArmed = _rainArmed = true; _trackTempBase = double.NaN;
    }

    public static bool IsRace(string? sessionType) =>
        sessionType != null && sessionType.TrimStart().StartsWith("Race", StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns the messages to queue for this sample (usually none). The returned list is reused.</summary>
    public IReadOnlyList<CoachMessage> Update(CoachSample s, string? sessionType, string language)
    {
        _out.Clear();
        var e = s.Eng;
        if (e == null || !Options.Enabled) return _out;
        if (e.NotDriving) { _primed = false; _lap = -1; return _out; }

        double now = s.SessionTime;
        if (now < _t - 1.0) { _lastSpoken.Clear(); _primed = false; _lap = -1; _limiterCheckAt = double.NaN; }
        _t = now;

        bool race = IsRace(sessionType);
        bool yellow = (e.SessionFlags & YellowMask) != 0;
        string lang = CoachPhrases.Normalize(language);

        TrackLap(s, e, now, yellow);
        _status = ComputeStatus(s, e);

        if (!_primed)
        {
            _prevFlags = e.SessionFlags; _prevState = e.SessionState;
            _prevPit = s.OnPitRoad; _pitEnterTime = now; _pitEnterFuel = e.FuelLevelL;
            _incidents = e.Incidents; _stablePos = _status.Position; _candPos = _stablePos;
            if (e.SessionState == StateRacing) _racingSince = double.NegativeInfinity;
            if (e.TrackTempC > -50) _trackTempBase = e.TrackTempC;
            _primed = true;
            return _out;
        }

        if (e.SessionState == StateRacing && _prevState != StateRacing) _racingSince = now;

        var ctx = new Ctx(s, e, now, race, yellow, lang);
        RaceStart(ctx);
        Flags(ctx);
        FuelCalls(ctx);
        bool boxCalled = PitCalls(ctx);
        RejoinCalls(ctx, boxCalled);
        RaceCalls(ctx);
        CarCalls(ctx);
        WeatherCalls(ctx);
        SessionEnd(ctx);

        _prevFlags = e.SessionFlags;
        _prevState = e.SessionState;
        _prevPit = s.OnPitRoad;
        return _out;
    }

    private readonly record struct Ctx(CoachSample S, EngineerData E, double Now, bool Race, bool Yellow, string Lang);

    // ------------------------------------------------------------------ lap / fuel tracking

    private void TrackLap(CoachSample s, EngineerData e, double now, bool yellow)
    {
        if (e.FuelLevelL < 0) return;
        if (_lap < 0 || s.Lap < _lap || s.Lap > _lap + 1) { StartLap(s, e, now, yellow); return; }
        if (s.Lap == _lap + 1)
        {
            double used = _lapStartFuel - e.FuelLevelL;
            double lapTime = now - _lapStartTime;
            string why = !_lapClean ? "pit/yellow" : _lapStartFuel < 0 ? "no start fuel" : used < MinLapFuelL ? "no consumption/refuel"
                : lapTime < 10 ? "too short" : "";
            if (why == "")
            {
                _lapFuel.Add(used); if (_lapFuel.Count > FuelAverageLaps) _lapFuel.RemoveAt(0);
                _lapTimes.Add(lapTime); if (_lapTimes.Count > FuelAverageLaps) _lapTimes.RemoveAt(0);
                Log?.Invoke(Inv($"t={now:F1} lap={_lap} fuel lap used {used:F3} L, time {lapTime:F1} s: counted (avg {AvgFuel():F3} L over {_lapFuel.Count})"));
            }
            else Log?.Invoke(Inv($"t={now:F1} lap={_lap} fuel lap ignored: {why}"));
            StartLap(s, e, now, yellow);
        }
        else if (s.OnPitRoad || yellow) _lapClean = false;
    }

    private void StartLap(CoachSample s, EngineerData e, double now, bool yellow)
    {
        _lap = s.Lap; _lapStartFuel = e.FuelLevelL; _lapStartTime = now;
        _lapClean = !s.OnPitRoad && !yellow;
    }

    private double AvgFuel() => _lapFuel.Count >= FuelMinLapsForAverage ? _lapFuel.Average() : double.NaN;
    private double AvgLapSec() => _lapTimes.Count > 0 ? _lapTimes.Average() : double.NaN;

    private EngineerStatus ComputeStatus(CoachSample s, EngineerData e)
    {
        double avg = AvgFuel();
        double fuel = e.FuelLevelL;
        double lapsLeft = fuel >= 0 && avg > 0 ? fuel / avg : double.NaN;
        double? dist = EngineerMath.DistanceToFinishLaps(e.SessionLapsRemain, e.SessionTimeRemainSec, s.LapDistPct, AvgLapSec());
        double shortL = double.NaN; int add = -1; int toGo = -1;
        if (dist is double d)
        {
            toGo = EngineerMath.CrossingsToFinish(d, s.LapDistPct);
            if (!double.IsNaN(lapsLeft)) { shortL = d * avg - fuel; add = shortL > 0 ? EngineerMath.AddFuelL(shortL, avg, e.FuelCapacityL, fuel) : 0; }
        }
        bool box = !double.IsNaN(lapsLeft) && lapsLeft - (1.0 - Math.Clamp(s.LapDistPct, 0, 1)) < 1.0 + BoxMarginLaps
            && (dist == null || shortL > 0);
        int pos = e.ClassPosition > 0 ? e.ClassPosition : e.Position;
        return new EngineerStatus(fuel, avg, lapsLeft, toGo, shortL, add, box, pos, e.GapAheadSec, e.GapBehindSec) { Rejoin = e.Rejoin };
    }

    // ------------------------------------------------------------------ rules

    private void RaceStart(Ctx c)
    {
        if (!Options.Flags || !c.Race || _startSpoken) return;
        bool go = (c.E.SessionFlags & FlagStartGo) != 0 && (_prevFlags & FlagStartGo) == 0;
        bool lights = c.E.SessionState == StateRacing && _prevState is >= 1 and < StateRacing;
        if (go || lights)
        {
            _startSpoken = true;
            Emit(c, "race.start", null, 5, stopped: true, ttl: 6, rule: "start", cooldown: 0);
        }
    }

    private void Flags(Ctx c)
    {
        if (!Options.Flags) return;
        int cur = c.E.SessionFlags, prev = _prevFlags;
        bool Rose(int mask) => (cur & mask) != 0 && (prev & mask) == 0;

        if (c.Race && (cur & YellowMask) != 0 && (prev & YellowMask) == 0)
            Emit(c, "flag.yellow", null, 6, interrupt: true, ttl: 12, rule: "flag.yellow", cooldown: 5);
        else if (c.Race && (cur & YellowMask) == 0 && (prev & YellowMask) != 0 && c.E.SessionState == StateRacing)
            Emit(c, "flag.green", null, 5, ttl: 12, rule: "flag.green", cooldown: 5);
        if (c.Race && Rose(FlagBlue))
            Emit(c, "flag.blue", null, 6, interrupt: true, ttl: 10, rule: "flag.blue", cooldown: 25);
        if (c.Race && Rose(FlagWhite))
            Emit(c, "flag.white", null, 5, ttl: 15, rule: "flag.white", cooldown: 30);
        if (Rose(FlagCheckered))
        {
            int pos = _status.Position;
            if (pos > 0) Emit(c, "flag.checkered.pos", new() { ["pos"] = pos.ToString(CultureInfo.InvariantCulture) }, 5, stopped: true, ttl: 15, rule: "flag.checkered", cooldown: 30);
            else Emit(c, "flag.checkered", null, 5, stopped: true, ttl: 15, rule: "flag.checkered", cooldown: 30);
        }
        if (Rose(FlagBlack | FlagDisqualify))
            Emit(c, "flag.black", null, 6, interrupt: true, stopped: true, ttl: 15, rule: "flag.black", cooldown: 20);
        if (Rose(FlagRed))
            Emit(c, "flag.red", null, 6, interrupt: true, stopped: true, ttl: 12, rule: "flag.red", cooldown: 20);
        if (Rose(FlagRepair))
            Emit(c, "flag.repair", null, 6, interrupt: true, stopped: true, ttl: 15, rule: "flag.repair", cooldown: 30);
    }

    private void FuelCalls(Ctx c)
    {
        double lapsLeft = _status.LapsLeft;
        if (double.IsNaN(lapsLeft)) return;
        if (lapsLeft > 5.5) _fuelStage = 0; // refueled: re-arm
        if (!Options.Fuel || c.S.OnPitRoad) return;

        int stage = lapsLeft < 1 ? 4 : lapsLeft < 2 ? 3 : lapsLeft <= 3 ? 2 : lapsLeft <= 5 ? 1 : 0;
        int minStage = c.Race ? 1 : 3;
        if (stage >= minStage && stage > _fuelStage)
        {
            _fuelStage = stage;
            switch (stage)
            {
                case 4: Emit(c, "fuel.empty", null, 5, ttl: 15, rule: "fuel.stage", cooldown: 0); break;
                case 3: Emit(c, "fuel.critical", new() { ["n"] = "2" }, 5, ttl: 15, rule: "fuel.stage", cooldown: 0); break;
                default:
                    Emit(c, "fuel.laps", new() { ["n"] = ((int)Math.Ceiling(lapsLeft)).ToString(CultureInfo.InvariantCulture) }, 4,
                        ttl: 20, rule: "fuel.stage", cooldown: 0);
                    break;
            }
        }

        // Fuel to the finish (races with known length): announce a shortfall once per stint, again only if the
        // amount to add moved by at least max(2 L, one lap of fuel); "enough" once.
        if (!c.Race || c.E.SessionState != StateRacing || double.IsNaN(_status.ShortL)) return;
        double avg = _status.AvgPerLap;
        if (_status.ShortL > 0)
        {
            int add = _status.AddL;
            bool changed = _finishState != 1 || Math.Abs(add - _finishAdd) >= Math.Max(2.0, avg);
            if (changed && Allow("fuel.finish", c.Now, 60))
            {
                _finishState = 1; _finishAdd = add;
                Emit(c, "fuel.finish", new()
                {
                    ["short"] = CoachPhrases.Number(c.Lang, _status.ShortL, 1),
                    ["add"] = add.ToString(CultureInfo.InvariantCulture),
                }, 5, ttl: 25, rule: "fuel.finish", cooldown: 0, force: true);
                if (Options.Pit && !double.IsNaN(lapsLeft))
                {
                    int n = (int)Math.Floor(lapsLeft);
                    int lap = EngineerMath.LastPitLap(c.S.Lap, c.S.LapDistPct, lapsLeft);
                    Emit(c, "pit.window", new()
                    {
                        ["n"] = n.ToString(CultureInfo.InvariantCulture),
                        ["lap"] = lap.ToString(CultureInfo.InvariantCulture),
                    }, 4, stopped: true, ttl: 25, rule: "pit.window", cooldown: 0, force: true);
                }
            }
        }
        else if (_finishState != 2 && _status.ShortL <= -0.5 * avg && Allow("fuel.enough", c.Now, 60))
        {
            _finishState = 2;
            Emit(c, "fuel.enough", null, 4, ttl: 20, rule: "fuel.enough", cooldown: 0, force: true);
        }
    }

    /// <summary>Returns true when "box this lap" was called in this sample.</summary>
    private bool PitCalls(Ctx c)
    {
        var s = c.S; var e = c.E;
        // Entering / leaving the pit road is tracked even with PitCalls off (fuel re-arm); calls are gated.
        if (s.OnPitRoad && !_prevPit)
        {
            _pitEnterTime = c.Now; _pitEnterFuel = e.FuelLevelL;
            _limiterCheckAt = c.Now + 1.5;
        }
        if (!s.OnPitRoad && _prevPit)
        {
            _limiterCheckAt = double.NaN;
            bool stayed = c.Now - _pitEnterTime >= MinPitStaySec;
            _fuelStage = 0; _finishState = 0; _boxLap = -1;
            if (Options.Pit && stayed && e.FuelLevelL >= 0 && _pitEnterFuel >= 0)
            {
                double added = e.FuelLevelL - _pitEnterFuel;
                if (added >= 0.5)
                    Emit(c, "pit.exit", new()
                    {
                        ["to"] = CoachPhrases.Number(c.Lang, e.FuelLevelL, 1),
                        ["added"] = CoachPhrases.Number(c.Lang, added, 1),
                    }, 5, stopped: true, ttl: 15, rule: "pit.exit", cooldown: 20);
                else Emit(c, "pit.exit.nofuel", null, 5, stopped: true, ttl: 15, rule: "pit.exit", cooldown: 20);
            }
        }
        if (!Options.Pit) return false;

        if (!double.IsNaN(_limiterCheckAt) && c.Now >= _limiterCheckAt)
        {
            _limiterCheckAt = double.NaN;
            if (s.OnPitRoad && e.EngineWarnings >= 0 && (e.EngineWarnings & WarnPitLimiter) == 0 && s.SpeedMs > 8)
                Emit(c, "pit.limiter", null, 5, stopped: true, ttl: 6, rule: "pit.limiter", cooldown: 10);
        }

        if (!s.OnPitRoad && _status.BoxNow && s.Lap != _boxLap && Allow("pit.box", c.Now, 20))
        {
            _boxLap = s.Lap;
            Emit(c, "pit.box", null, 5, stopped: true, ttl: 10, rule: "pit.box", cooldown: 0, force: true);
            return true;
        }
        return false;
    }

    /// <summary>"If you pit now, you rejoin P9, behind car 12, 1.8 seconds": right after "box this lap", on pit
    /// entry and every <see cref="RejoinEveryLaps"/> laps while the pit window is close. Races only, green only
    /// for the periodic call.</summary>
    private void RejoinCalls(Ctx c, bool boxCalled)
    {
        double ll = _status.LapsLeft;
        bool shortFuel = _status.ShortL > 0 || _status.BoxNow;
        if (double.IsNaN(ll) || !shortFuel || ll > RejoinWindowExitLaps) _rejoinWindow = false;
        else if (ll <= RejoinWindowLaps) _rejoinWindow = true;

        if (!Options.Rejoin || !c.Race || c.E.SessionState != StateRacing) return;
        var r = c.E.Rejoin;
        if (r is not { Available: true, RaceOrder: true } || r.PredictedPosition <= 0) return;

        bool entered = c.S.OnPitRoad && !_prevPit;
        bool periodic = _rejoinWindow && !c.S.OnPitRoad && !c.Yellow && (_rejoinLap < 0 || c.S.Lap >= _rejoinLap + RejoinEveryLaps);
        if (boxCalled || entered)
        {
            if (!Allow("pit.rejoin", c.Now, RejoinEventCooldownSec)) return;
        }
        else if (!periodic || !Allow("pit.rejoin", c.Now, RejoinCooldownSec)) return;

        var (key, args) = RejoinPhrase(r, c.Lang);
        _rejoinLap = c.S.Lap;
        Emit(c, key, args, 5, stopped: true, ttl: boxCalled || entered ? 12 : 20, rule: "pit.rejoin", cooldown: 0, force: true);
    }

    /// <summary>Phrase key + arguments for a prediction (shared by the proactive call and the voice answer).</summary>
    public static (string Key, Dictionary<string, string> Args) RejoinPhrase(PitRejoinPrediction r, string language)
    {
        string lang = CoachPhrases.Normalize(language);
        var args = new Dictionary<string, string>
        {
            ["lead"] = CoachPhrases.Render(lang, r.InPit ? "pit.rejoin.lead.pit" : "pit.rejoin.lead.now", null),
        };
        string N(double v) => CoachPhrases.Number(lang, v, 1);
        static string Car(RejoinNeighbour n) => (n.ClassName.Trim() + " " + n.Number.Trim()).Trim();

        if (!r.RaceOrder)
        {
            if (r.Ahead == null) return ("pit.rejoin.track.clear", args);
            args["num"] = r.Ahead.Number; args["gap"] = N(r.Ahead.GapSec);
            return ("pit.rejoin.track", args);
        }
        args["pos"] = r.PredictedPosition.ToString(CultureInfo.InvariantCulture);
        if (r.MultiClass)
        {
            var fb = r.FasterBehind is { } f && f.GapSec <= RejoinTrafficSec ? f : null;
            var sa = r.SlowerAhead is { } sl && sl.GapSec <= RejoinTrafficSec ? sl : null;
            if (fb != null) { args["fcar"] = Car(fb); args["fgap"] = N(fb.GapSec); }
            if (sa != null) { args["scar"] = Car(sa); args["sgap"] = N(sa.GapSec); }
            if (fb != null && sa != null) return ("pit.rejoin.mc", args);
            if (fb != null) return ("pit.rejoin.mc.faster", args);
            if (sa != null) return ("pit.rejoin.mc.slower", args);
            if (r.Ahead == null) return ("pit.rejoin.class.clear", args);
            args["num"] = r.Ahead.Number; args["gap"] = N(r.Ahead.GapSec);
            return ("pit.rejoin.class", args);
        }
        if (r.Ahead == null) return ("pit.rejoin.clear", args);
        args["num"] = r.Ahead.Number; args["gap"] = N(r.Ahead.GapSec);
        return ("pit.rejoin", args);
    }

    private void RaceCalls(Ctx c)
    {
        if (!Options.Gaps || !c.Race || c.E.SessionState != StateRacing) return;
        if (c.S.OnPitRoad) { _aheadSince = _behindSince = double.NaN; return; }

        // Position: commit a change only after it held PositionHoldSec; silent under yellow / right after the start.
        int pos = _status.Position;
        if (pos > 0)
        {
            if (_stablePos == 0) _stablePos = pos;
            if (pos == _stablePos) _candPos = pos;
            else
            {
                if (_candPos != pos) { _candPos = pos; _candSince = c.Now; }
                else if (c.Now - _candSince >= PositionHoldSec)
                {
                    int old = _stablePos;
                    _stablePos = pos;
                    if (!c.Yellow && c.Now - _racingSince >= RaceStartQuietSec)
                        Emit(c, pos < old ? "race.gain" : "race.loss", new() { ["pos"] = pos.ToString(CultureInfo.InvariantCulture) },
                            4, ttl: 12, rule: "position", cooldown: PositionCooldownSec);
                }
            }
        }

        if (!c.Yellow && c.Now - _racingSince >= RaceStartQuietSec)
        {
            Gap(c, c.E.GapAheadSec, ref _aheadArmed, ref _aheadSince, "race.attack");
            Gap(c, c.E.GapBehindSec, ref _behindArmed, ref _behindSince, "race.defend");
        }

        int toGo = _status.LapsToGo;
        if (toGo > 0 && Array.IndexOf(LapMilestones, toGo) >= 0 && !c.Yellow && _milestones.Add(toGo))
            Emit(c, "race.laps", new() { ["n"] = toGo.ToString(CultureInfo.InvariantCulture) }, 4, ttl: 15, rule: "laps", cooldown: 0);
    }

    private void Gap(Ctx c, double gap, ref bool armed, ref double since, string key)
    {
        if (double.IsNaN(gap) || gap <= 0) { since = double.NaN; return; }
        if (gap > GapRearmSec) { armed = true; since = double.NaN; return; }
        if (gap > GapAlertSec) { since = double.NaN; return; }
        if (double.IsNaN(since)) since = c.Now;
        if (armed && c.Now - since >= GapHoldSec && Allow(key, c.Now, GapCooldownSec))
        {
            armed = false;
            Emit(c, key, new() { ["gap"] = CoachPhrases.Number(c.Lang, gap, 1) }, 4, ttl: 10, rule: key, cooldown: 0, force: true);
        }
    }

    private void CarCalls(Ctx c)
    {
        var e = c.E;
        if (e.Incidents >= 0)
        {
            if (_incidents >= 0 && e.Incidents > _incidents)
            {
                int d = e.Incidents - _incidents;
                var args = new Dictionary<string, string> { ["n"] = e.Incidents.ToString(CultureInfo.InvariantCulture) };
                if (d >= HeavyContactIncidents) Emit(c, "car.heavy", args, 6, interrupt: true, ttl: 10, rule: "incident", cooldown: 8);
                else Emit(c, "car.contact", args, 5, ttl: 10, rule: "incident", cooldown: 8);
            }
            _incidents = e.Incidents;
        }

        bool waterHot = (e.EngineWarnings >= 0 && (e.EngineWarnings & WarnWater) != 0) || e.WaterTempC >= WaterHighC;
        bool waterClear = (e.EngineWarnings < 0 || (e.EngineWarnings & WarnWater) == 0) && e.WaterTempC < WaterClearC;
        if (waterClear) _waterArmed = true;
        else if (waterHot && _waterArmed)
        {
            _waterArmed = false;
            Emit(c, "car.water", null, 5, ttl: 12, rule: "water", cooldown: 90);
        }

        if (e.OilTempC >= 0 && e.OilTempC < OilClearC) _oilArmed = true;
        else if (e.OilTempC >= OilHighC && _oilArmed)
        {
            _oilArmed = false;
            Emit(c, "car.oil", null, 5, ttl: 12, rule: "oil", cooldown: 90);
        }
    }

    private void WeatherCalls(Ctx c)
    {
        var e = c.E;
        if (e.TrackTempC > -50)
        {
            if (double.IsNaN(_trackTempBase)) _trackTempBase = e.TrackTempC;
            else if (Math.Abs(e.TrackTempC - _trackTempBase) >= TrackTempChangeC && Allow("track.temp", c.Now, 300))
            {
                bool up = e.TrackTempC > _trackTempBase;
                _trackTempBase = e.TrackTempC;
                Emit(c, up ? "weather.track.up" : "weather.track.down",
                    new() { ["t"] = ((int)Math.Round(e.TrackTempC)).ToString(CultureInfo.InvariantCulture) },
                    4, ttl: 20, rule: "track.temp", cooldown: 0, force: true);
            }
        }
        if (e.Precipitation >= 0)
        {
            if (e.Precipitation < 0.02) _rainArmed = true;
            else if (e.Precipitation >= 0.1 && _rainArmed && Allow("rain", c.Now, 300))
            {
                _rainArmed = false;
                Emit(c, "weather.rain", null, 4, ttl: 20, rule: "rain", cooldown: 0, force: true);
            }
        }
    }

    private void SessionEnd(Ctx c)
    {
        if (!Options.Flags || c.Race) return;
        if (c.E.SessionState == StateCoolDown && _prevState != StateCoolDown)
            Emit(c, "session.end", null, 4, stopped: true, ttl: 15, rule: "session.end", cooldown: 0);
    }

    // ------------------------------------------------------------------ helpers

    private bool Allow(string rule, double now, double cooldownSec)
    {
        if (cooldownSec <= 0) return true;
        return !_lastSpoken.TryGetValue(rule, out var last) || now - last >= cooldownSec;
    }

    /// <summary>Queues a call. With cooldown the rule is rate-limited here; force = the caller already
    /// checked <see cref="Allow"/> and only wants the timestamp stored (when cooldown is 0 nothing is stored).</summary>
    private void Emit(Ctx c, string key, Dictionary<string, string>? args, int priority, bool interrupt = false,
        bool stopped = false, double ttl = 15, string? rule = null, double cooldown = 0, bool force = false)
    {
        rule ??= key;
        if (!force && cooldown > 0 && !Allow(rule, c.Now, cooldown)) return;
        if (cooldown > 0 || force) _lastSpoken[rule] = c.Now;
        args ??= new Dictionary<string, string>();
        var msg = CoachMessages.Build(c.Lang, CoachMessageKind.Engineer, key, args, priority)
            with { Interrupt = interrupt, AllowStopped = stopped, TtlSec = ttl };
        _out.Add(msg);
        Log?.Invoke(Inv($"t={c.Now:F1} lap={c.S.Lap} SAY {key} [{string.Join(", ", args.Select(kv => kv.Key + "=" + kv.Value))}] -> {msg.Text}"));
    }

    private static string Inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
}
