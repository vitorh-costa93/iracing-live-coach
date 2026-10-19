using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IracingLiveCoach.Core.Coach;
using Xunit;

namespace IracingLiveCoach.Core.Tests.Coach;

public class CoachEngineTests : IDisposable
{
    private readonly string _dir = CoachSim.TempDir();
    private readonly DateTime _now = new(2026, 10, 19, 12, 0, 0);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private CoachEngine NewEngine(List<CoachMessage>? msgs = null, List<LapReport>? laps = null)
    {
        var e = new CoachEngine(_dir, () => _now) { BrakeTonesEnabled = false };
        if (msgs != null) e.MessageReady += msgs.Add;
        if (laps != null) e.LapCompleted += laps.Add;
        e.SetContext(CoachSim.Ctx);
        return e;
    }

    [Fact]
    public void Reference_lap_detects_three_corners()
    {
        var e = NewEngine();
        new CoachSim().DriveReference(e);

        Assert.NotNull(e.Reference);
        var corners = e.ReferenceCorners;
        Assert.Equal(3, corners.Count);
        Assert.Equal(800 / CoachSim.L, corners[0].ApexPct, 2);
        Assert.Equal(1700 / CoachSim.L, corners[1].ApexPct, 2);
        Assert.Equal(2600 / CoachSim.L, corners[2].ApexPct, 2);
    }

    [Fact]
    public void Early_braking_gives_brake_later_tip_on_next_lap_approach()
    {
        var msgs = new List<CoachMessage>();
        var laps = new List<LapReport>();
        var e = NewEngine(msgs, laps);
        var sim = new CoachSim();
        sim.DriveReference(e);

        sim.Feed(e, sim.DriveToLine(CoachSim.With(2, c => c with { EarlyBrakeM = 20 })));
        var r = laps.Last();
        Assert.True(r.Valid);
        Assert.True(r.DeltaToRefSec > 0.05);
        Assert.Equal(3, r.Corners.Count);
        var t2 = r.Corners[1];
        Assert.Equal(2, t2.Index);
        Assert.Equal(nameof(CornerIssue.BrakeTooEarly), t2.MainIssue);
        Assert.InRange(t2.BrakePointDeltaM, -24, -16);
        Assert.True(t2.TimeLostSec > 0.05);
        Assert.InRange(t2.TimeLostSec, r.DeltaToRefSec - 0.05, r.DeltaToRefSec + 0.05);
        Assert.Equal(nameof(CornerIssue.None), r.Corners[0].MainIssue);
        Assert.Equal(nameof(CornerIssue.None), r.Corners[2].MainIssue);

        // Next lap: the tip is spoken on the approach of turn 2, between 30 and 400 m before it.
        msgs.Clear();
        double tipPct = -1;
        e.MessageReady += m => { if (m.Kind == CoachMessageKind.CornerTip) tipPct = e.LiveTrace[^1].LapDistPct; };
        sim.Feed(e, sim.DriveToLine(CoachSim.Ref));
        var tips = msgs.Where(m => m.Kind == CoachMessageKind.CornerTip).ToList();
        var tip = Assert.Single(tips);
        Assert.Equal("tip.BrakeTooEarly", tip.Key);
        Assert.Equal("Brake later into turn 2, about 20 meters.", tip.Text);
        double before = (t2.StartPct - tipPct) * CoachSim.L;
        Assert.InRange(before, 30, 400);
        Assert.Contains(msgs, m => m.Kind == CoachMessageKind.LapSummary);
    }

    [Fact]
    public void Tip_is_rendered_in_portuguese()
    {
        var msgs = new List<CoachMessage>();
        var e = NewEngine(msgs);
        e.Language = "pt_BR";
        var sim = new CoachSim();
        sim.DriveReference(e);
        sim.Feed(e, sim.DriveToLine(CoachSim.With(2, c => c with { EarlyBrakeM = 20 })));
        sim.Feed(e, sim.DriveToLine(CoachSim.Ref));
        Assert.Contains(msgs, m => m.Text == "Frenagem mais tarde na curva 2, uns 20 metros.");
    }

    [Fact]
    public void Better_lap_replaces_reference_and_persists()
    {
        var laps = new List<LapReport>();
        var e = NewEngine(laps: laps);
        var sim = new CoachSim();
        var slow = CoachSim.Ref.Select(c => c with { ApexSpeedDelta = -4 }).ToArray();
        sim.Feed(e, sim.DriveToLine(slow));
        sim.Feed(e, sim.DriveToLine(slow));
        double slowTime = e.Reference!.LapTimeSec;

        sim.Feed(e, sim.DriveToLine(CoachSim.Ref));
        double fastTime = laps.Last().LapTimeSec;
        Assert.True(fastTime < slowTime);
        Assert.Equal(fastTime, e.Reference!.LapTimeSec, 6);
        Assert.True(laps.Last().DeltaToRefSec < 0);

        // A slower valid lap does not replace it.
        sim.Feed(e, sim.DriveToLine(slow));
        Assert.Equal(fastTime, e.Reference!.LapTimeSec, 6);

        var path = new ReferenceStore(_dir).PathFor(CoachSim.Ctx.TrackId, CoachSim.Ctx.CarId);
        Assert.True(File.Exists(path));
        var e2 = new CoachEngine(_dir, () => _now);
        e2.SetContext(CoachSim.Ctx);
        Assert.NotNull(e2.Reference);
        Assert.Equal(fastTime, e2.Reference!.LapTimeSec, 6);
        Assert.Equal(3, e2.ReferenceCorners.Count);
    }

    [Fact]
    public void Invalid_laps_are_ignored()
    {
        var msgs = new List<CoachMessage>();
        var laps = new List<LapReport>();
        var e = NewEngine(msgs, laps);
        var sim = new CoachSim();
        sim.DriveReference(e);
        var refTime = e.Reference!.LapTimeSec;
        var lastValid = e.LastReport;

        // Pit lap, even if it is faster on paper.
        var fast = CoachSim.Ref.Select(c => c with { ApexSpeedDelta = 3 }).ToArray();
        sim.Feed(e, sim.DriveToLine(fast, pitAt: x => x > 2900));
        Assert.False(laps.Last().Valid);
        Assert.Empty(laps.Last().Corners);
        Assert.Equal(refTime, e.Reference!.LapTimeSec);
        Assert.Same(lastValid, e.LastReport);

        // Anomalous lap (>130% of the reference).
        sim.Feed(e, sim.DriveToLine(CoachSim.With(2, c => c with { EarlyBrakeM = 20 }), scale: 0.6));
        Assert.False(laps.Last().Valid);
        Assert.Equal(refTime, e.Reference!.LapTimeSec);

        msgs.Clear();
        sim.Feed(e, sim.DriveToLine(CoachSim.Ref));
        Assert.DoesNotContain(msgs, m => m.Kind == CoachMessageKind.CornerTip);
    }

    private static SimCorner[] AllEarly() => CoachSim.Ref.Select(c => c with { EarlyBrakeM = 25 }).ToArray();

    [Theory]
    [InlineData(SkillLevel.Beginner, 1)]
    [InlineData(SkillLevel.Intermediate, 3)]
    [InlineData(SkillLevel.Advanced, 3)]
    public void Skill_level_limits_tips(SkillLevel skill, int expectedTips)
    {
        var msgs = new List<CoachMessage>();
        var e = NewEngine(msgs);
        e.Skill = skill;
        var sim = new CoachSim();
        sim.DriveReference(e);
        sim.Feed(e, sim.DriveToLine(AllEarly()));
        msgs.Clear();
        sim.Feed(e, sim.DriveToLine(CoachSim.Ref));
        Assert.Equal(expectedTips, msgs.Count(m => m.Kind == CoachMessageKind.CornerTip));
    }

    [Fact]
    public void Beginner_never_tips_two_consecutive_corners()
    {
        var reports = Enumerable.Range(1, 6).Select(i =>
            new CornerReport(i, 0, 0, 0, -20, 0, 0, 0, 0, 0.3 - i * 0.01, nameof(CornerIssue.BrakeTooEarly))).ToList();
        var picked = CoachEngine.SelectTipCorners(reports, SkillLevel.Beginner).Select(c => c.Index).OrderBy(i => i).ToList();
        Assert.Equal(new[] { 1, 3, 5 }, picked);
    }

    [Fact]
    public void No_speech_in_pits_or_at_low_speed()
    {
        var msgs = new List<CoachMessage>();
        var e = NewEngine(msgs); // briefing queued
        double t = 0;
        for (int i = 0; i < 600; i++)
            e.Feed(new CoachSample(t += 1 / 60.0, 0, 0.1 + i * 1e-5, 20, 0, 0.3, 0, 1, 3000, true, true));
        for (int i = 0; i < 600; i++)
            e.Feed(new CoachSample(t += 1 / 60.0, 0, 0.11 + i * 1e-5, 5, 0, 0.3, 0, 1, 3000, false, true));
        Assert.Empty(msgs);

        e.Feed(new CoachSample(t += 1 / 60.0, 0, 0.12, 20, 0, 1, 0, 2, 5000, false, true));
        var m = Assert.Single(msgs);
        Assert.Equal(CoachMessageKind.Briefing, m.Kind);
        Assert.Equal("No reference yet. Drive a clean lap to set one.", m.Text);
    }

    [Fact]
    public void Scheduler_keeps_four_seconds_between_messages()
    {
        var s = new CoachMessageScheduler();
        var args = new Dictionary<string, string>();
        s.Enqueue(new CoachMessage(CoachMessageKind.LapSummary, "a", args, 2, "A"));
        s.Enqueue(new CoachMessage(CoachMessageKind.Praise, "b", args, 1, "B"));
        CoachSample At(double t) => new(t, 1, 0.5, 40, 0, 1, 0, 4, 6000, false, true);
        Assert.Equal("A", s.Tick(At(10), 3000)!.Text);
        Assert.Null(s.Tick(At(13.9), 3000));
        Assert.Equal("B", s.Tick(At(14.0), 3000)!.Text);
    }

    [Fact]
    public void Brake_tones_fire_at_the_right_distance()
    {
        var cues = new List<(BrakeToneCue Cue, double Pct)>();
        var e = new CoachEngine(_dir, () => _now);
        e.SetContext(CoachSim.Ctx);
        var sim = new CoachSim();
        sim.DriveReference(e);
        e.BrakeToneCue += c => cues.Add((c, e.LiveTrace[^1].LapDistPct));
        sim.Feed(e, sim.DriveToLine(CoachSim.Ref));

        Assert.Equal(9, cues.Count);
        Assert.Equal(new[] { 1, 2, 3, 1, 2, 3, 1, 2, 3 }, cues.Select(c => c.Cue.Beat));
        double interval = BrakeToneScheduler.IntervalMeters(CoachSim.Vmax); // 35 m at 70 m/s
        foreach (var (cue, _) in cues)
        {
            double expected = (3 - cue.Beat) * interval;
            Assert.InRange(cue.MetersToBrakePoint, expected - 1.5, expected + 0.01);
        }
        // Beat 3 lands on the reference brake point of turn 1 (800 m apex, 178 m braking).
        double brake1 = 800 - (CoachSim.Vmax * CoachSim.Vmax - 25 * 25) / (2 * CoachSim.Decel);
        Assert.InRange(cues[2].Pct * CoachSim.L, brake1 - 4, brake1 + 4);
    }

    [Fact]
    public void Brake_tones_are_silent_when_disabled_or_in_pits()
    {
        var b = new BrakeToneScheduler();
        b.SetBrakePoints(new[] { 0.5 });
        int n = 0;
        for (double x = 1300; x < 1520; x += 1)
            n += b.Update(new CoachSample(0, 1, x / 3000, 40, 0, 1, 0, 4, 0, true, true), 3000).Count;
        Assert.Equal(0, n);
        for (double x = 1300; x < 1520; x += 1)
            n += b.Update(new CoachSample(0, 2, x / 3000, 40, 0, 1, 0, 4, 0, false, true), 3000).Count;
        Assert.Equal(3, n);
    }

    [Fact]
    public void Score_and_session_score()
    {
        var laps = new List<LapReport>();
        var e = NewEngine(laps: laps);
        var sim = new CoachSim();
        sim.DriveReference(e);
        Assert.Equal(100, laps.Last().TechniqueScore);
        Assert.Equal(0, e.SessionScore); // the lap that sets the first reference is not scored

        sim.Feed(e, sim.DriveToLine(CoachSim.Ref));
        int clean = laps.Last().TechniqueScore;
        Assert.InRange(clean, 95, 100);

        sim.Feed(e, sim.DriveToLine(AllEarly()));
        int early = laps.Last().TechniqueScore;
        Assert.True(early < clean);
        Assert.True(e.LastBreakdown!.Braking < 60);
        Assert.Equal("Braking", e.LastBreakdown.WorstCategory);
        Assert.Equal((int)Math.Round(laps.Skip(1).Average(l => l.TechniqueScore)), e.SessionScore);
    }

    [Fact]
    public void Technique_scorer_categories()
    {
        var c = new CornerReport(1, 0, 0, 0, -20, 0, -8, 12, -2, 0.2, nameof(CornerIssue.BrakeTooEarly));
        var b = TechniqueScorer.Score(new[] { c }, new[] { 90.0 }, 0.5);
        Assert.Equal(60, b.Braking);
        Assert.Equal(80, b.TrailBraking);
        Assert.Equal(70, b.Throttle);
        Assert.Equal(90, b.Consistency);
        Assert.Equal(75, b.Total);
        Assert.Equal("Braking", b.WorstCategory);
    }

    [Fact]
    public void Answers_to_intents()
    {
        var msgs = new List<CoachMessage>();
        var e = NewEngine(msgs);
        Assert.Equal("No lap completed yet.", e.Answer("lastlap"));
        Assert.Equal("Not enough laps yet for a focus.", e.Answer("focus"));

        var sim = new CoachSim();
        sim.DriveReference(e);
        // Three compared laps (the reference lap itself does not count for the weekly focus).
        for (int i = 0; i < 3; i++) sim.Feed(e, sim.DriveToLine(CoachSim.With(2, c => c with { EarlyBrakeM = 20 })));

        var last = e.LastReport!;
        Assert.StartsWith("Last lap " + CoachPhrases.LapTime("en", last.LapTimeSec), e.Answer("last lap"));
        Assert.StartsWith("Turn 2 costs the most", e.Answer("worstcorner"));
        Assert.EndsWith("Brake later into turn 2, about 20 meters.", e.Answer("worst_corner"));
        Assert.Equal($"Session score {e.SessionScore}. Last lap {last.TechniqueScore}.", e.Answer("score"));
        Assert.Equal("This week, focus on braking.", e.Answer("focus"));
        Assert.Equal(e.LastMessage!.Text, e.Answer("repeat"));
        var summary = e.Answer("summary");
        Assert.StartsWith("Lap ", summary);
        Assert.Contains("in turn 2. Brake later into turn 2, about 20 meters.", summary);
        Assert.Equal("Sorry, I did not get that.", e.Answer("make me coffee"));

        e.Language = "de";
        Assert.Equal("Diese Woche Fokus auf das Bremsen.", e.Answer("focus"));
        Assert.True(File.Exists(Path.Combine(_dir, "history.json")));
    }

    [Fact]
    public void Praise_when_corner_improves()
    {
        var msgs = new List<CoachMessage>();
        var e = NewEngine(msgs);
        e.Skill = SkillLevel.Advanced;
        var sim = new CoachSim();
        sim.DriveReference(e);
        // Slightly slower clean lap stays as non-reference, then a lap with early braking, then fixed.
        sim.Feed(e, sim.DriveToLine(CoachSim.With(2, c => c with { EarlyBrakeM = 30 })));
        sim.Feed(e, sim.DriveToLine(CoachSim.With(2, c => c with { EarlyBrakeM = 5 })));
        msgs.Clear();
        sim.Feed(e, sim.DriveToLine(CoachSim.Ref));
        Assert.Contains(msgs, m => m.Kind == CoachMessageKind.Praise && m.Text.StartsWith("Good, turn 2 is better."));
    }

    [Fact]
    public void Live_trace_keeps_last_seconds_with_reference_values()
    {
        var e = NewEngine();
        var sim = new CoachSim();
        sim.DriveReference(e);
        sim.Feed(e, sim.DriveToLine(CoachSim.Ref));
        var tr = e.LiveTrace;
        Assert.InRange(tr[^1].SessionTime - tr[0].SessionTime, CoachEngine.LiveTraceSeconds - 0.1, CoachEngine.LiveTraceSeconds);
        Assert.NotNull(tr[^1].RefSpeedMs);
        Assert.InRange(tr[^1].RefSpeedMs!.Value, tr[^1].SpeedMs - 1, tr[^1].SpeedMs + 1);
    }
}
