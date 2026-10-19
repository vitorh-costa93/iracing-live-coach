using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IracingLiveCoach.Core.Coach;

namespace IracingLiveCoach.Core.Tests.Coach;

/// <summary>One corner of the synthetic 3 km track. EarlyBrakeM moves the driver's brake point
/// earlier (same apex speed, gentler decel); ApexSpeedDelta changes the apex speed (m/s).</summary>
internal sealed record SimCorner(double ApexM, double VminMs, double EarlyBrakeM = 0, double ApexSpeedDelta = 0);

/// <summary>Synthetic driver: straights at 70 m/s, 3 corners (apex 800/1700/2600 m), braking at
/// 12 m/s^2 with brake 0.9 trailing to 0 over the last 15 m, acceleration 6 m/s^2 at full throttle.</summary>
internal sealed class CoachSim
{
    public const double L = 3000;
    public const double Vmax = 70;
    public const double Decel = 12;
    public const double Accel = 6;
    public const double Dt = 1.0 / 60.0;

    public static readonly SimCorner[] Ref =
    {
        new(800, 25), new(1700, 35), new(2600, 45),
    };

    public double T = 100;
    public int LapNo;
    public double X = L - 60;

    public static SimCorner[] With(int turn, Func<SimCorner, SimCorner> change) =>
        Ref.Select((c, i) => i == turn - 1 ? change(c) : c).ToArray();

    /// <summary>Speed, brake and throttle at distance x for a set of corners (scale = speed factor).</summary>
    public static (double V, double Brake, double Thr) Profile(double x, SimCorner[] corners, double scale = 1.0)
    {
        double v = Vmax, brake = 0, thr = 1;
        foreach (var c in corners)
        {
            double vmin = c.VminMs + c.ApexSpeedDelta;
            double refDist = (Vmax * Vmax - c.VminMs * c.VminMs) / (2 * Decel);
            double dec = (Vmax * Vmax - vmin * vmin) / (2 * (refDist + c.EarlyBrakeM));
            if (x <= c.ApexM)
            {
                double ve = Math.Sqrt(vmin * vmin + 2 * dec * (c.ApexM - x));
                if (ve < v)
                {
                    v = ve;
                    double toApex = c.ApexM - x;
                    brake = toApex > 15 ? 0.9 : 0.9 * toApex / 15;
                    thr = 0;
                }
            }
            else
            {
                double va = Math.Sqrt(vmin * vmin + 2 * Accel * (x - c.ApexM));
                if (va < v) { v = va; brake = 0; thr = 1; }
            }
        }
        return (v * scale, brake, thr);
    }

    /// <summary>Drives until the start/finish line is crossed (inclusive of the first sample after it).</summary>
    public List<CoachSample> DriveToLine(SimCorner[] corners, double scale = 1.0, Func<double, bool>? pitAt = null)
    {
        var list = new List<CoachSample>();
        while (true)
        {
            var (v, b, th) = Profile(X, corners, scale);
            X += v * Dt;
            T += Dt;
            bool crossed = false;
            if (X >= L) { X -= L; LapNo++; crossed = true; }
            var p = Profile(X, corners, scale);
            bool pit = pitAt?.Invoke(X) ?? false;
            list.Add(new CoachSample(T, LapNo, X / L, p.V, p.Brake, p.Thr, 0.1, 4, 7000, pit, true));
            if (crossed) return list;
        }
    }

    public void Feed(CoachEngine e, IEnumerable<CoachSample> samples)
    {
        foreach (var s in samples) e.Feed(s);
    }

    /// <summary>Warm-up (crosses the line once) + one reference lap.</summary>
    public void DriveReference(CoachEngine e)
    {
        Feed(e, DriveToLine(Ref));
        Feed(e, DriveToLine(Ref));
    }

    public static CoachContext Ctx => new("monza full", "ferrari 296", L, "Practice");

    public static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "coach-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }
}
