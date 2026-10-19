using System;
using System.Collections.Generic;

namespace IracingLiveCoach.Core.Coach;

/// <summary>Compares a lap with the reference corner by corner (by distance bins).
/// Time lost = sum over bins of dx/v_lap - dx/v_ref from the approach to the exit (circular).</summary>
internal static class CornerAnalyzer
{
    public const double BrakeOn = 0.1;
    public const double BrakeOff = 0.05;
    public const double ThrottleOn = 0.6;
    public const double MinLossSec = 0.05;
    private const double MinSpeedMs = 1.0;

    // Corner bins may be unwrapped across the start/finish line (see CornerSegment).
    private static int W(int i) => ReferenceTrace.Wrap(i);

    public static IReadOnlyList<CornerReport> Analyze(RecordedLap lap, ReferenceTrace r, IReadOnlyList<CornerSegment> corners)
    {
        var list = new List<CornerReport>(corners.Count);
        foreach (var c in corners) list.Add(AnalyzeCorner(lap, r, c));
        return list;
    }

    internal static CornerReport AnalyzeCorner(RecordedLap lap, ReferenceTrace r, CornerSegment c)
    {
        double dx = r.BinLengthM;
        int a0 = c.AnalysisStartBin, apex = c.ApexBin, end = c.EndBin;

        double entryLoss = 0, exitLoss = 0;
        for (int i = a0; i <= end; i++)
        {
            double d = dx / Math.Max(MinSpeedMs, lap.Speed[W(i)]) - dx / Math.Max(MinSpeedMs, r.Speed[W(i)]);
            if (i <= apex) entryLoss += d; else exitLoss += d;
        }
        double timeLost = entryLoss + exitLoss;

        int? lapBrake = FirstAbove(lap.Brake, a0, apex, BrakeOn);
        int? refBrake = FirstAbove(r.Brake, a0, apex, BrakeOn);
        double brakeDeltaM = 0;
        if (lapBrake.HasValue && refBrake.HasValue) brakeDeltaM = (lapBrake.Value - refBrake.Value) * dx;
        else if (lapBrake.HasValue) brakeDeltaM = (lapBrake.Value - c.StartBin) * dx; // ref only lifted
        else if (refBrake.HasValue) brakeDeltaM = 0; // driver did not brake at all: judged by speed

        double peakDelta = Max(lap.Brake, a0, end) - Max(r.Brake, a0, end);

        double trailDeltaM = 0;
        if (lapBrake.HasValue && refBrake.HasValue)
        {
            int lapRel = Release(lap.Brake, lapBrake.Value, end);
            int refRel = Release(r.Brake, refBrake.Value, end);
            trailDeltaM = (lapRel - refRel) * dx;
        }

        double thrDeltaM = (ThrottlePoint(lap.Throttle, a0, apex, end) - ThrottlePoint(r.Throttle, a0, apex, end)) * dx;
        double minDeltaKmh = (Min(lap.Speed, a0, end) - Min(r.Speed, a0, end)) * 3.6;

        var issue = PickIssue(timeLost, entryLoss, exitLoss, brakeDeltaM, peakDelta, trailDeltaM, thrDeltaM, minDeltaKmh);
        return new CornerReport(c.Number, ReferenceTrace.PctOf(c.StartBin),
            ReferenceTrace.PctOf(c.ApexBin), ReferenceTrace.PctOf(c.EndBin),
            Math.Round(brakeDeltaM, 1), Math.Round(peakDelta, 3), Math.Round(trailDeltaM, 1), Math.Round(thrDeltaM, 1),
            Math.Round(minDeltaKmh, 1), Math.Round(timeLost, 3), issue.ToString());
    }

    internal static CornerIssue PickIssue(double timeLost, double entryLoss, double exitLoss, double brakeDeltaM,
        double peakDelta, double trailDeltaM, double thrDeltaM, double minDeltaKmh)
    {
        if (timeLost < MinLossSec) return CornerIssue.None;

        CornerIssue Entry()
        {
            if (brakeDeltaM <= -8) return CornerIssue.BrakeTooEarly;
            if (brakeDeltaM >= 8 && minDeltaKmh <= -3) return CornerIssue.BrakeTooLate;
            if (peakDelta <= -0.15) return CornerIssue.BrakeTooSoft;
            if (trailDeltaM <= -10) return CornerIssue.TrailBrakeShort;
            if (minDeltaKmh <= -3) return CornerIssue.MinSpeedLow;
            return CornerIssue.None;
        }
        CornerIssue Exit()
        {
            if (thrDeltaM >= 10) return CornerIssue.ThrottleLate;
            if (minDeltaKmh <= -3) return CornerIssue.MinSpeedLow;
            return CornerIssue.None;
        }

        var first = entryLoss >= exitLoss ? Entry() : Exit();
        if (first != CornerIssue.None) return first;
        var second = entryLoss >= exitLoss ? Exit() : Entry();
        return second != CornerIssue.None ? second : CornerIssue.MinSpeedLow;
    }

    private static int? FirstAbove(double[] x, int from, int to, double th)
    {
        for (int i = from; i <= to; i++) if (x[W(i)] > th) return i;
        return null;
    }

    /// <summary>Last bin of continuous braking after the brake point (brake &gt; BrakeOff).</summary>
    private static int Release(double[] brake, int from, int to)
    {
        int i = from;
        while (i < to && brake[W(i + 1)] > BrakeOff) i++;
        return i;
    }

    /// <summary>First bin at/after the throttle minimum on entry where throttle reaches ThrottleOn.</summary>
    private static int ThrottlePoint(double[] thr, int from, int apex, int end)
    {
        int minBin = from;
        for (int i = from; i <= apex; i++) if (thr[W(i)] < thr[W(minBin)]) minBin = i;
        for (int i = minBin; i <= end; i++) if (thr[W(i)] >= ThrottleOn) return i;
        return end;
    }

    private static double Max(double[] x, int from, int to)
    {
        double m = double.MinValue;
        for (int i = from; i <= to; i++) m = Math.Max(m, x[W(i)]);
        return m;
    }

    private static double Min(double[] x, int from, int to)
    {
        double m = double.MaxValue;
        for (int i = from; i <= to; i++) m = Math.Min(m, x[W(i)]);
        return m;
    }
}
