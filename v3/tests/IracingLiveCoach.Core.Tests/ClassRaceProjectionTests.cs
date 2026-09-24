using IracingLiveCoach.Core.Telemetry;

namespace IracingLiveCoach.Core.Tests;

public sealed class ClassRaceProjectionTests
{
    [Fact]
    public void Matches_the_kapps_multiclass_print()
    {
        // Live 24/09/2026, 1499.85 s left. Last-5 averages: GTP leader 75.566, LMP2 82.783, GT3 86.296.
        var leaders = new[]
        {
            new ClassLeader(1, 15.378, 75.566),
            new ClassLeader(2, 14.259, 82.783),
            new ClassLeader(3, 13.494, 86.296),
        };
        var p = ClassRaceProjection.Compute(leaders, overallLeaderClassId: 1, timeRemaining: 1499.85);
        Assert.InRange(p[1], 35.20, 35.24); // Kapps 35.22
        Assert.InRange(p[2], 33.00, 33.10); // Kapps 33.05
        Assert.InRange(p[3], 31.48, 31.58); // Kapps 31.53
    }

    [Fact]
    public void No_leader_pace_no_projection() =>
        Assert.Empty(ClassRaceProjection.Compute(new[] { new ClassLeader(1, 3, null) }, 1, 600));

    [Fact]
    public void Lap_history_averages_the_last_five_settled_laps()
    {
        var h = new LapHistory();
        h.Update(7, 1, 91.0);
        for (int lap = 2; lap <= 7; lap++) { h.Update(7, lap, 70.0); h.Update(7, lap, 80.0); } // lag tick then settled value
        Assert.Equal(80.0, h.RecentAverage(7));
        Assert.Null(h.RecentAverage(8));
        h.Update(8, 0, 90); // no lap completed yet: ignored
        Assert.Null(h.RecentAverage(8));
    }
}
