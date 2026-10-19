using System;
using System.Collections.Generic;
using System.Linq;

namespace IracingLiveCoach.Core.Coach;

/// <summary>Corner found on the reference lap (bins). Number is the 1-based turn number.
/// AnalysisStartBin extends the approach a bit before StartBin so an early brake by the driver
/// is still inside the time-loss integral. Bins are "unwrapped" around the apex (0..BinCount-1):
/// a corner crossing the start/finish line has StartBin/AnalysisStartBin &lt; 0 or EndBin &gt;= BinCount;
/// index arrays with <see cref="ReferenceTrace.Wrap"/>.</summary>
internal sealed record CornerSegment(int Number, int StartBin, int ApexBin, int EndBin, int AnalysisStartBin,
    int? RefBrakeBin);

/// <summary>Detects corners on the reference: local speed minima whose drop from the preceding
/// peak is &gt;= 8 km/h, or with braking &gt; 0.3 on the approach. The lap is treated as circular.</summary>
internal static class CornerDetector
{
    public const double MinDropKmh = 8.0;
    public const double MinBrake = 0.3;
    public const double BrakeOnThreshold = 0.1;
    private const int MinimaWindow = 15;     // bins on each side (1.5% of the lap)
    private const int MaxSearch = 200;       // bins (20% of the lap)
    private const double ApproachExtraM = 60.0;

    private static int W(int i) => ReferenceTrace.Wrap(i);

    public static IReadOnlyList<CornerSegment> Detect(ReferenceTrace r)
    {
        const int n = ReferenceTrace.BinCount;
        var v = Smooth(r.Speed.Select(x => x * 3.6).ToArray(), 2);

        // 1) candidate apexes (0..n-1): local minima over +-MinimaWindow, circular. Ties: the
        //    earlier bin wins, so a flat stretch yields at most its first bin.
        var apexes = new List<int>();
        for (int i = 0; i < n; i++)
        {
            bool isMin = true;
            for (int d = -MinimaWindow; d <= MinimaWindow && isMin; d++)
            {
                if (d == 0) continue;
                double vj = v[W(i + d)];
                if (vj < v[i] || (vj == v[i] && d < 0)) isMin = false;
            }
            if (isMin) apexes.Add(i);
        }

        var raw = new List<(int Start, int Apex, int End, int? Brake)>();
        foreach (var a in apexes)
        {
            int s = PeakBin(v, a, -1);
            int e = PeakBin(v, a, +1);

            double drop = v[W(s)] - v[a];
            int? brakeBin = null;
            double maxBrake = 0;
            for (int j = s; j <= a; j++)
            {
                double b = r.Brake[W(j)];
                if (b > maxBrake) maxBrake = b;
                if (brakeBin is null && b > BrakeOnThreshold) brakeBin = j;
            }
            if (drop < MinDropKmh && maxBrake <= MinBrake) continue;
            int start = brakeBin.HasValue ? Math.Min(s, brakeBin.Value) : s;
            raw.Add((start, a, e, brakeBin));
        }

        // 2) merge overlapping detections (keep the slowest apex). Apexes are ascending.
        var merged = new List<(int Start, int Apex, int End, int? Brake)>();
        foreach (var c in raw)
        {
            if (merged.Count > 0 && c.Start <= merged[^1].Apex)
            {
                var p = merged[^1];
                var keep = v[c.Apex] < v[p.Apex] ? c : p;
                merged[^1] = (Math.Min(p.Start, c.Start), keep.Apex, Math.Max(p.End, c.End), p.Brake ?? c.Brake);
            }
            else merged.Add(c);
        }
        // ... and across the line: the first corner's approach reaching back past the last apex.
        while (merged.Count > 1 && merged[0].Start + n <= merged[^1].Apex)
        {
            var first = merged[0];
            var last = merged[^1];
            if (v[first.Apex] < v[last.Apex])
            {
                // Keep the first apex; the last detection becomes its approach (shifted by -n).
                merged[0] = (Math.Min(first.Start, last.Start - n), first.Apex, Math.Max(first.End, last.End - n),
                    last.Brake.HasValue ? last.Brake.Value - n : first.Brake);
                merged.RemoveAt(merged.Count - 1);
            }
            else
            {
                merged[^1] = (Math.Min(last.Start, first.Start + n), last.Apex, Math.Max(last.End, first.End + n),
                    last.Brake ?? (first.Brake.HasValue ? first.Brake.Value + n : null));
                merged.RemoveAt(0);
            }
        }

        // 3) analysis windows: extend approach, never overlapping the previous corner's exit (circular).
        int m = merged.Count;
        var ends = new int[m];
        for (int k = 0; k < m; k++)
        {
            var c = merged[k];
            int nextStart = k + 1 < m ? merged[k + 1].Start : merged[0].Start + n;
            ends[k] = Math.Max(Math.Min(c.End, nextStart), c.Apex);
        }
        int extra = r.BinLengthM > 0 ? (int)Math.Ceiling(ApproachExtraM / r.BinLengthM) : 6;
        var result = new List<CornerSegment>(m);
        for (int k = 0; k < m; k++)
        {
            var c = merged[k];
            int prevEnd = k > 0 ? ends[k - 1] : ends[m - 1] - n;
            int aStart = Math.Max(prevEnd + 1, c.Start - extra);
            aStart = Math.Min(aStart, c.Start);
            result.Add(new CornerSegment(k + 1, c.Start, c.Apex, ends[k], aStart, c.Brake));
        }
        return result;
    }

    /// <summary>Walks from the apex (dir -1 = back, +1 = forward; circular, at most MaxSearch bins)
    /// while the speed does not fall clearly below the running maximum, and returns the unwrapped bin
    /// closest to the apex that is within 0.5 km/h of that maximum (brake point side / end of acceleration).</summary>
    private static int PeakBin(double[] v, int apex, int dir)
    {
        int i = apex, maxBin = apex;
        for (int steps = 0; steps < MaxSearch; steps++)
        {
            int j = i + dir;
            if (v[W(j)] < v[W(maxBin)] - 1.0) break;
            i = j;
            if (v[W(i)] > v[W(maxBin)]) maxBin = i;
        }
        double top = v[W(maxBin)];
        for (int k = apex; k != maxBin; k += dir)
            if (v[W(k)] >= top - 0.5) return k;
        return maxBin;
    }

    /// <summary>Circular moving average (the lap wraps at the line).</summary>
    private static double[] Smooth(double[] x, int half)
    {
        int n = x.Length;
        var y = new double[n];
        for (int i = 0; i < n; i++)
        {
            double sum = 0;
            for (int d = -half; d <= half; d++) sum += x[((i + d) % n + n) % n];
            y[i] = sum / (2 * half + 1);
        }
        return y;
    }
}
