using IracingLiveCoach.Core.Telemetry;
using Xunit;

namespace IracingLiveCoach.Core.Tests;

public class RelativeDebugLogTests
{
    [Fact]
    public void RelativeLine_HasOneFieldPerHeaderColumnAndInvariantNumbers()
    {
        var row = new RelativeDebugRow("ahead", -1, 7, 3, 92.5, true, 0.5123456, 0.0123, 41.2, 40.1, 0.5, null, 1.1, 1.3, false, 3, 12);
        var line = RelativeDebugFormat.RelativeLine(new DateTime(2026, 10, 28, 13, 5, 9, 42), 123.456, "Race", 4, row);
        Assert.Equal(RelativeDebugFormat.RelativeHeader.Split(',').Length, line.Split(',').Length);
        Assert.StartsWith("2026-10-28T13:05:09.042,123.456,Race,4,ahead,-1,7,3,92.5,1,0.5123,0.0123,41.2,40.1,0.5,,1.1,1.3,0,3,12", line);
    }

    [Fact]
    public void StandingsLine_MatchesHeader()
    {
        var line = RelativeDebugFormat.StandingsLine(DateTime.Now, 1, "Race", 4, new StandingsDebugRow(2, 1, 3, 4.25, null, "1.5", "estTime-forward"));
        Assert.Equal(RelativeDebugFormat.StandingsHeader.Split(',').Length, line.Split(',').Length);
        Assert.EndsWith(",4.25,,1.5,estTime-forward", line);
        Assert.Equal("2L", RelativeDebugFormat.Interval(new IntervalValue(null, 2)));
        Assert.Equal("", RelativeDebugFormat.Interval(IntervalValue.None));
    }

    [Fact]
    public void Sampler_FiresAtMostEveryInterval()
    {
        var s = new DebugSampler(TimeSpan.FromMilliseconds(500));
        var t = new DateTime(2026, 1, 1);
        Assert.True(s.Due(t));
        Assert.False(s.Due(t.AddMilliseconds(200)));
        Assert.True(s.Due(t.AddMilliseconds(520)));
    }

    [Fact]
    public void Writer_RotatesThenStopsAtLimit()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rdl-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "t.csv");
        try
        {
            var w = new DebugCsvWriter(path, "h", maxFileBytes: 40, maxFiles: 2);
            for (int i = 0; i < 20; i++) w.Append(new[] { "0123456789" });
            Assert.True(File.Exists(path));
            Assert.True(File.Exists(Path.Combine(dir, "t-2.csv")));
            Assert.False(File.Exists(Path.Combine(dir, "t-3.csv")));
            Assert.Equal("h", File.ReadLines(path).First());
            Assert.Equal("h", File.ReadLines(Path.Combine(dir, "t-2.csv")).First());
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void Writer_SwallowsIoFailure()
    {
        var w = new DebugCsvWriter("Z:\\nope\\<>|\\x.csv", "h");
        w.Append(new[] { "a" });
        w.Append(new[] { "b" });
    }

    [Fact]
    public void Branches_AreReported()
    {
        IntervalInput Car(int pos, double prog, double? est, double? gap = null) => new(1, pos, pos, prog, est, gap);
        Assert.Equal("laps", ClassIntervals.RaceIntervalWithBranch(Car(2, 3.2, 20), Car(1, 5.3, 60), 90).Branch);
        Assert.Equal("finalTimes", ClassIntervals.RaceIntervalWithBranch(Car(2, 35, null, 17.5), Car(1, 35, null, 0), 67).Branch);
        Assert.Equal("estTime-forward", ClassIntervals.RaceIntervalWithBranch(Car(2, 5.1, 10), Car(1, 5.4, 15), 100).Branch);
        Assert.Equal("estTime-nearest", ClassIntervals.RaceIntervalWithBranch(Car(2, 5.4, 15), Car(1, 5.1, 10), 100).Branch);
        Assert.Equal("f2time-fallback", ClassIntervals.RaceIntervalWithBranch(Car(2, 5.1, null, 12), Car(1, 5.2, 5, 10), null).Branch);
        Assert.Equal("none", ClassIntervals.RaceIntervalWithBranch(Car(2, 5.1, null), Car(1, 5.2, null), null).Branch);
        var all = ClassIntervals.ComputeWithBranch(new[] { Car(1, 5.4, 15), Car(2, 5.1, 10) }, true);
        Assert.Equal("leader", all[0].Branch);
        Assert.Equal("estTime-forward", all[1].Branch);
    }
}
