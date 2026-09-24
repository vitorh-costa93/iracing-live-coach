namespace IracingLiveCoach.Core.Telemetry;

/// <summary>Race distance helpers shared by Standings (the race length itself is <see cref="RaceLengthEstimator"/>).</summary>
public static class RaceLapEstimator
{
    /// <summary>iRacing reports 32767 laps for a session with no lap limit.</summary>
    public const int UnlimitedLaps = 32767;

    /// <summary>Race distance covered by a car: CarIdxLapCompleted + CarIdxLapDistPct, never negative. On
    /// the grid iRacing reports LapCompleted = -1 with the car just short of the line (pct ~0.99), which is
    /// -0.01 laps, i.e. 0. Null when the car is not in the world (pct &lt; 0).</summary>
    public static double? Progress(int lapCompleted, double lapDistPct) =>
        lapDistPct < 0 ? null : Math.Max(0, lapCompleted + lapDistPct);
}
