using System;
using System.IO;
using System.Linq;
using IracingLiveCoach.Core.Coach;
using Xunit;

namespace IracingLiveCoach.Core.Tests.Coach;

public class CoachPartsTests
{
    [Fact]
    public void Every_language_has_every_phrase()
    {
        var keys = new[]
        {
            "praise", "summary.best", "summary.delta", "summary.clean", "briefing.ref", "briefing.noref",
            "answer.lastlap", "answer.nolap", "answer.worstcorner", "answer.noloss", "answer.score", "answer.noscore",
            "answer.focus", "answer.nofocus", "answer.repeat.none", "answer.unknown", "summary.head", "summary.corner",
            "summary.nocorner", "focus.Braking", "focus.TrailBraking", "focus.Throttle", "focus.Consistency",
        }.Concat(Enum.GetNames<CornerIssue>().Where(n => n != "None").Select(n => "tip." + n));

        foreach (var lang in CoachPhrases.Languages)
            foreach (var k in keys)
            {
                var en = CoachPhrases.Render("en", k);
                var txt = CoachPhrases.Render(lang, k);
                Assert.NotEqual(k, txt);
                if (lang != "en") Assert.NotEqual(en, txt);
            }
    }

    [Theory]
    [InlineData("pt", "pt-BR")]
    [InlineData("PT_br", "pt-BR")]
    [InlineData("de-DE", "de")]
    [InlineData("fr-CA", "fr")]
    [InlineData("it", "en")]
    [InlineData(null, "en")]
    public void Language_normalization(string? input, string expected) =>
        Assert.Equal(expected, CoachPhrases.Normalize(input));

    [Fact]
    public void Lap_time_uses_culture_separator()
    {
        Assert.Equal("1:32.4", CoachPhrases.LapTime("en", 92.43));
        Assert.Equal("1:32,4", CoachPhrases.LapTime("pt-BR", 92.43));
    }

    [Fact]
    public void Recorder_skips_partial_first_lap_and_flags_teleport()
    {
        var rec = new LapRecorder();
        var sim = new CoachSim { X = 1500 }; // joined mid-lap
        Assert.All(sim.DriveToLine(CoachSim.Ref), s => Assert.Null(rec.Add(s)));

        var lap = sim.DriveToLine(CoachSim.Ref).Select(rec.Add).Last(l => l != null)!;
        Assert.True(lap.Complete && lap.Clean);
        Assert.InRange(lap.LapTimeSec, 40, 80);

        // Teleport/reset in the middle of the lap.
        var samples = sim.DriveToLine(CoachSim.Ref);
        int k = samples.Count / 2;
        samples[k] = samples[k] with { LapDistPct = samples[k].LapDistPct + 0.2 };
        var bad = samples.Select(rec.Add).Last(l => l != null)!;
        Assert.False(bad.Clean);
    }

    [Fact]
    public void Reference_store_round_trip_and_bad_file()
    {
        var dir = CoachSim.TempDir();
        try
        {
            var store = new ReferenceStore(dir);
            Assert.Null(store.Load("a", "b"));
            var r = new ReferenceTrace { TrackId = "Spa / GP", CarId = "BMW M4", TrackLengthM = 7004, LapTimeSec = 140.2 };
            r.Speed[10] = 55;
            store.Save(r);
            Assert.EndsWith("spa---gp_bmw-m4.json", store.PathFor(r.TrackId, r.CarId));
            var back = store.Load(r.TrackId, r.CarId)!;
            Assert.Equal(140.2, back.LapTimeSec);
            Assert.Equal(55, back.Speed[10]);

            File.WriteAllText(store.PathFor("x", "y"), "{not json");
            Assert.Null(store.Load("x", "y"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Weekly_focus_is_per_week_and_track()
    {
        var dir = CoachSim.TempDir();
        try
        {
            var s = new WeeklyFocusStore(dir);
            var now = new DateTime(2026, 10, 19);
            for (int i = 0; i < 3; i++) s.Record("t", "c", now, 90, new TechniqueBreakdown(90, 50, 80, 95));
            Assert.Equal("TrailBraking", s.GetFocus("t", "c", now));
            Assert.Null(s.GetFocus("other", "c", now));
            Assert.Equal("2026-W43", WeeklyFocusStore.WeekKey(now));

            var reloaded = new WeeklyFocusStore(dir);
            Assert.Equal(3, reloaded.Entries.Single().Laps);
            Assert.Equal("TrailBraking", reloaded.GetFocus("t", "c", now.AddDays(14))); // falls back to all weeks
        }
        finally { Directory.Delete(dir, true); }
    }
}
