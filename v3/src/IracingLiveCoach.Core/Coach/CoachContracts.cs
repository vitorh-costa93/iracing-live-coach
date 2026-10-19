using System;
using System.Collections.Generic;

namespace IracingLiveCoach.Core.Coach;

/// <summary>One telemetry sample fed to the coach (~60 Hz). LapDistPct is 0..1.</summary>
public sealed record CoachSample(double SessionTime, int Lap, double LapDistPct, double SpeedMs,
    double Brake, double Throttle, double SteeringRad, int Gear, double Rpm, bool OnPitRoad, bool IsOnTrack);

/// <summary>Track/car the coach is working on. SessionType is informational ("Practice", "Race"...).</summary>
public sealed record CoachContext(string TrackId, string CarId, double TrackLengthM, string SessionType);

public enum SkillLevel { Beginner, Intermediate, Advanced }

public enum CoachMessageKind { Briefing, CornerTip, LapSummary, Praise, Answer }

/// <summary>A spoken/displayed message. Key is the phrase key (see <see cref="CoachPhrases"/>),
/// Args the values used to render it, Text the rendered sentence in the engine language.</summary>
public sealed record CoachMessage(CoachMessageKind Kind, string Key, IReadOnlyDictionary<string, string> Args,
    int Priority, string Text);

/// <summary>Corner-by-corner comparison vs reference. Index is the 1-based turn number.
/// Deltas are lap minus reference: BrakePointDeltaM &lt; 0 = braked earlier, ThrottleOnDeltaM &gt; 0 =
/// throttle later, TrailBrakeDeltaM &lt; 0 = released the brake earlier, MinSpeedDeltaKmh &lt; 0 = slower.
/// MainIssue is a <see cref="CornerIssue"/> name.</summary>
public sealed record CornerReport(int Index, double StartPct, double ApexPct, double EndPct,
    double BrakePointDeltaM, double PeakBrakeDelta, double TrailBrakeDeltaM, double ThrottleOnDeltaM,
    double MinSpeedDeltaKmh, double TimeLostSec, string MainIssue);

public sealed record LapReport(int Lap, double LapTimeSec, bool Valid, double DeltaToRefSec,
    IReadOnlyList<CornerReport> Corners, int TechniqueScore);

/// <summary>Assistive brake beep. Beat 1..3; Beat 3 is at the reference brake point.</summary>
public sealed record BrakeToneCue(double MetersToBrakePoint, int Beat);

/// <summary>Live trace point for the TraceWidget, with the reference values at the same distance
/// (null when there is no reference).</summary>
public sealed record TracePoint(double SessionTime, double LapDistPct, double SpeedMs, double Brake,
    double Throttle, double SteeringRad, int Gear,
    double? RefSpeedMs = null, double? RefBrake = null, double? RefThrottle = null);

/// <summary>Main issue of a corner, used as <see cref="CornerReport.MainIssue"/> and phrase keys.</summary>
public enum CornerIssue { None, BrakeTooEarly, BrakeTooLate, BrakeTooSoft, TrailBrakeShort, MinSpeedLow, ThrottleLate }

/// <summary>Technique categories (0-100 each). Also the weekly focus categories.</summary>
public sealed record TechniqueBreakdown(int Braking, int TrailBraking, int Throttle, int Consistency)
{
    public int Total => (int)Math.Round((Braking + TrailBraking + Throttle + Consistency) / 4.0);

    public string WorstCategory
    {
        get
        {
            var items = new (string Name, int Value)[]
            {
                ("Braking", Braking), ("TrailBraking", TrailBraking), ("Throttle", Throttle), ("Consistency", Consistency),
            };
            var worst = items[0];
            foreach (var i in items) if (i.Value < worst.Value) worst = i;
            return worst.Name;
        }
    }
}
