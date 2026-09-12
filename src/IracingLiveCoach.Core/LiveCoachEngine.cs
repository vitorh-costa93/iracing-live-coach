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

    private CornerFeedback BuildFeedback(CornerBaseline corner, List<TelemetrySample> samples) =>
        new(corner.Number, corner.Name, BrakingDeltaMeters: null, CorrectionDeg: null, WheelspinDetected: null);
}
