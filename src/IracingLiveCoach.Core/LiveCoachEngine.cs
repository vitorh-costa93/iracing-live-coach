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
        _corners = corners;
        _gearModel = gearModel;
        _trackLengthMeters = trackLengthMeters;
    }

    public void Update(TelemetrySample sample)
    {
        var corner = _corners.FirstOrDefault(c => sample.LapDistPct >= c.StartPct && sample.LapDistPct < c.EndPct);

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

        var correctionDeg = ComputeWastedSteeringDeg(samples);

        return new(corner.Number, corner.Name, brakingDeltaMeters, correctionDeg, WheelspinDetected: null);
    }

    private const double BrakeThreshold = 0.1; // matches iracing-analytics's own BRAKE_THRESHOLD

    /// <summary>First sample, in distance order, where Brake crosses above BrakeThreshold after
    /// being below it -- the onset of braking, not "any sample with the pedal down" (which would
    /// also catch trail-braking deep into the corner). Mirrors
    /// iracing-analytics/lib/local-coach-baselines.ts's own brakeOnsetDistance exactly.</summary>
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
}
