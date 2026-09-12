using System.Collections.Generic;
using IracingLiveCoach.Core;
using Xunit;

namespace IracingLiveCoach.Core.Tests;

public class LiveCoachEngineTests
{
    private static CornerBaseline Corner(int number, double start, double end) =>
        new(number, $"Turn {number}", start, end, null, null, null, null, null, null);

    private static CornerBaseline CornerWithBraking(int number, double start, double end, double brakingPointPct) =>
        new(number, $"Turn {number}", start, end, brakingPointPct, 0.3, null, null, null, null);

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

    [Fact]
    public void Fires_CornerCompleted_exactly_once_when_the_car_leaves_a_corners_window()
    {
        var corners = new List<CornerBaseline> { Corner(1, 10, 20), Corner(2, 40, 50) };
        var engine = new LiveCoachEngine(corners, gearModel: null, trackLengthMeters: null);
        var completed = new List<CornerFeedback>();
        engine.CornerCompleted += feedback => completed.Add(feedback);

        // Approach, enter, and exit corner 1; nothing between 20 and 40 is inside any corner.
        engine.Update(new TelemetrySample(LapDistPct: 5, null, null, null, null, null, null));
        engine.Update(new TelemetrySample(LapDistPct: 12, null, null, null, null, null, null));
        engine.Update(new TelemetrySample(LapDistPct: 18, null, null, null, null, null, null));
        engine.Update(new TelemetrySample(LapDistPct: 25, null, null, null, null, null, null)); // exits corner 1's window here

        Assert.Single(completed);
        Assert.Equal(1, completed[0].CornerNumber);
        Assert.Equal("Turn 1", completed[0].CornerName);
    }

    [Fact]
    public void Does_not_fire_while_still_inside_the_same_corner()
    {
        var corners = new List<CornerBaseline> { Corner(1, 10, 20) };
        var engine = new LiveCoachEngine(corners, gearModel: null, trackLengthMeters: null);
        var completedCount = 0;
        engine.CornerCompleted += _ => completedCount++;

        engine.Update(new TelemetrySample(12, null, null, null, null, null, null));
        engine.Update(new TelemetrySample(14, null, null, null, null, null, null));
        engine.Update(new TelemetrySample(16, null, null, null, null, null, null));

        Assert.Equal(0, completedCount);
    }

    [Fact]
    public void Tracks_two_separate_corners_across_a_lap()
    {
        var corners = new List<CornerBaseline> { Corner(1, 10, 20), Corner(2, 40, 50) };
        var engine = new LiveCoachEngine(corners, gearModel: null, trackLengthMeters: null);
        var completed = new List<CornerFeedback>();
        engine.CornerCompleted += feedback => completed.Add(feedback);

        foreach (var pct in new[] { 12.0, 18.0, 25.0, 45.0, 55.0 })
            engine.Update(new TelemetrySample(pct, null, null, null, null, null, null));

        Assert.Equal(2, completed.Count);
        Assert.Equal(1, completed[0].CornerNumber);
        Assert.Equal(2, completed[1].CornerNumber);
    }

    [Fact]
    public void Reports_a_positive_braking_delta_when_the_live_lap_brakes_later_than_baseline()
    {
        // Baseline brakes at 13%; this lap starts braking at 15% -- later, within a 5891m track.
        var corners = new List<CornerBaseline> { CornerWithBraking(1, 10, 20, brakingPointPct: 13) };
        var engine = new LiveCoachEngine(corners, gearModel: null, trackLengthMeters: 5891);
        CornerFeedback? feedback = null;
        engine.CornerCompleted += f => feedback = f;

        engine.Update(new TelemetrySample(12, Brake: 0.0, null, null, null, null, null));
        engine.Update(new TelemetrySample(14, Brake: 0.0, null, null, null, null, null));
        engine.Update(new TelemetrySample(15, Brake: 0.8, null, null, null, null, null)); // onset at 15%
        engine.Update(new TelemetrySample(18, Brake: 0.8, null, null, null, null, null));
        engine.Update(new TelemetrySample(25, null, null, null, null, null, null)); // exits corner

        Assert.NotNull(feedback);
        Assert.NotNull(feedback!.BrakingDeltaMeters);
        // (15 - 13) / 100 * 5891 = 117.82
        Assert.True(feedback.BrakingDeltaMeters > 0, "later braking should be a positive delta");
        Assert.Equal(117.82, feedback.BrakingDeltaMeters!.Value, precision: 1);
    }

    [Fact]
    public void Reports_null_braking_delta_when_the_baseline_has_no_braking_point_for_this_corner()
    {
        var corners = new List<CornerBaseline> { Corner(1, 10, 20) }; // no braking baseline (all nulls)
        var engine = new LiveCoachEngine(corners, gearModel: null, trackLengthMeters: 5891);
        CornerFeedback? feedback = null;
        engine.CornerCompleted += f => feedback = f;

        engine.Update(new TelemetrySample(12, Brake: 0.0, null, null, null, null, null));
        engine.Update(new TelemetrySample(15, Brake: 0.8, null, null, null, null, null));
        engine.Update(new TelemetrySample(25, null, null, null, null, null, null));

        Assert.NotNull(feedback);
        Assert.Null(feedback!.BrakingDeltaMeters);
    }

    [Fact]
    public void Reports_null_braking_delta_when_this_pass_never_actually_braked()
    {
        var corners = new List<CornerBaseline> { CornerWithBraking(1, 10, 20, brakingPointPct: 13) };
        var engine = new LiveCoachEngine(corners, gearModel: null, trackLengthMeters: 5891);
        CornerFeedback? feedback = null;
        engine.CornerCompleted += f => feedback = f;

        engine.Update(new TelemetrySample(12, Brake: 0.0, null, null, null, null, null));
        engine.Update(new TelemetrySample(15, Brake: 0.0, null, null, null, null, null)); // never brakes
        engine.Update(new TelemetrySample(25, null, null, null, null, null, null));

        Assert.NotNull(feedback);
        Assert.Null(feedback!.BrakingDeltaMeters);
    }

    [Fact]
    public void Reports_a_braking_delta_when_the_driver_brakes_before_the_corners_own_StartPct()
    {
        // Baseline brakes at 13% (within the corner's own body); this lap brakes 3 pct-points
        // before StartPct (at 7%, corner starts at 10) -- inside the 8-pct approach window, so
        // Update must still pick this sample up even though it's outside [StartPct, EndPct).
        var corners = new List<CornerBaseline> { CornerWithBraking(1, 10, 20, brakingPointPct: 13) };
        var engine = new LiveCoachEngine(corners, gearModel: null, trackLengthMeters: 5891);
        CornerFeedback? feedback = null;
        engine.CornerCompleted += f => feedback = f;

        // Approach window is [StartPct-8, StartPct) = [2, 10) here.
        engine.Update(new TelemetrySample(4, Brake: 0.0, null, null, null, null, null));
        engine.Update(new TelemetrySample(6, Brake: 0.0, null, null, null, null, null));
        engine.Update(new TelemetrySample(7, Brake: 0.8, null, null, null, null, null)); // onset at 7%, before StartPct=10
        engine.Update(new TelemetrySample(12, Brake: 0.8, null, null, null, null, null));
        engine.Update(new TelemetrySample(25, null, null, null, null, null, null)); // exits corner

        Assert.NotNull(feedback);
        Assert.NotNull(feedback!.BrakingDeltaMeters);
        // (7 - 13) / 100 * 5891 = -353.46 -- braked earlier than baseline, a negative delta.
        Assert.True(feedback.BrakingDeltaMeters < 0, "earlier braking should be a negative delta");
        Assert.Equal(-353.46, feedback.BrakingDeltaMeters!.Value, precision: 1);
    }

    [Fact]
    public void Fires_CornerCompleted_exactly_once_even_though_samples_span_approach_and_body()
    {
        var corners = new List<CornerBaseline> { Corner(1, 10, 20) };
        var engine = new LiveCoachEngine(corners, gearModel: null, trackLengthMeters: null);
        var completedCount = 0;
        engine.CornerCompleted += _ => completedCount++;

        // 3 (before approach window), 4 (enters approach window, 10-8=2), 9 (still approach), 10
        // (enters body), 15 (body), 25 (exits) -- the approach->body transition at pct 10 must not
        // itself trigger a completion; only leaving the corner altogether at pct 25 should.
        foreach (var pct in new[] { 3.0, 4.0, 9.0, 10.0, 15.0, 25.0 })
            engine.Update(new TelemetrySample(pct, null, null, null, null, null, null));

        Assert.Equal(1, completedCount);
    }

    [Fact]
    public void Reports_wasted_steering_motion_for_the_corner_just_completed()
    {
        var corners = new List<CornerBaseline> { Corner(1, 10, 20) };
        var engine = new LiveCoachEngine(corners, gearModel: null, trackLengthMeters: null);
        CornerFeedback? feedback = null;
        engine.CornerCompleted += f => feedback = f;

        // A wheel movement that goes out to +5 degrees then back to 0 within the corner window --
        // total absolute movement (10 deg) minus net displacement (0 deg) = 10 degrees wasted.
        engine.Update(new TelemetrySample(12, null, null, SteeringRad: 0.0, null, null, null));
        engine.Update(new TelemetrySample(15, null, null, SteeringRad: DegreesToRadians(5), null, null, null));
        engine.Update(new TelemetrySample(18, null, null, SteeringRad: 0.0, null, null, null));
        engine.Update(new TelemetrySample(25, null, null, null, null, null, null)); // exits corner

        Assert.NotNull(feedback);
        Assert.NotNull(feedback!.CorrectionDeg);
        Assert.Equal(10.0, feedback.CorrectionDeg!.Value, precision: 1);
    }

    [Fact]
    public void Reports_null_correction_when_fewer_than_two_steering_samples_are_available()
    {
        var corners = new List<CornerBaseline> { Corner(1, 10, 20) };
        var engine = new LiveCoachEngine(corners, gearModel: null, trackLengthMeters: null);
        CornerFeedback? feedback = null;
        engine.CornerCompleted += f => feedback = f;

        engine.Update(new TelemetrySample(15, null, null, SteeringRad: 0.1, null, null, null)); // only 1 sample with steering data
        engine.Update(new TelemetrySample(25, null, null, null, null, null, null));

        Assert.NotNull(feedback);
        Assert.Null(feedback!.CorrectionDeg);
    }

    [Fact]
    public void Detects_wheelspin_when_exit_half_RPM_exceeds_the_gear_model_by_the_threshold()
    {
        var corners = new List<CornerBaseline> { Corner(1, 10, 20) }; // exit half is [15, 20)
        var gearModel = new Dictionary<string, GearFit> { ["3"] = new GearFit(A: 100, B: 0) }; // RPM = 100*speed
        var engine = new LiveCoachEngine(corners, gearModel, trackLengthMeters: null);
        CornerFeedback? feedback = null;
        engine.CornerCompleted += f => feedback = f;

        // Entry half (before 15): normal throttle, doesn't matter for this signal.
        engine.Update(new TelemetrySample(12, null, Throttle: 0.5, null, Rpm: 3000, Gear: 3, SpeedMs: 30));
        // Exit half: predicted RPM = 100*30 = 3000; actual 3450 is a 15% surplus at high throttle.
        engine.Update(new TelemetrySample(16, null, Throttle: 0.95, null, Rpm: 3450, Gear: 3, SpeedMs: 30));
        engine.Update(new TelemetrySample(25, null, null, null, null, null, null)); // exits corner

        Assert.NotNull(feedback);
        Assert.True(feedback!.WheelspinDetected);
    }

    [Fact]
    public void Does_not_detect_wheelspin_when_exit_half_RPM_matches_the_gear_model()
    {
        var corners = new List<CornerBaseline> { Corner(1, 10, 20) };
        var gearModel = new Dictionary<string, GearFit> { ["3"] = new GearFit(A: 100, B: 0) };
        var engine = new LiveCoachEngine(corners, gearModel, trackLengthMeters: null);
        CornerFeedback? feedback = null;
        engine.CornerCompleted += f => feedback = f;

        engine.Update(new TelemetrySample(16, null, Throttle: 0.95, null, Rpm: 3000, Gear: 3, SpeedMs: 30)); // matches model exactly
        engine.Update(new TelemetrySample(25, null, null, null, null, null, null));

        Assert.NotNull(feedback);
        Assert.False(feedback!.WheelspinDetected);
    }

    [Fact]
    public void Does_not_detect_wheelspin_when_a_nearby_gear_shift_explains_the_RPM_surplus()
    {
        var corners = new List<CornerBaseline> { Corner(1, 10, 20) }; // exit half is [15, 20)
        var gearModel = new Dictionary<string, GearFit> { ["3"] = new GearFit(A: 100, B: 0) }; // RPM = 100*speed
        var engine = new LiveCoachEngine(corners, gearModel, trackLengthMeters: null);
        CornerFeedback? feedback = null;
        engine.CornerCompleted += f => feedback = f;

        // Same 15% RPM surplus as the "detects wheelspin" test above (predicted 3000, actual 3450),
        // but a neighboring sample within the +/-3 index window reports a different gear (a shift
        // just happened nearby) -- the gear-stability gate must suppress this as a false positive.
        engine.Update(new TelemetrySample(12, null, Throttle: 0.5, null, Rpm: 3000, Gear: 3, SpeedMs: 30));
        engine.Update(new TelemetrySample(16, null, Throttle: 0.95, null, Rpm: 3450, Gear: 3, SpeedMs: 30)); // would trigger under old code
        engine.Update(new TelemetrySample(17, null, Throttle: 0.95, null, Rpm: 3400, Gear: 4, SpeedMs: 34)); // neighbor gear differs -- a shift
        engine.Update(new TelemetrySample(25, null, null, null, null, null, null)); // exits corner

        Assert.NotNull(feedback);
        Assert.NotEqual(true, feedback!.WheelspinDetected);
    }

    [Fact]
    public void Reports_null_wheelspin_when_there_is_no_gear_model_for_the_gear_used()
    {
        var corners = new List<CornerBaseline> { Corner(1, 10, 20) };
        var engine = new LiveCoachEngine(corners, gearModel: null, trackLengthMeters: null); // no gear model at all
        CornerFeedback? feedback = null;
        engine.CornerCompleted += f => feedback = f;

        engine.Update(new TelemetrySample(16, null, Throttle: 0.95, null, Rpm: 3450, Gear: 3, SpeedMs: 30));
        engine.Update(new TelemetrySample(25, null, null, null, null, null, null));

        Assert.NotNull(feedback);
        Assert.Null(feedback!.WheelspinDetected);
    }

    [Fact]
    public void Ignores_low_throttle_samples_when_checking_for_wheelspin()
    {
        var corners = new List<CornerBaseline> { Corner(1, 10, 20) };
        var gearModel = new Dictionary<string, GearFit> { ["3"] = new GearFit(A: 100, B: 0) };
        var engine = new LiveCoachEngine(corners, gearModel, trackLengthMeters: null);
        CornerFeedback? feedback = null;
        engine.CornerCompleted += f => feedback = f;

        // Big RPM surplus, but throttle is only 50% -- below the 85% gate, so this isn't spin, it's
        // more likely a lift or a gearshift artifact (same reasoning as the original TS detector).
        engine.Update(new TelemetrySample(16, null, Throttle: 0.5, null, Rpm: 3450, Gear: 3, SpeedMs: 30));
        engine.Update(new TelemetrySample(25, null, null, null, null, null, null));

        Assert.NotNull(feedback);
        Assert.Null(feedback!.WheelspinDetected);
    }
}
