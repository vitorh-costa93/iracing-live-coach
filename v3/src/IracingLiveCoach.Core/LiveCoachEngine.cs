using System;
using System.Collections.Generic;
using System.Linq;

namespace IracingLiveCoach.Core;

/// <summary>One live telemetry tick from the iRacing SDK, already narrowed to the channels this
/// app needs. `LapDistPct` is 0-100 (matching the baseline endpoint's own corner boundary units)
/// -- the SDK's own LapDistPct is 0-1, so the caller (TelemetryReader, Task 7) must multiply by
/// 100 before constructing this record.</summary>
public record TelemetrySample(double LapDistPct, double? Brake, double? Throttle, double? SteeringRad, double? Rpm, int? Gear, double? SpeedMs);

/// <summary>The live-vs-baseline comparison for one corner, emitted once the car leaves that
/// corner's window. Any field can be null -- either the baseline itself had no history for that
/// signal (see CornerBaseline's own doc comment), or this pass through the corner didn't produce
/// enough samples to judge it. A null field means "nothing to show", never zero.</summary>
public record CornerFeedback(int CornerNumber, string? CornerName, double? BrakingDeltaMeters, double? CorrectionDeg, bool? WheelspinDetected);

/// <summary>Tracks which corner (from the cached baseline, looked up by LapDistPct -- never
/// re-detected live) the car is currently in, accumulates this pass's own samples for it, and
/// fires CornerCompleted with the live-vs-baseline comparison once the car's LapDistPct leaves
/// that corner's [StartPct, EndPct) window. Per-signal comparison math (braking point, steering
/// correction, wheelspin) is added in Tasks 4-6 -- this task only wires the corner-boundary
/// tracking itself.</summary>
public class LiveCoachEngine
{
    private readonly List<CornerBaseline> _corners;
    private readonly Dictionary<string, GearFit>? _gearModel;
    private readonly double? _trackLengthMeters;
    private CornerBaseline? _currentCorner;
    private readonly List<TelemetrySample> _cornerSamples = new();

    public event Action<CornerFeedback>? CornerCompleted;

    public LiveCoachEngine(List<CornerBaseline> corners, Dictionary<string, GearFit>? gearModel, double? trackLengthMeters)
    {
        _corners = corners ?? new();
        _gearModel = gearModel;
        _trackLengthMeters = trackLengthMeters;
    }

    private const double ApproachWindowPct = 8; // matches iracing-analytics's own APPROACH_WINDOW_PCT

    public void Update(TelemetrySample sample)
    {
        var bodyCorner = _corners.FirstOrDefault(c => sample.LapDistPct >= c.StartPct && sample.LapDistPct < c.EndPct);
        var corner = bodyCorner ?? _corners.FirstOrDefault(c =>
            sample.LapDistPct >= c.StartPct - ApproachWindowPct && sample.LapDistPct < c.StartPct);

        if (!ReferenceEquals(corner, _currentCorner))
        {
            if (_currentCorner is not null && _cornerSamples.Count > 0)
                CornerCompleted?.Invoke(BuildFeedback(_currentCorner, _cornerSamples));
            _currentCorner = corner;
            _cornerSamples.Clear();
        }

        if (corner is not null)
            _cornerSamples.Add(sample);
    }

    private CornerFeedback BuildFeedback(CornerBaseline corner, List<TelemetrySample> samples)
    {
        double? brakingDeltaMeters = null;
        if (corner.BrakingPointPct is double baselinePct)
        {
            var liveOnsetPct = FindBrakeOnset(samples);
            if (liveOnsetPct is double onsetPct)
            {
                var deltaPct = onsetPct - baselinePct;
                brakingDeltaMeters = _trackLengthMeters is double trackLength
                    ? deltaPct / 100 * trackLength
                    : deltaPct; // degraded fallback: no track length known, report raw pct-points instead of meters
            }
        }

        var inCornerSamples = samples.Where(s => s.LapDistPct >= corner.StartPct).ToList();
        var correctionDeg = ComputeWastedSteeringDeg(inCornerSamples);

        var wheelspinDetected = DetectWheelspin(corner, samples);

        return new(corner.Number, corner.Name, brakingDeltaMeters, correctionDeg, wheelspinDetected);
    }

    private const double BrakeThreshold = 0.1; // matches iracing-analytics's own BRAKE_THRESHOLD

    /// <summary>First sample, in distance order, where Brake crosses above BrakeThreshold after
    /// being below it -- the onset of braking, not "any sample with the pedal down" (which would
    /// also catch trail-braking deep into the corner). This method itself is unchanged from
    /// iracing-analytics/lib/local-coach-baselines.ts's own brakeOnsetDistance: the approach-window
    /// widening (8 pct-points before the corner's own StartPct) now happens in the caller's
    /// corner-membership lookup (see Update), so `samples` here already spans approach+body exactly
    /// like that function's own windowed `inWindow` list.</summary>
    private static double? FindBrakeOnset(List<TelemetrySample> samples)
    {
        for (var i = 1; i < samples.Count; i++)
        {
            var previous = samples[i - 1].Brake ?? 0;
            var current = samples[i].Brake ?? 0;
            if (previous < BrakeThreshold && current >= BrakeThreshold)
                return samples[i].LapDistPct;
        }
        return null;
    }

    /// <summary>Total absolute steering movement minus net displacement, over samples that have
    /// SteeringRad data -- near zero for a smooth monotonic turn-in, large when the wheel moves
    /// back and forth without progressing the angle. Mirrors
    /// iracing-analytics/lib/local-coach-baselines.ts's own wastedSteeringDeg exactly.</summary>
    private static double? ComputeWastedSteeringDeg(List<TelemetrySample> samples)
    {
        var withSteering = samples.Where(s => s.SteeringRad is not null).Select(s => s.SteeringRad!.Value).ToList();
        if (withSteering.Count < 2) return null;

        var totalMoveDeg = 0.0;
        for (var i = 1; i < withSteering.Count; i++)
            totalMoveDeg += Math.Abs((withSteering[i] - withSteering[i - 1]) * (180.0 / Math.PI));

        var netMoveDeg = Math.Abs((withSteering[^1] - withSteering[0]) * (180.0 / Math.PI));
        return totalMoveDeg - netMoveDeg;
    }

    private const double WheelspinThrottleMin = 0.85; // matches iracing-analytics's own WHEELSPIN_THROTTLE_MIN
    private const double WheelspinRpmSurplusPct = 8; // matches iracing-analytics's own WHEELSPIN_RPM_SURPLUS_PCT
    private const int WheelspinGearStableWindow = 3; // matches iracing-analytics's own WHEELSPIN_GEAR_STABLE_WINDOW

    /// <summary>Checks only the corner's EXIT half (from the midpoint of [StartPct, EndPct) to
    /// EndPct) for an RPM surplus over the pooled per-gear model, at high throttle, with a
    /// gear-stability neighbor check -- mirrors iracing-analytics/lib/local-coach-baselines.ts's
    /// own hasWheelspinInCorner exactly, including the exit-half restriction added after that
    /// codebase's own final review found entry-corner downshifts were misread as wheelspin under a
    /// whole-corner-window check, and the ±WheelspinGearStableWindow index-adjacency gear check
    /// that skips a candidate sample when any neighbor within that window reports a different gear
    /// (a nearby shift produces its own brief RPM/speed mismatch that isn't real wheelspin).
    /// Gear-adjacency is checked by array index within `samples` (which spans the approach zone and
    /// the whole corner body after Update's own windowing), not by distance -- same as the ported
    /// function's own index-ordered neighbor scan. Returns null (not false) when there's no usable
    /// data to judge with, so "never spins" stays distinguishable from "couldn't tell" at the
    /// overlay layer.</summary>
    private bool? DetectWheelspin(CornerBaseline corner, List<TelemetrySample> samples)
    {
        if (_gearModel is null) return null;

        var exitStart = (corner.StartPct + corner.EndPct) / 2;
        var anyJudged = false;

        for (var index = 0; index < samples.Count; index++)
        {
            var sample = samples[index];
            if (sample.LapDistPct < exitStart || sample.LapDistPct >= corner.EndPct) continue;
            if (sample.Throttle is not double throttle || throttle < WheelspinThrottleMin) continue;
            if (sample.Gear is not int gear || sample.Rpm is not double rpm || sample.SpeedMs is not double speed) continue;
            if (!_gearModel.TryGetValue(gear.ToString(), out var fit)) continue;

            var windowStart = Math.Max(0, index - WheelspinGearStableWindow);
            var windowEnd = Math.Min(samples.Count - 1, index + WheelspinGearStableWindow);
            var gearStable = true;
            for (var i = windowStart; i <= windowEnd; i++)
            {
                if (samples[i].Gear != gear) { gearStable = false; break; }
            }
            if (!gearStable) continue;

            anyJudged = true;
            var predicted = fit.A * speed + fit.B;
            if (predicted <= 0) continue;
            var surplusPct = (rpm - predicted) / predicted * 100;
            if (surplusPct >= WheelspinRpmSurplusPct) return true;
        }

        return anyJudged ? false : null;
    }
}
