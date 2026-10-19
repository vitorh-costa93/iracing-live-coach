using IracingLiveCoach.Core.Coach;
using IracingLiveCoach.OverlayHost.Coach;

namespace IracingLiveCoach.OverlayHost.Widgets;

/// <summary>Fictitious coach state for the simulation mode (T key / edit mode) and the Control Center
/// preview of the three coach widgets -- a synthetic 4-corner lap with a reference ghost that brakes a
/// touch later. Never used as a stand-in for real telemetry.</summary>
public static class CoachSimulationData
{
    private static readonly double[] Corners = [0.12, 0.37, 0.62, 0.85];
    private const double LapSeconds = 32.0;

    private static double Trap(double d, double a, double peakStart, double peakEnd, double b)
    {
        if (d <= a || d >= b) return 0;
        if (d < peakStart) return (d - a) / (peakStart - a);
        if (d <= peakEnd) return 1;
        return (b - d) / (b - peakEnd);
    }

    private static (double Brake, double Throttle, double SpeedMs, double Steer) Model(double phase, double brakeShift, double amp)
    {
        double brake = 0, close = 0, steer = 0;
        int n = 0;
        foreach (var c in Corners)
        {
            double d = phase - c;
            brake = Math.Max(brake, Trap(d, -0.06 + brakeShift, -0.05 + brakeShift, -0.025, 0.015) * amp);
            close = Math.Max(close, Math.Max(0, 1 - Math.Abs(d + 0.01) / 0.07));
            steer += (n++ % 2 == 0 ? 1 : -1) * 2.2 * Math.Max(0, 1 - Math.Abs(d) / 0.04);
        }
        double throttle = Math.Clamp(1.0 - brake * 1.4 - close * 0.35, 0, 1);
        return (brake, throttle, 78 - 46 * close, steer);
    }

    public static CoachSnapshot Snapshot(double seconds)
    {
        const int hz = 30;
        int count = (int)(CoachEngine.LiveTraceSeconds * hz);
        var trace = new List<TracePoint>(count);
        for (int i = 0; i < count; i++)
        {
            double t = seconds - CoachEngine.LiveTraceSeconds + (i + 1) / (double)hz;
            double phase = (t / LapSeconds) % 1.0;
            if (phase < 0) phase += 1;
            var me = Model(phase, 0, 0.95);
            var rf = Model(phase, 0.004, 1.0);
            int gear = me.SpeedMs switch { < 30 => 2, < 45 => 3, < 58 => 4, < 70 => 5, _ => 6 };
            trace.Add(new TracePoint(t, phase, me.SpeedMs, me.Brake, me.Throttle, me.Steer, gear, rf.SpeedMs + 0.8, rf.Brake, rf.Throttle));
        }

        var report = new LapReport(7, 92.456, true, 0.312,
        [
            new CornerReport(1, 0.08, 0.12, 0.16, -9, -0.04, -3, 4, -2.0, 0.18, nameof(CornerIssue.BrakeTooEarly)),
            new CornerReport(2, 0.33, 0.37, 0.41, 2, -0.22, -12, 3, -4.1, 0.11, nameof(CornerIssue.BrakeTooSoft)),
            new CornerReport(3, 0.58, 0.62, 0.66, 1, 0.02, 0, 12, -1.5, 0.07, nameof(CornerIssue.ThrottleLate)),
            new CornerReport(4, 0.81, 0.85, 0.89, 0, 0.0, 0, 1, 0.8, -0.03, nameof(CornerIssue.None)),
        ], 78);

        return new CoachSnapshot(new CoachSettings(), trace, true, report, new TechniqueBreakdown(82, 71, 85, 74), 76,
            new CoachMessage(CoachMessageKind.CornerTip, "tip.BrakeTooEarly", new Dictionary<string, string>(), 1,
                "Turn 1: you braked 9 meters early. Brake a little later."),
            nameof(TechniqueBreakdown.TrailBraking));
    }
}
