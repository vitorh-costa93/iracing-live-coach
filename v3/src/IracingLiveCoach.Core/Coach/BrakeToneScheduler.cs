using System;
using System.Collections.Generic;

namespace IracingLiveCoach.Core.Coach;

/// <summary>Three assistive beeps before each reference brake point, evenly spaced in time
/// (BeatSpacingSec at the current speed): Beat 1 at 2 intervals, Beat 2 at 1 interval,
/// Beat 3 at the brake point.</summary>
public sealed class BrakeToneScheduler
{
    public const double BeatSpacingSec = 0.5;
    public const double MinIntervalM = 10.0;
    public const double MinSpeedMs = 8.0;

    private sealed class Point
    {
        public double Pct;
        public int NextBeat = 1;
    }

    private readonly List<Point> _points = new();

    /// <summary>Brake points as lap fractions (0..1).</summary>
    public void SetBrakePoints(IEnumerable<double> brakePointPcts)
    {
        _points.Clear();
        foreach (var p in brakePointPcts) _points.Add(new Point { Pct = p - Math.Floor(p) });
    }

    public int Count => _points.Count;

    public static double IntervalMeters(double speedMs) => Math.Max(MinIntervalM, speedMs * BeatSpacingSec);

    public IReadOnlyList<BrakeToneCue> Update(CoachSample s, double trackLengthM)
    {
        if (trackLengthM <= 0 || _points.Count == 0) return Array.Empty<BrakeToneCue>();
        List<BrakeToneCue>? cues = null; // allocated only when a beep is due (rare)
        bool silent = s.SpeedMs < MinSpeedMs || s.OnPitRoad || !s.IsOnTrack;
        double interval = IntervalMeters(s.SpeedMs);

        foreach (var p in _points)
        {
            // Signed distance to the brake point in (-L/2, L/2]: positive = still ahead.
            double d = (p.Pct - (s.LapDistPct - Math.Floor(s.LapDistPct))) * trackLengthM;
            if (d > trackLengthM / 2) d -= trackLengthM;
            if (d <= -trackLengthM / 2) d += trackLengthM;

            if (d > 2 * interval + 30 || d < -50) { p.NextBeat = 1; continue; }
            if (p.NextBeat > 3) continue;

            int due = d <= 0 ? 3 : d <= interval ? 2 : d <= 2 * interval ? 1 : 0;
            if (due < p.NextBeat) continue;
            if (d < -20) { p.NextBeat = 4; continue; } // too late to be useful
            p.NextBeat = due + 1;
            if (!silent) (cues ??= new List<BrakeToneCue>(1)).Add(new BrakeToneCue(Math.Max(0, d), due));
        }
        return cues ?? (IReadOnlyList<BrakeToneCue>)Array.Empty<BrakeToneCue>();
    }
}
