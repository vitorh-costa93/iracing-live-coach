using IracingLiveCoach.Core.Telemetry;
using Xunit;

namespace IracingLiveCoach.Core.Tests;

public class PassageHistoryTests
{
    private static PassageHistory Car(double startPct, double lapsPerSecond, double from, double to)
    {
        var h = new PassageHistory();
        for (double t = from; t <= to; t += 0.1) h.Record(1, t, ((startPct + lapsPerSecond * (t - from)) % 1 + 1) % 1);
        return h;
    }

    [Fact]
    public void SecondsSince_IsRealTimeSinceCarWasAtThatSpot()
    {
        var h = Car(0.10, 0.01, 0, 20); // 0.01 lap/s: at 20 s the car is at 0.30
        Assert.Equal(5.0, h.SecondsSince(1, 0.25, 20)!.Value, 1);
    }

    [Fact]
    public void SecondsSince_HandlesFinishLineWrap()
    {
        var h = Car(0.95, 0.01, 0, 10); // crosses the line at 5 s
        Assert.Equal(7.0, h.SecondsSince(1, 0.98, 10)!.Value, 1);
        Assert.Equal(2.0, h.SecondsSince(1, 0.03, 10)!.Value, 1);
    }

    [Fact]
    public void SecondsSince_NullWhenSpotNotInTrail()
    {
        var h = Car(0.10, 0.01, 0, 5);
        Assert.Null(h.SecondsSince(1, 0.60, 5));
        Assert.Null(h.SecondsSince(99, 0.12, 5));
    }
}

public class PassageTrustTests
{
    [Fact] public void Trusted_KeepsPassageNearCurveGap() => Assert.Equal(5.55, PassageHistory.Trusted(5.55, 4.83), 3);
    [Fact] public void Trusted_ParkedCarFallsBackToCurveGap() => Assert.Equal(0.1, PassageHistory.Trusted(1.6, 0.1), 3);
    [Fact] public void Trusted_NoPassageUsesCurveGap() => Assert.Equal(2.0, PassageHistory.Trusted(null, 2.0), 3);
}
