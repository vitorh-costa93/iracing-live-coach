using System;
using System.Collections.Generic;
using System.Linq;

namespace IracingLiveCoach.Core.Coach;

/// <summary>Driving coach (Trophi-like), pure logic. Feed it samples at ~60 Hz; it records laps by
/// distance bins, compares them with the reference (best valid lap, persisted per track/car), and
/// raises prioritized/spaced messages, lap reports and brake-tone cues. Not thread-safe: call
/// everything from the telemetry thread.</summary>
public sealed class CoachEngine
{
    public const double LiveTraceSeconds = 10.0;
    public const double MaxLapTimeVsRef = 1.30;
    public const double MinLapTimeSec = 10.0;
    public const double PraiseMinGainSec = 0.1;
    /// <summary>Consecutive clean laps slower than MaxLapTimeVsRef x a reference loaded from disk after
    /// which that reference is considered stale (wet track, new setup...) and the current lap replaces it.</summary>
    public const int StaleReferenceLaps = 3;

    private readonly Func<DateTime> _clock;
    private readonly ReferenceStore _refStore;
    private readonly CoachWriteQueue? _writer;
    // References saved by this engine (path -> trace): authoritative while the background write is pending.
    private readonly Dictionary<string, ReferenceTrace> _savedRefs = new();
    private readonly WeeklyFocusStore _history;
    private readonly LapRecorder _recorder = new();
    private readonly CoachMessageScheduler _scheduler = new();
    private readonly BrakeToneScheduler _tones = new();
    private readonly RaceEngineer _engineer = new();
    private readonly List<TracePoint> _trace = new();
    private readonly List<double> _validLapTimes = new();
    private readonly List<int> _lapScores = new();

    private CoachContext? _ctx;
    private ReferenceTrace? _ref;
    private IReadOnlyList<CornerSegment> _corners = Array.Empty<CornerSegment>();
    private LapReport? _prevValid;
    private CoachMessage? _lastMessage;
    private double _lastTime = double.NegativeInfinity;
    private string _language = "en";
    private bool _refFromStore;
    private int _slowCleanStreak;

    public CoachEngine(string dataDir, Func<DateTime>? clock = null) : this(dataDir, clock, null) { }

    /// <summary>With a writer, reference and history.json saves are only enqueued (no file I/O on the
    /// calling/telemetry thread); without one they are written synchronously.</summary>
    public CoachEngine(string dataDir, Func<DateTime>? clock, CoachWriteQueue? writer)
    {
        _clock = clock ?? (() => DateTime.Now);
        _writer = writer;
        _refStore = new ReferenceStore(dataDir);
        _history = new WeeklyFocusStore(dataDir, writer);
        _engineer.Log += line => EngineerLogLine?.Invoke(line);
    }

    public SkillLevel Skill { get; set; } = SkillLevel.Intermediate;

    /// <summary>"en","pt-BR","es","de","fr" (other values are normalized, unknown -> "en").</summary>
    public string Language { get => _language; set => _language = CoachPhrases.Normalize(value); }

    public bool CornerTipsEnabled { get; set; } = true;
    public bool LapSummaryEnabled { get; set; } = true;
    public bool BrakeTonesEnabled { get; set; } = true;

    /// <summary>Race engineer (fuel, pit, flags, gaps, car, weather). Toggles apply on the next sample.</summary>
    public bool EngineerEnabled { get => _engineer.Options.Enabled; set => _engineer.Options.Enabled = value; }
    public bool FuelCallsEnabled { get => _engineer.Options.Fuel; set => _engineer.Options.Fuel = value; }
    public bool FlagCallsEnabled { get => _engineer.Options.Flags; set => _engineer.Options.Flags = value; }
    public bool GapCallsEnabled { get => _engineer.Options.Gaps; set => _engineer.Options.Gaps = value; }
    public bool PitCallsEnabled { get => _engineer.Options.Pit; set => _engineer.Options.Pit = value; }
    public bool RejoinCallsEnabled { get => _engineer.Options.Rejoin; set => _engineer.Options.Rejoin = value; }
    public EngineerStatus EngineerStatus => _engineer.Status;

    /// <summary>One line per engineer decision (for coach-engineer.log).</summary>
    public event Action<string>? EngineerLogLine;

    public event Action<CoachMessage>? MessageReady;
    public event Action<LapReport>? LapCompleted;
    public event Action<BrakeToneCue>? BrakeToneCue;

    public CoachContext? Context => _ctx;
    /// <summary>Last ~LiveTraceSeconds of samples (oldest first), with reference values at the same distance.</summary>
    public IReadOnlyList<TracePoint> LiveTrace => _trace.ToArray();
    public ReferenceTrace? Reference => _ref;
    /// <summary>Last valid lap report (invalid laps are reported via LapCompleted only).</summary>
    public LapReport? LastReport { get; private set; }
    public TechniqueBreakdown? LastBreakdown { get; private set; }
    /// <summary>Mean technique score of the valid laps since SetContext (0 = none yet).</summary>
    public int SessionScore { get; private set; }
    public CoachMessage? LastMessage => _lastMessage;
    /// <summary>Corners detected on the reference: (turn number, start, apex, end) as lap fractions.</summary>
    public IReadOnlyList<(int Turn, double StartPct, double ApexPct, double EndPct)> ReferenceCorners =>
        _corners.Select(c => (c.Number, Pct(c.StartBin), Pct(c.ApexBin), Pct(c.EndBin))).ToArray();
    /// <summary>Weekly focus category ("Braking","TrailBraking","Throttle","Consistency") or null.</summary>
    public string? WeeklyFocus => _ctx == null ? null : _history.GetFocus(_ctx.TrackId, _ctx.CarId, _clock());

    public void SetContext(CoachContext ctx)
    {
        _ctx = ctx;
        _recorder.Reset();
        _scheduler.Clear();
        _trace.Clear();
        _validLapTimes.Clear();
        _lapScores.Clear();
        _prevValid = null;
        LastReport = null;
        LastBreakdown = null;
        SessionScore = 0;
        _lastTime = double.NegativeInfinity;
        _slowCleanStreak = 0;
        _engineer.Reset();

        var loaded = _savedRefs.TryGetValue(_refStore.PathFor(ctx.TrackId, ctx.CarId), out var mem)
            ? mem : _refStore.Load(ctx.TrackId, ctx.CarId);
        if (loaded != null && ctx.TrackLengthM > 0 && Math.Abs(loaded.TrackLengthM - ctx.TrackLengthM) > ctx.TrackLengthM * 0.05)
            loaded = null; // different layout under the same id
        SetReference(loaded, fromStore: loaded != null);

        var key = _ref != null ? "briefing.ref" : "briefing.noref";
        var args = new Dictionary<string, string>();
        if (_ref != null) args["lap"] = CoachPhrases.LapTime(Language, _ref.LapTimeSec);
        _scheduler.Enqueue(CoachMessages.Build(Language, CoachMessageKind.Briefing, key, args, CoachMessages.PriorityBriefing));
    }

    public void Feed(CoachSample s)
    {
        if (_ctx == null) return;
        if (s.SessionTime < _lastTime - 1.0)
        {
            _trace.Clear();
            _scheduler.ResetTiming();
        }
        _lastTime = s.SessionTime;

        AddTrace(s);

        var lap = _recorder.Add(s);
        if (lap != null) OnLapFinished(lap);

        if (BrakeTonesEnabled && _ref != null)
            foreach (var cue in _tones.Update(s, TrackLength))
                BrakeToneCue?.Invoke(cue);

        if (s.Eng != null)
            foreach (var m in _engineer.Update(s, _ctx.SessionType, Language))
                _scheduler.Enqueue(m);

        var msg = _scheduler.Tick(s, TrackLength);
        if (msg != null)
        {
            _lastMessage = msg;
            MessageReady?.Invoke(msg);
        }
    }

    public string Answer(string intent) =>
        CoachQuery.Answer(intent, new CoachQueryState(Language, LastReport, _ref?.LapTimeSec, SessionScore,
            WeeklyFocus, _lastMessage?.Text, _engineer.Status));

    private double TrackLength => _ctx is { TrackLengthM: > 0 } ? _ctx.TrackLengthM : _ref?.TrackLengthM ?? 0;

    private static double Pct(int bin) => ReferenceTrace.PctOf(bin);

    private void SetReference(ReferenceTrace? r, bool fromStore)
    {
        _ref = r;
        _refFromStore = fromStore;
        _slowCleanStreak = 0;
        _corners = r != null ? CornerDetector.Detect(r) : Array.Empty<CornerSegment>();
        _tones.SetBrakePoints(_corners.Select(c => Pct(c.RefBrakeBin ?? c.StartBin)));
    }

    private void AddTrace(CoachSample s)
    {
        double? rs = null, rb = null, rt = null;
        if (_ref != null)
        {
            int b = ReferenceTrace.BinOf(s.LapDistPct);
            rs = _ref.Speed[b]; rb = _ref.Brake[b]; rt = _ref.Throttle[b];
        }
        _trace.Add(new TracePoint(s.SessionTime, s.LapDistPct, s.SpeedMs, s.Brake, s.Throttle, s.SteeringRad, s.Gear, rs, rb, rt));
        int drop = 0;
        while (drop < _trace.Count && _trace[drop].SessionTime < s.SessionTime - LiveTraceSeconds) drop++;
        if (drop > 0) _trace.RemoveRange(0, drop);
    }

    private void OnLapFinished(RecordedLap lap)
    {
        var ctx = _ctx!;
        bool clean = lap.Complete && lap.Clean && lap.LapTimeSec >= MinLapTimeSec;
        bool tooSlow = clean && _ref != null && lap.LapTimeSec > _ref.LapTimeSec * MaxLapTimeVsRef;
        bool staleReset = false;
        if (tooSlow && _refFromStore)
        {
            // A reference from an earlier session that is now far out of reach (rain, new setup):
            // after StaleReferenceLaps consecutive clean laps over the limit, this lap replaces it.
            if (++_slowCleanStreak >= StaleReferenceLaps)
            {
                SetReference(null, fromStore: false);
                _prevValid = null;
                staleReset = true;
                tooSlow = false;
            }
        }
        else _slowCleanStreak = 0;
        bool valid = clean && !tooSlow;
        double delta = _ref != null ? lap.LapTimeSec - _ref.LapTimeSec : 0;

        if (!valid)
        {
            LapCompleted?.Invoke(new LapReport(lap.Lap, lap.LapTimeSec, false, Math.Round(delta, 3),
                Array.Empty<CornerReport>(), 0));
            return;
        }

        var corners = _ref != null && _corners.Count > 0
            ? CornerAnalyzer.Analyze(lap, _ref, _corners)
            : Array.Empty<CornerReport>();
        _validLapTimes.Add(lap.LapTimeSec);
        var breakdown = TechniqueScorer.Score(corners, _validLapTimes, delta);
        var report = new LapReport(lap.Lap, lap.LapTimeSec, true, Math.Round(delta, 3), corners, breakdown.Total);

        // A lap without a reference to compare with only becomes the reference: it is not scored
        // (its 100 would inflate the session score) nor recorded in the weekly history.
        if (_ref != null)
        {
            _lapScores.Add(breakdown.Total);
            SessionScore = (int)Math.Round(_lapScores.Average());
            _history.Record(ctx.TrackId, ctx.CarId, _clock(), lap.LapTimeSec, breakdown);
        }

        bool canStore = ctx.TrackLengthM > 0;
        bool newBest = canStore && (_ref == null || lap.LapTimeSec < _ref.LapTimeSec);
        PlanMessages(report, newBest && !staleReset);

        if (newBest)
        {
            var r = ReferenceTrace.FromLap(lap, ctx, _clock().ToUniversalTime());
            SaveReference(r);
            SetReference(r, fromStore: false);
            _prevValid = null; // corner losses vs the old reference are not comparable any more
        }
        else _prevValid = report;

        LastReport = report;
        LastBreakdown = breakdown;
        LapCompleted?.Invoke(report);
    }

    private void SaveReference(ReferenceTrace r)
    {
        var path = _refStore.PathFor(r.TrackId, r.CarId);
        _savedRefs[path] = r; // r is never mutated after this point
        if (_writer != null) _writer.Enqueue(path, () => _refStore.Save(r));
        else _refStore.Save(r);
    }

    private void PlanMessages(LapReport report, bool newBest)
    {
        var lang = Language;
        if (CornerTipsEnabled)
        {
            var tips = SelectTipCorners(report.Corners, Skill)
                .Select(c => (Msg: CoachMessages.Tip(lang, c)!, c.StartPct))
                .ToList();
            _scheduler.PlanTips(tips);

            var praise = BestImprovement(report, tips.Select(t => t.Msg.Args["turn"]).ToHashSet());
            if (praise != null) _scheduler.Enqueue(praise);
        }
        else _scheduler.PlanTips(Array.Empty<(CoachMessage, double)>());

        if (LapSummaryEnabled)
        {
            var lapTxt = CoachPhrases.LapTime(lang, report.LapTimeSec);
            var worst = report.Corners.Where(c => c.MainIssue != nameof(CornerIssue.None))
                .OrderByDescending(c => c.TimeLostSec).FirstOrDefault();
            var args = new Dictionary<string, string>
            {
                ["lap"] = lapTxt,
                ["delta"] = CoachPhrases.Number(lang, Math.Max(0, report.DeltaToRefSec), 1),
            };
            string key;
            if (newBest) key = "summary.best";
            else if (worst != null) { key = "summary.delta"; args["turn"] = worst.Index.ToString(); }
            else key = "summary.clean";
            _scheduler.Enqueue(CoachMessages.Build(lang, CoachMessageKind.LapSummary, key, args, CoachMessages.PrioritySummary));
        }
    }

    private CoachMessage? BestImprovement(LapReport report, HashSet<string> tippedTurns)
    {
        if (_prevValid == null) return null;
        CornerReport? best = null;
        double bestGain = 0;
        foreach (var c in report.Corners)
        {
            if (tippedTurns.Contains(c.Index.ToString())) continue;
            var p = _prevValid.Corners.FirstOrDefault(x => x.Index == c.Index);
            if (p == null || p.MainIssue == nameof(CornerIssue.None)) continue;
            double gain = p.TimeLostSec - c.TimeLostSec;
            if (gain >= PraiseMinGainSec && gain > bestGain) { best = c; bestGain = gain; }
        }
        if (best == null) return null;
        return CoachMessages.Build(Language, CoachMessageKind.Praise, "praise", new()
        {
            ["turn"] = best.Index.ToString(),
            ["gain"] = CoachPhrases.Number(Language, bestGain, 1),
        }, CoachMessages.PriorityPraise);
    }

    /// <summary>Corners to coach on the next lap, worst first. Beginner: never two consecutive corners
    /// (at most one tip every two corners); Intermediate: up to 3; Advanced: every corner with an issue.</summary>
    internal static IReadOnlyList<CornerReport> SelectTipCorners(IReadOnlyList<CornerReport> corners, SkillLevel skill)
    {
        var candidates = corners
            .Where(c => c.MainIssue != nameof(CornerIssue.None) && c.TimeLostSec >= CornerAnalyzer.MinLossSec)
            .OrderByDescending(c => c.TimeLostSec)
            .ToList();
        int n = corners.Count;
        var picked = new List<CornerReport>();
        switch (skill)
        {
            case SkillLevel.Beginner:
                foreach (var c in candidates)
                {
                    if (picked.Count >= Math.Max(1, n / 2)) break;
                    bool adjacent = picked.Any(p =>
                    {
                        int d = Math.Abs(p.Index - c.Index);
                        return d <= 1 || (n > 2 && d == n - 1);
                    });
                    if (!adjacent) picked.Add(c);
                }
                break;
            case SkillLevel.Intermediate:
                picked.AddRange(candidates.Take(3));
                break;
            default:
                picked.AddRange(candidates);
                break;
        }
        return picked;
    }
}
