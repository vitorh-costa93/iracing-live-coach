using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IracingLiveCoach.Core.Coach;
using Xunit;

namespace IracingLiveCoach.Core.Tests.Coach;

public class CoachRobustnessTests : IDisposable
{
    private readonly string _dir = CoachSim.TempDir();
    private readonly DateTime _now = new(2026, 10, 19, 12, 0, 0);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    // ---- engine rules -------------------------------------------------------------------------

    [Fact]
    public void First_reference_lap_is_not_scored_nor_written_to_history()
    {
        var laps = new List<LapReport>();
        var e = new CoachEngine(_dir, () => _now) { BrakeTonesEnabled = false };
        e.LapCompleted += laps.Add;
        e.SetContext(CoachSim.Ctx);
        new CoachSim().DriveReference(e);

        Assert.True(laps.Last().Valid);
        Assert.NotNull(e.Reference);
        Assert.Equal(0, e.SessionScore);
        Assert.False(File.Exists(Path.Combine(_dir, "history.json")));
    }

    [Fact]
    public void Unknown_track_length_never_announces_new_best()
    {
        var msgs = new List<CoachMessage>();
        var e = new CoachEngine(_dir, () => _now) { BrakeTonesEnabled = false };
        e.MessageReady += msgs.Add;
        e.SetContext(CoachSim.Ctx with { TrackLengthM = 0 });
        var sim = new CoachSim();
        sim.DriveReference(e);
        sim.Feed(e, sim.DriveToLine(CoachSim.Ref.Select(c => c with { ApexSpeedDelta = 3 }).ToArray()));
        sim.Feed(e, sim.DriveToLine(CoachSim.Ref));

        Assert.Null(e.Reference);
        Assert.Contains(msgs, m => m.Kind == CoachMessageKind.LapSummary);
        Assert.DoesNotContain(msgs, m => m.Key == "summary.best");
    }

    [Fact]
    public void Stale_persisted_reference_is_replaced_after_three_slow_clean_laps()
    {
        var a = new CoachEngine(_dir, () => _now) { BrakeTonesEnabled = false };
        a.SetContext(CoachSim.Ctx);
        new CoachSim().DriveReference(a);
        double fast = a.Reference!.LapTimeSec;

        var laps = new List<LapReport>();
        var msgs = new List<CoachMessage>();
        var e = new CoachEngine(_dir, () => _now) { BrakeTonesEnabled = false };
        e.LapCompleted += laps.Add;
        e.MessageReady += msgs.Add;
        e.SetContext(CoachSim.Ctx);
        Assert.Equal(fast, e.Reference!.LapTimeSec, 6);

        var sim = new CoachSim();
        sim.Feed(e, sim.DriveToLine(CoachSim.Ref, scale: 0.6)); // out lap (crosses the line)
        sim.Feed(e, sim.DriveToLine(CoachSim.Ref, scale: 0.6));
        sim.Feed(e, sim.DriveToLine(CoachSim.Ref, scale: 0.6));
        Assert.All(laps, l => Assert.False(l.Valid));
        Assert.Equal(fast, e.Reference!.LapTimeSec, 6);

        sim.Feed(e, sim.DriveToLine(CoachSim.Ref, scale: 0.6));
        var third = laps.Last();
        Assert.True(third.Valid);
        Assert.True(third.LapTimeSec > fast * CoachEngine.MaxLapTimeVsRef);
        Assert.Equal(third.LapTimeSec, e.Reference!.LapTimeSec, 6);
        Assert.Equal(0, e.SessionScore);
        Assert.DoesNotContain(msgs, m => m.Key == "summary.best");
        Assert.Equal(third.LapTimeSec, new ReferenceStore(_dir).Load(CoachSim.Ctx.TrackId, CoachSim.Ctx.CarId)!.LapTimeSec, 6);
    }

    [Fact]
    public void Reference_set_this_session_is_not_replaced_by_slow_laps()
    {
        var e = new CoachEngine(_dir, () => _now) { BrakeTonesEnabled = false };
        e.SetContext(CoachSim.Ctx);
        var sim = new CoachSim();
        sim.DriveReference(e);
        double fast = e.Reference!.LapTimeSec;
        for (int i = 0; i < 4; i++) sim.Feed(e, sim.DriveToLine(CoachSim.Ref, scale: 0.6));
        Assert.Equal(fast, e.Reference!.LapTimeSec, 6);
    }

    [Fact]
    public void Engine_with_write_queue_persists_after_flush()
    {
        using var q = new CoachWriteQueue();
        var e = new CoachEngine(_dir, () => _now, q) { BrakeTonesEnabled = false };
        e.SetContext(CoachSim.Ctx);
        var sim = new CoachSim();
        sim.DriveReference(e);
        sim.Feed(e, sim.DriveToLine(CoachSim.Ref));
        Assert.True(q.Flush(TimeSpan.FromSeconds(5)));

        Assert.True(File.Exists(Path.Combine(_dir, "history.json")));
        Assert.Single(new WeeklyFocusStore(_dir).Entries);
        Assert.NotNull(new ReferenceStore(_dir).Load(CoachSim.Ctx.TrackId, CoachSim.Ctx.CarId));
    }

    // ---- write queue / settings ---------------------------------------------------------------

    [Fact]
    public void Write_queue_coalesces_by_key_and_drains_on_dispose()
    {
        var done = new List<string>();
        var gate = new System.Threading.ManualResetEventSlim(false);
        var q = new CoachWriteQueue();
        q.Enqueue("block", () => gate.Wait(TimeSpan.FromSeconds(5)));
        q.Enqueue("a", () => { lock (done) done.Add("a1"); });
        q.Enqueue("b", () => { lock (done) done.Add("b"); });
        q.Enqueue("a", () => { lock (done) done.Add("a2"); });
        gate.Set();
        q.Dispose();
        Assert.Equal(new[] { "a2", "b" }, done);
        q.Enqueue("late", () => done.Add("late")); // ignored after Dispose
        Assert.Equal(2, done.Count);
    }

    [Fact]
    public void Settings_try_load_distinguishes_missing_from_broken()
    {
        var path = Path.Combine(_dir, "coach-settings.json");
        var missing = CoachSettings.TryLoad(path);
        Assert.NotNull(missing);
        Assert.True(missing!.Enabled);

        File.WriteAllText(path, "{ \"Enabled\": false, ");
        Assert.Null(CoachSettings.TryLoad(path));
        Assert.True(CoachSettings.Load(path).Enabled); // Load stays compatible: defaults

        new CoachSettings { Enabled = false, Skill = SkillLevel.Advanced }.Save(path);
        var ok = CoachSettings.TryLoad(path)!;
        Assert.False(ok.Enabled);
        Assert.Equal(SkillLevel.Advanced, ok.Skill);
    }

    [Fact]
    public void Scheduler_with_nothing_pending_returns_null_and_brake_tones_without_points_are_empty()
    {
        var s = new CoachMessageScheduler();
        var sample = new CoachSample(10, 1, 0.5, 40, 0, 1, 0, 4, 6000, false, true);
        Assert.Null(s.Tick(sample, 3000));
        Assert.Empty(new BrakeToneScheduler().Update(sample, 3000));
    }

    // ---- corners across the start/finish line --------------------------------------------------

    private const double VmaxKmh = 250, VminKmh = 80;

    /// <summary>Synthetic circular reference: each corner brakes over 80 bins before the apex (brake 0.9)
    /// and accelerates over 100 bins after it.</summary>
    private static ReferenceTrace Circular(params int[] apexes)
    {
        const int n = ReferenceTrace.BinCount;
        var r = new ReferenceTrace { TrackId = "t", CarId = "c", TrackLengthM = 3000, LapTimeSec = 60 };
        for (int i = 0; i < n; i++)
        {
            double v = VmaxKmh, brake = 0, thr = 1;
            foreach (var a in apexes)
            {
                int before = ((a - i) % n + n) % n, after = ((i - a) % n + n) % n;
                if (before <= 80)
                {
                    double ve = VminKmh + (VmaxKmh - VminKmh) * before / 80.0;
                    if (ve < v) { v = ve; brake = before > 5 ? 0.9 : 0.2; thr = 0; }
                }
                if (after > 0 && after <= 100)
                {
                    double va = VminKmh + (VmaxKmh - VminKmh) * after / 100.0;
                    if (va < v) { v = va; brake = 0; thr = 1; }
                }
            }
            r.Speed[i] = v / 3.6; r.Brake[i] = brake; r.Throttle[i] = thr;
        }
        return r;
    }

    private static RecordedLap LapFrom(ReferenceTrace r, Func<int, double> speedFactor) => new()
    {
        Lap = 2, LapTimeSec = r.LapTimeSec, Complete = true, Clean = true,
        Speed = r.Speed.Select((v, i) => v * speedFactor(i)).ToArray(),
        Brake = (double[])r.Brake.Clone(), Throttle = (double[])r.Throttle.Clone(),
        Steering = new double[ReferenceTrace.BinCount], Gear = new int[ReferenceTrace.BinCount],
        Elapsed = new double[ReferenceTrace.BinCount],
    };

    [Fact]
    public void Corner_braking_before_the_line_keeps_its_approach()
    {
        var r = Circular(5, 500);
        var corners = CornerDetector.Detect(r);
        Assert.Equal(2, corners.Count);
        var t1 = corners[0];
        Assert.Equal(1, t1.Number);
        Assert.InRange(t1.ApexBin, 3, 8);
        Assert.InRange(t1.StartBin, -82, -70);           // brake point before the line (bin ~925)
        Assert.True(t1.RefBrakeBin < 0);
        Assert.True(t1.AnalysisStartBin < t1.StartBin);
        Assert.True(corners[1].EndBin < t1.AnalysisStartBin + ReferenceTrace.BinCount); // no overlap

        // Losing time in the braking zone before the line is charged to turn 1.
        var lap = LapFrom(r, i => i >= 930 ? 0.9 : 1.0);
        var rep = CornerAnalyzer.AnalyzeCorner(lap, r, t1);
        Assert.True(rep.TimeLostSec > 0.05);
        Assert.InRange(rep.StartPct, 0.9, 1.0);
        Assert.Equal(0, CornerAnalyzer.AnalyzeCorner(lap, r, corners[1]).TimeLostSec, 3);
    }

    [Fact]
    public void Corner_exiting_after_the_line_keeps_its_exit()
    {
        var r = Circular(500, 960);
        var corners = CornerDetector.Detect(r);
        Assert.Equal(2, corners.Count);
        var last = corners[1];
        Assert.InRange(last.ApexBin, 958, 963);
        Assert.True(last.EndBin >= ReferenceTrace.BinCount);        // exit continues past the line
        Assert.True(corners[0].AnalysisStartBin > last.EndBin - ReferenceTrace.BinCount);

        var lap = LapFrom(r, i => i < 50 ? 0.9 : 1.0);              // slow exit after the line
        Assert.True(CornerAnalyzer.AnalyzeCorner(lap, r, last).TimeLostSec > 0.05);
        Assert.Equal(0, CornerAnalyzer.AnalyzeCorner(lap, r, corners[0]).TimeLostSec, 3);
    }

    [Fact]
    public void Corner_with_apex_on_the_line_is_detected_once()
    {
        var corners = CornerDetector.Detect(Circular(0, 500));
        Assert.Equal(2, corners.Count);
        Assert.Contains(corners, c => ReferenceTrace.Wrap(c.ApexBin) <= 2 || ReferenceTrace.Wrap(c.ApexBin) >= 998);
    }
}
