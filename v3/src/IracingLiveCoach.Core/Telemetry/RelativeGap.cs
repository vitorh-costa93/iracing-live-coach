namespace IracingLiveCoach.Core.Telemetry;

/// <summary>
/// Relative gap = how long the PLAYER would take to cover the physical distance to the other car right
/// now: the lap fraction between the two (wrapped to -0.5..0.5) x the player's own class reference lap.
/// One base for every car, any class. CarIdxEstTime is not used: each car's EstTime runs on ITS class's
/// reference curve, so between classes the difference mixes two curves and drifts while a faster class
/// passes (the user saw the Relative show an interval-like number where Kapps showed the real distance),
/// and even inside one class it is not monotonic along the lap (live start 24/09/2026). Pure, unit-tested.
/// </summary>
public static class RelativeGap
{
    /// <summary>Lap fraction from the player to the other car, wrapped to (-0.5, 0.5]; &gt; 0 = ahead.</summary>
    public static double WrappedDelta(double otherPct, double myPct)
    {
        double delta = otherPct - myPct;
        if (delta > 0.5) delta -= 1;
        else if (delta <= -0.5) delta += 1;
        return delta;
    }

    /// <summary>Seconds (always &gt;= 0) at the player's reference lap time; 0 when no lap time is known.</summary>
    public static double Seconds(double otherPct, double myPct, double myReferenceLapSeconds) =>
        myReferenceLapSeconds > 0 ? Math.Abs(WrappedDelta(otherPct, myPct)) * myReferenceLapSeconds : 0;
}
