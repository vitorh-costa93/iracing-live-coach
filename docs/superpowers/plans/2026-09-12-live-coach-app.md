# Live Coach App Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `iracing-live-coach`, a Windows desktop app that reads live iRacing telemetry, compares it corner-by-corner against this driver's own historical baselines (fetched from `iracing-analytics`), and shows the deltas as a transparent on-screen overlay.

**Architecture:** A pure `IracingLiveCoach.Core` class library (models, `LiveCoachEngine`, `BaselineSync`) with no UI/SDK dependencies, fully unit-testable with xUnit. A separate `IracingLiveCoach.App` WPF project wires the SDK reader (`IRSDKSharper` NuGet package) and the overlay window on top of that library. The Core library gets full TDD coverage; the App project (SDK integration + WPF rendering) is verified by `dotnet build` only — it cannot be meaningfully unit-tested without a running iRacing session or an interactive display, exactly as the spec's own Testing section says.

**Tech Stack:** .NET 8 (already installed and verified: `dotnet --version` → 8.0.425), C#, xUnit, `IRSDKSharper` NuGet package (verified real package: `dotnet add package IRSDKSharper`, GitHub `mherbold/IRSDKSharper`, main class `IRacingSdk`), WPF (`net8.0-windows`, `UseWPF=true`).

**Spec:** `docs/superpowers/specs/2026-09-12-live-coach-overlay-design.md`

## Global Constraints

- A `null` field in the baseline JSON means "not enough history to trust this signal for this corner" — never treat it as zero, never fabricate a comparison for it.
- `corners: []` in the baseline response is a valid, non-error state ("no history yet") — the app must handle it without crashing (idle/no-comparison state, not an exception).
- The app must never block driving on a failed network sync — always fall back to the last successfully cached baseline file, or an empty/idle state if none exists yet.
- JSON field names from the endpoint are camelCase (`trackLengthMeters`, `brakingPointPct`, etc.) — deserialize with `JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }`, not per-property attributes, so new fields the endpoint might add later don't need a matching C# attribute to round-trip.

---

### Task 1: Solution and project scaffolding

**Files:**
- Create: `IracingLiveCoach.sln`
- Create: `src/IracingLiveCoach.Core/IracingLiveCoach.Core.csproj`
- Create: `src/IracingLiveCoach.Core/Class1.cs` (deleted again in Task 2 once real files exist — `dotnet new classlib` creates it)
- Create: `src/IracingLiveCoach.App/IracingLiveCoach.App.csproj`
- Create: `src/IracingLiveCoach.App/App.xaml`, `src/IracingLiveCoach.App/App.xaml.cs` (from the WPF template)
- Create: `tests/IracingLiveCoach.Core.Tests/IracingLiveCoach.Core.Tests.csproj`
- Create: `.gitignore` (standard .NET one)

**Interfaces:**
- Consumes: nothing from other tasks.
- Produces: a solution that builds clean via `dotnet build`, with a class library project, a WPF app project (referencing the library), and an xUnit test project (referencing the library). Later tasks add real files to these projects.

- [ ] **Step 1: Create the solution and projects**

```bash
dotnet new sln -n IracingLiveCoach
dotnet new classlib -n IracingLiveCoach.Core -o src/IracingLiveCoach.Core
dotnet new wpf -n IracingLiveCoach.App -o src/IracingLiveCoach.App
dotnet new xunit -n IracingLiveCoach.Core.Tests -o tests/IracingLiveCoach.Core.Tests
dotnet sln add src/IracingLiveCoach.Core/IracingLiveCoach.Core.csproj
dotnet sln add src/IracingLiveCoach.App/IracingLiveCoach.App.csproj
dotnet sln add tests/IracingLiveCoach.Core.Tests/IracingLiveCoach.Core.Tests.csproj
dotnet add src/IracingLiveCoach.App/IracingLiveCoach.App.csproj reference src/IracingLiveCoach.Core/IracingLiveCoach.Core.csproj
dotnet add tests/IracingLiveCoach.Core.Tests/IracingLiveCoach.Core.Tests.csproj reference src/IracingLiveCoach.Core/IracingLiveCoach.Core.csproj
```

- [ ] **Step 2: Add a standard .NET `.gitignore`**

```bash
dotnet new gitignore
```

- [ ] **Step 3: Remove the template's placeholder class**

Delete `src/IracingLiveCoach.Core/Class1.cs` — it has no purpose beyond letting `dotnet new classlib` scaffold the project; Task 2 adds real files.

- [ ] **Step 4: Verify the solution builds and the template test passes**

Run: `dotnet build`
Expected: `Build succeeded.` for all 3 projects (Core, App, Tests).

Run: `dotnet test`
Expected: the xUnit template's own placeholder test passes (1 passed) — this proves the test project can find and run tests against the Core project reference before any real code exists.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "chore: scaffold solution (Core lib, WPF app, xUnit tests)"
```

---

### Task 2: Core models + JSON deserialization

**Files:**
- Create: `src/IracingLiveCoach.Core/Models.cs`
- Test: `tests/IracingLiveCoach.Core.Tests/ModelsTests.cs`

**Interfaces:**
- Consumes: nothing from other tasks.
- Produces:
  - `record GearFit(double A, double B);`
  - `record CornerBaseline(int Number, string? Name, double StartPct, double EndPct, double? BrakingPointPct, double? BrakingPointStdDev, double? CorrectionBaselineDeg, double? WheelspinRatePct, double? LapTimeContributionSeconds, double? LapTimeStdDev);`
  - `record BaselineResponse(string Status, double? TrackLengthMeters, Dictionary<string, GearFit>? GearModel, List<CornerBaseline> Corners);`
  - `static class JsonOptions { public static readonly JsonSerializerOptions Baseline; }` — the shared `JsonSerializerOptions` with camelCase naming, used by every JSON deserialization in this codebase (Task 6's `BaselineSync` reuses this).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/IracingLiveCoach.Core.Tests/ModelsTests.cs
using System.Text.Json;
using IracingLiveCoach.Core;
using Xunit;

namespace IracingLiveCoach.Core.Tests;

public class ModelsTests
{
    private const string SampleJson = """
    {
      "status": "ok",
      "trackLengthMeters": 5891,
      "gearModel": { "3": { "a": 42.1, "b": 850 }, "4": { "a": 38.7, "b": 920 } },
      "corners": [
        {
          "number": 1,
          "name": "Copse",
          "startPct": 2.1,
          "endPct": 5.4,
          "brakingPointPct": 3.0,
          "brakingPointStdDev": 0.3,
          "correctionBaselineDeg": 6.2,
          "wheelspinRatePct": 12.5,
          "lapTimeContributionSeconds": 4.8,
          "lapTimeStdDev": 0.15
        },
        {
          "number": 2,
          "name": null,
          "startPct": 10.0,
          "endPct": 15.0,
          "brakingPointPct": null,
          "brakingPointStdDev": null,
          "correctionBaselineDeg": null,
          "wheelspinRatePct": null,
          "lapTimeContributionSeconds": null,
          "lapTimeStdDev": null
        }
      ]
    }
    """;

    [Fact]
    public void Deserializes_the_real_endpoint_shape_including_nulls()
    {
        var result = JsonSerializer.Deserialize<BaselineResponse>(SampleJson, JsonOptions.Baseline);

        Assert.NotNull(result);
        Assert.Equal("ok", result!.Status);
        Assert.Equal(5891, result.TrackLengthMeters);
        Assert.Equal(2, result.Corners.Count);
        Assert.Equal(42.1, result.GearModel!["3"].A);
        Assert.Equal(850, result.GearModel["3"].B);

        var namedCorner = result.Corners[0];
        Assert.Equal("Copse", namedCorner.Name);
        Assert.Equal(3.0, namedCorner.BrakingPointPct);

        var unnamedCorner = result.Corners[1];
        Assert.Null(unnamedCorner.Name);
        Assert.Null(unnamedCorner.BrakingPointPct);
        Assert.Null(unnamedCorner.WheelspinRatePct);
    }

    [Fact]
    public void Deserializes_an_empty_corners_response_without_error()
    {
        const string json = """{"status":"ok","trackLengthMeters":null,"corners":[]}""";

        var result = JsonSerializer.Deserialize<BaselineResponse>(json, JsonOptions.Baseline);

        Assert.NotNull(result);
        Assert.Empty(result!.Corners);
        Assert.Null(result.TrackLengthMeters);
        Assert.Null(result.GearModel);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter ModelsTests`
Expected: FAIL — `IracingLiveCoach.Core.Models`/`JsonOptions` do not exist yet (compile error).

- [ ] **Step 3: Implement the models**

```csharp
// src/IracingLiveCoach.Core/Models.cs
using System.Text.Json;

namespace IracingLiveCoach.Core;

/// <summary>
/// Shared JSON options for every deserialization in this app -- camelCase naming policy matches
/// iracing-analytics's endpoint field names (trackLengthMeters, brakingPointPct, etc.) without
/// needing a [JsonPropertyName] attribute on every property, so a new field the endpoint adds
/// later round-trips automatically as long as the C# property name matches it case-insensitively
/// once camelCased.
/// </summary>
public static class JsonOptions
{
    public static readonly JsonSerializerOptions Baseline = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}

/// <summary>RPM = A*speed + B for one gear, pooled across this driver's own historical laps
/// (see iracing-analytics's lib/local-coach-baselines.ts buildGearModel for how this is computed
/// server-side). Used live to judge "is the engine spinning faster than this car/gear combo
/// normally does at this speed" -- the same corner-relative-baseline philosophy as every other
/// signal in this app.</summary>
public record GearFit(double A, double B);

/// <summary>One corner's historical baseline for all four coaching signals. Any field can be
/// null -- not enough historical laps to trust that specific signal for that specific corner.
/// A null field must never be treated as zero or compared against; skip that signal for that
/// corner instead.</summary>
public record CornerBaseline(
    int Number,
    string? Name,
    double StartPct,
    double EndPct,
    double? BrakingPointPct,
    double? BrakingPointStdDev,
    double? CorrectionBaselineDeg,
    double? WheelspinRatePct,
    double? LapTimeContributionSeconds,
    double? LapTimeStdDev);

/// <summary>The full response from GET /api/telemetry/local-coach/baselines. `Corners` can be
/// an empty list -- "no history yet for this car/track", not an error.</summary>
public record BaselineResponse(
    string Status,
    double? TrackLengthMeters,
    Dictionary<string, GearFit>? GearModel,
    List<CornerBaseline> Corners);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter ModelsTests`
Expected: PASS (2 passed)

- [ ] **Step 5: Commit**

```bash
git add src/IracingLiveCoach.Core/Models.cs tests/IracingLiveCoach.Core.Tests/ModelsTests.cs
git commit -m "feat: core models + JSON deserialization for the baselines endpoint contract"
```

---

### Task 3: LiveCoachEngine corner tracking (orchestration only, no signal math yet)

**Files:**
- Create: `src/IracingLiveCoach.Core/LiveCoachEngine.cs`
- Test: `tests/IracingLiveCoach.Core.Tests/LiveCoachEngineTests.cs`

**Interfaces:**
- Consumes: `CornerBaseline`, `BaselineResponse` from Task 2.
- Produces:
  - `record TelemetrySample(double LapDistPct, double? Brake, double? Throttle, double? SteeringRad, double? Rpm, int? Gear, double? SpeedMs);`
  - `record CornerFeedback(int CornerNumber, string? CornerName, double? BrakingDeltaMeters, double? CorrectionDeg, bool? WheelspinDetected);` (no lap-time field -- out of scope per the spec's own "Out of scope" section for this plan)
  - `class LiveCoachEngine { public LiveCoachEngine(List<CornerBaseline> corners, Dictionary<string, GearFit>? gearModel, double? trackLengthMeters); public event Action<CornerFeedback>? CornerCompleted; public void Update(TelemetrySample sample); }`

This task wires corner lookup and corner-boundary-crossing detection only. `CornerCompleted` fires with `BrakingDeltaMeters = null`, `CorrectionDeg = null`, `WheelspinDetected = null` for now (Tasks 4-6 fill these in) -- the point of this task is proving the orchestration (which corner is the car in right now, when does it end) works before adding any per-signal math.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/IracingLiveCoach.Core.Tests/LiveCoachEngineTests.cs
using System.Collections.Generic;
using IracingLiveCoach.Core;
using Xunit;

namespace IracingLiveCoach.Core.Tests;

public class LiveCoachEngineTests
{
    private static CornerBaseline Corner(int number, double start, double end) =>
        new(number, $"Turn {number}", start, end, null, null, null, null, null, null);

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
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter LiveCoachEngineTests`
Expected: FAIL — `LiveCoachEngine`/`TelemetrySample`/`CornerFeedback` do not exist yet.

- [ ] **Step 3: Implement the orchestration**

```csharp
// src/IracingLiveCoach.Core/LiveCoachEngine.cs
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
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter LiveCoachEngineTests`
Expected: PASS (3 passed)

- [ ] **Step 5: Commit**

```bash
git add src/IracingLiveCoach.Core/LiveCoachEngine.cs tests/IracingLiveCoach.Core.Tests/LiveCoachEngineTests.cs
git commit -m "feat: LiveCoachEngine corner-boundary tracking (orchestration only)"
```

---

### Task 4: Braking-point live comparison

**Files:**
- Modify: `src/IracingLiveCoach.Core/LiveCoachEngine.cs`
- Modify: `tests/IracingLiveCoach.Core.Tests/LiveCoachEngineTests.cs`

**Interfaces:**
- Consumes: `LiveCoachEngine`, `TelemetrySample`, `CornerFeedback` from Task 3 (same file, extended in place).
- Produces: `CornerFeedback.BrakingDeltaMeters` populated when the baseline has `BrakingPointPct` and this pass produced a detectable brake onset.

`BrakingDeltaMeters` sign convention: **positive means braked LATER than the baseline** (a coaching-relevant "you're braking later here" signal), computed as `(liveOnsetPct - baseline.BrakingPointPct) / 100 * trackLengthMeters`. If `trackLengthMeters` is null, fall back to reporting the raw percentage delta scaled by 1 (i.e. treat 1 pct-point as 1 "meter" — document this fallback clearly; it's a degraded-but-non-crashing behavior, not a silent wrong unit).

- [ ] **Step 1: Write the failing test**

```csharp
// Append to tests/IracingLiveCoach.Core.Tests/LiveCoachEngineTests.cs

private static CornerBaseline CornerWithBraking(int number, double start, double end, double brakingPointPct) =>
    new(number, $"Turn {number}", start, end, brakingPointPct, 0.3, null, null, null, null);

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
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter LiveCoachEngineTests`
Expected: FAIL — the 3 new tests fail (BrakingDeltaMeters is always null currently).

- [ ] **Step 3: Implement braking-onset detection**

Add this private method to `LiveCoachEngine` (below `BuildFeedback`):

```csharp
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
```

Replace `BuildFeedback`'s body with:

```csharp
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

        return new(corner.Number, corner.Name, brakingDeltaMeters, CorrectionDeg: null, WheelspinDetected: null);
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter LiveCoachEngineTests`
Expected: PASS (6 passed)

- [ ] **Step 5: Commit**

```bash
git add src/IracingLiveCoach.Core/LiveCoachEngine.cs tests/IracingLiveCoach.Core.Tests/LiveCoachEngineTests.cs
git commit -m "feat: live braking-point comparison in LiveCoachEngine"
```

---

### Task 5: Steering-correction live comparison

**Files:**
- Modify: `src/IracingLiveCoach.Core/LiveCoachEngine.cs`
- Modify: `tests/IracingLiveCoach.Core.Tests/LiveCoachEngineTests.cs`

**Interfaces:**
- Consumes: same as Task 4.
- Produces: `CornerFeedback.CorrectionDeg` populated with this pass's own wasted-steering-motion degrees whenever there's enough data, regardless of whether the baseline itself has a value (the LIVE number is shown either way; comparison against baseline is a display-layer concern for the overlay, not this engine's job -- the engine reports raw live signals plus baseline-derived deltas where a delta makes sense, and braking delta in Task 4 is a delta specifically because "meters late/early" is the natural framing, whereas degrees of wasted motion is meaningful on its own).

- [ ] **Step 1: Write the failing test**

```csharp
// Append to tests/IracingLiveCoach.Core.Tests/LiveCoachEngineTests.cs

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

private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter LiveCoachEngineTests`
Expected: FAIL — the 2 new tests fail (CorrectionDeg is always null currently).

- [ ] **Step 3: Implement wasted-motion computation**

Add this private method (below `FindBrakeOnset`):

```csharp
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
```

In `BuildFeedback`, add the correction computation and include it in the returned record:

```csharp
        var correctionDeg = ComputeWastedSteeringDeg(samples);

        return new(corner.Number, corner.Name, brakingDeltaMeters, correctionDeg, WheelspinDetected: null);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter LiveCoachEngineTests`
Expected: PASS (8 passed)

- [ ] **Step 5: Commit**

```bash
git add src/IracingLiveCoach.Core/LiveCoachEngine.cs tests/IracingLiveCoach.Core.Tests/LiveCoachEngineTests.cs
git commit -m "feat: live steering-correction comparison in LiveCoachEngine"
```

---

### Task 6: Wheelspin live detection

**Files:**
- Modify: `src/IracingLiveCoach.Core/LiveCoachEngine.cs`
- Modify: `tests/IracingLiveCoach.Core.Tests/LiveCoachEngineTests.cs`

**Interfaces:**
- Consumes: same as Task 5, plus the `gearModel` constructor parameter (unused until now).
- Produces: `CornerFeedback.WheelspinDetected` populated (`true`/`false`) whenever at least one sample in the corner's EXIT HALF has gear/rpm/speed/throttle data and a matching entry in `gearModel`; `null` when there's no usable data (missing channels, or no `gearModel` entry for the gear used).

- [ ] **Step 1: Write the failing test**

```csharp
// Append to tests/IracingLiveCoach.Core.Tests/LiveCoachEngineTests.cs

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
    Assert.False(feedback!.WheelspinDetected);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter LiveCoachEngineTests`
Expected: FAIL — the 4 new tests fail (WheelspinDetected is always null currently).

- [ ] **Step 3: Implement wheelspin detection**

Add this private method (below `ComputeWastedSteeringDeg`):

```csharp
    private const double WheelspinThrottleMin = 0.85; // matches iracing-analytics's own WHEELSPIN_THROTTLE_MIN
    private const double WheelspinRpmSurplusPct = 8; // matches iracing-analytics's own WHEELSPIN_RPM_SURPLUS_PCT

    /// <summary>Checks only the corner's EXIT half (from the midpoint of [StartPct, EndPct) to
    /// EndPct) for an RPM surplus over the pooled per-gear model, at high throttle -- mirrors
    /// iracing-analytics/lib/local-coach-baselines.ts's own hasWheelspinInCorner exactly,
    /// including the exit-half restriction added after that codebase's own final review found
    /// entry-corner downshifts were misread as wheelspin under a whole-corner-window check.
    /// Returns null (not false) when there's no usable data to judge with, so "never spins" stays
    /// distinguishable from "couldn't tell" at the overlay layer.</summary>
    private bool? DetectWheelspin(CornerBaseline corner, List<TelemetrySample> samples)
    {
        if (_gearModel is null) return null;

        var exitStart = (corner.StartPct + corner.EndPct) / 2;
        var exitSamples = samples.Where(s => s.LapDistPct >= exitStart && s.LapDistPct < corner.EndPct).ToList();
        if (exitSamples.Count == 0) return null;

        var anyJudged = false;
        foreach (var sample in exitSamples)
        {
            if (sample.Throttle is not double throttle || throttle < WheelspinThrottleMin) continue;
            if (sample.Gear is not int gear || sample.Rpm is not double rpm || sample.SpeedMs is not double speed) continue;
            if (!_gearModel.TryGetValue(gear.ToString(), out var fit)) continue;

            anyJudged = true;
            var predicted = fit.A * speed + fit.B;
            if (predicted <= 0) continue;
            var surplusPct = (rpm - predicted) / predicted * 100;
            if (surplusPct >= WheelspinRpmSurplusPct) return true;
        }

        return anyJudged ? false : null;
    }
```

In `BuildFeedback`, add the wheelspin check and include it in the returned record:

```csharp
        var wheelspinDetected = DetectWheelspin(corner, samples);

        return new(corner.Number, corner.Name, brakingDeltaMeters, correctionDeg, wheelspinDetected);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter LiveCoachEngineTests`
Expected: PASS (12 passed)

- [ ] **Step 5: Run the full test suite and commit**

```bash
dotnet test
```
Expected: all tests across the whole solution pass.

```bash
git add src/IracingLiveCoach.Core/LiveCoachEngine.cs tests/IracingLiveCoach.Core.Tests/LiveCoachEngineTests.cs
git commit -m "feat: live wheelspin detection in LiveCoachEngine"
```

---

### Task 7: BaselineSync (HTTP fetch + local JSON cache)

**Files:**
- Create: `src/IracingLiveCoach.Core/BaselineSync.cs`
- Test: `tests/IracingLiveCoach.Core.Tests/BaselineSyncTests.cs`

**Interfaces:**
- Consumes: `BaselineResponse`, `JsonOptions` from Task 2.
- Produces: `class BaselineSync { public BaselineSync(HttpClient httpClient, string cacheDirectory, string endpointBaseUrl, string importKey); public Task<BaselineResponse> GetBaselineAsync(int carId, int trackId); }`

`GetBaselineAsync` tries the network first; on ANY failure (network error, non-200 status, malformed JSON), it falls back to whatever is cached on disk for that car+track; if neither succeeds, it returns an empty baseline (`new BaselineResponse("ok", null, null, new())`) rather than throwing -- per the Global Constraint, this app must never block driving on a failed sync.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/IracingLiveCoach.Core.Tests/BaselineSyncTests.cs
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IracingLiveCoach.Core;
using Xunit;

namespace IracingLiveCoach.Core.Tests;

/// <summary>Routes every HttpClient request to a caller-supplied function instead of the network
/// -- the standard way to unit-test HttpClient-based code without a real server.</summary>
internal class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
    public HttpRequestMessage? LastRequest { get; private set; }

    public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        return Task.FromResult(_responder(request));
    }
}

public class BaselineSyncTests : IDisposable
{
    private readonly string _tempCacheDir = Path.Combine(Path.GetTempPath(), "iracing-live-coach-tests-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_tempCacheDir)) Directory.Delete(_tempCacheDir, recursive: true);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private const string SampleJson = """{"status":"ok","trackLengthMeters":5891,"corners":[]}""";

    [Fact]
    public async Task Fetches_from_the_network_and_caches_the_result_on_success()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, SampleJson));
        var httpClient = new HttpClient(handler);
        var sync = new BaselineSync(httpClient, _tempCacheDir, "https://example.test", importKey: "secret");

        var result = await sync.GetBaselineAsync(carId: 152, trackId: 80);

        Assert.Equal(5891, result.TrackLengthMeters);
        Assert.Equal("https://example.test/api/telemetry/local-coach/baselines?car=152&track=80", handler.LastRequest!.RequestUri!.ToString());
        Assert.Equal("secret", handler.LastRequest.Headers.GetValues("x-import-key").First());

        var cachedFile = Path.Combine(_tempCacheDir, "152_80.json");
        Assert.True(File.Exists(cachedFile));
    }

    [Fact]
    public async Task Falls_back_to_the_cached_file_when_the_network_call_fails()
    {
        Directory.CreateDirectory(_tempCacheDir);
        await File.WriteAllTextAsync(Path.Combine(_tempCacheDir, "152_80.json"), SampleJson);

        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("network down"));
        var httpClient = new HttpClient(handler);
        var sync = new BaselineSync(httpClient, _tempCacheDir, "https://example.test", importKey: "secret");

        var result = await sync.GetBaselineAsync(carId: 152, trackId: 80);

        Assert.Equal(5891, result.TrackLengthMeters);
    }

    [Fact]
    public async Task Falls_back_to_the_cached_file_when_the_server_returns_an_error_status()
    {
        Directory.CreateDirectory(_tempCacheDir);
        await File.WriteAllTextAsync(Path.Combine(_tempCacheDir, "152_80.json"), SampleJson);

        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.InternalServerError, "{}"));
        var httpClient = new HttpClient(handler);
        var sync = new BaselineSync(httpClient, _tempCacheDir, "https://example.test", importKey: "secret");

        var result = await sync.GetBaselineAsync(carId: 152, trackId: 80);

        Assert.Equal(5891, result.TrackLengthMeters);
    }

    [Fact]
    public async Task Returns_an_empty_baseline_when_both_the_network_and_the_cache_are_unavailable()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("network down"));
        var httpClient = new HttpClient(handler);
        var sync = new BaselineSync(httpClient, _tempCacheDir, "https://example.test", importKey: "secret");

        var result = await sync.GetBaselineAsync(carId: 999, trackId: 999);

        Assert.Equal("ok", result.Status);
        Assert.Empty(result.Corners);
    }
}
```

Add `using System.Linq;` to the top of the file for `.First()` on the header values.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter BaselineSyncTests`
Expected: FAIL — `BaselineSync` does not exist yet.

- [ ] **Step 3: Implement BaselineSync**

```csharp
// src/IracingLiveCoach.Core/BaselineSync.cs
using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace IracingLiveCoach.Core;

/// <summary>Fetches this driver's own historical per-corner baselines from iracing-analytics's
/// GET /api/telemetry/local-coach/baselines, caching each car+track response to a local JSON
/// file. Per this app's own Global Constraint, a sync failure (network error, non-2xx status,
/// malformed JSON) NEVER blocks driving -- it falls back to whatever was last cached for that
/// exact car+track, and if nothing was ever cached either, returns an empty (no-corners)
/// baseline rather than throwing.</summary>
public class BaselineSync
{
    private readonly HttpClient _httpClient;
    private readonly string _cacheDirectory;
    private readonly string _endpointBaseUrl;
    private readonly string _importKey;

    public BaselineSync(HttpClient httpClient, string cacheDirectory, string endpointBaseUrl, string importKey)
    {
        _httpClient = httpClient;
        _cacheDirectory = cacheDirectory;
        _endpointBaseUrl = endpointBaseUrl.TrimEnd('/');
        _importKey = importKey;
    }

    public async Task<BaselineResponse> GetBaselineAsync(int carId, int trackId)
    {
        var cacheFile = Path.Combine(_cacheDirectory, $"{carId}_{trackId}.json");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{_endpointBaseUrl}/api/telemetry/local-coach/baselines?car={carId}&track={trackId}");
            request.Headers.Add("x-import-key", _importKey);

            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) return await ReadFromCacheOrEmpty(cacheFile);

            var json = await response.Content.ReadAsStringAsync();
            var parsed = JsonSerializer.Deserialize<BaselineResponse>(json, JsonOptions.Baseline);
            if (parsed is null) return await ReadFromCacheOrEmpty(cacheFile);

            Directory.CreateDirectory(_cacheDirectory);
            await File.WriteAllTextAsync(cacheFile, json);
            return parsed;
        }
        catch
        {
            return await ReadFromCacheOrEmpty(cacheFile);
        }
    }

    private static async Task<BaselineResponse> ReadFromCacheOrEmpty(string cacheFile)
    {
        try
        {
            if (File.Exists(cacheFile))
            {
                var cachedJson = await File.ReadAllTextAsync(cacheFile);
                var cached = JsonSerializer.Deserialize<BaselineResponse>(cachedJson, JsonOptions.Baseline);
                if (cached is not null) return cached;
            }
        }
        catch
        {
            // A corrupted cache file is no better than a missing one -- fall through to empty.
        }

        return new BaselineResponse("ok", null, null, new());
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter BaselineSyncTests`
Expected: PASS (4 passed)

- [ ] **Step 5: Run the full test suite and commit**

```bash
dotnet test
```
Expected: all tests across the whole solution pass (16 total: 2 Models + 12 LiveCoachEngine + 4 BaselineSync — wait, count from your own actual run and confirm it matches Tasks 2+3+4+5+6+7's cumulative test count, don't just trust this number blindly).

```bash
git add src/IracingLiveCoach.Core/BaselineSync.cs tests/IracingLiveCoach.Core.Tests/BaselineSyncTests.cs
git commit -m "feat: BaselineSync HTTP fetch with local JSON cache fallback"
```

---

### Task 8: TelemetryReader + OverlayWindow + app wiring (build-verified only)

**Files:**
- Create: `src/IracingLiveCoach.App/TelemetryReader.cs`
- Create: `src/IracingLiveCoach.App/OverlayViewModel.cs`
- Modify: `src/IracingLiveCoach.App/App.xaml.cs`
- Modify: `src/IracingLiveCoach.App/MainWindow.xaml` (rename semantics only -- this becomes the overlay window; the WPF template's default `MainWindow.xaml`/`.xaml.cs` files are reused, not deleted-and-recreated, to avoid fighting the template's own generated `App.xaml` `StartupUri` wiring)
- Modify: `src/IracingLiveCoach.App/MainWindow.xaml.cs`
- Modify: `src/IracingLiveCoach.App/IracingLiveCoach.App.csproj` (add the IRSDKSharper package reference)

**Interfaces:**
- Consumes: `LiveCoachEngine`, `TelemetrySample`, `CornerFeedback`, `BaselineSync`, `BaselineResponse` from Tasks 3-7.
- Produces: a running (once launched on a real Windows machine, ideally with iRacing open) desktop app. No other task consumes this — it's the composition root.

**This task is verified by `dotnet build` only.** The SDK integration needs a real iRacing session to exercise, and the overlay window needs an interactive display to see rendered — neither is available in this development environment. Per the spec's own Testing section, these are validated by the repo owner driving a real session once this is built; this task's job is to produce code that *compiles* and is *structurally correct* against the real, verified `IRSDKSharper` API (see below), not to prove it works end-to-end.

- [ ] **Step 1: Add the IRSDKSharper package reference**

```bash
dotnet add src/IracingLiveCoach.App/IracingLiveCoach.App.csproj package IRSDKSharper
```

- [ ] **Step 2: Implement TelemetryReader**

```csharp
// src/IracingLiveCoach.App/TelemetryReader.cs
using System;
using IRSDKSharper;
using IracingLiveCoach.Core;

namespace IracingLiveCoach.App;

/// <summary>Wraps IRSDKSharper's IRacingSdk, translating its raw telemetry variables into this
/// app's own TelemetrySample shape and forwarding each tick to a LiveCoachEngine. IRSDKSharper's
/// own LapDistPct is 0-1; the baseline endpoint's corner boundaries (and this app's
/// TelemetrySample.LapDistPct) are 0-100, so this is where that *100 conversion happens -- the
/// ONLY place in this app that needs to know about that unit mismatch.</summary>
public class TelemetryReader : IDisposable
{
    private readonly IRacingSdk _sdk = new();
    private readonly LiveCoachEngine _engine;

    public TelemetryReader(LiveCoachEngine engine)
    {
        _engine = engine;
        _sdk.OnTelemetryData += OnTelemetryData;
    }

    public void Start() => _sdk.Start();

    private void OnTelemetryData()
    {
        var lapDistPct = _sdk.Data.GetFloat("LapDistPct") * 100.0;
        var brake = (double?)_sdk.Data.GetFloat("Brake");
        var throttle = (double?)_sdk.Data.GetFloat("Throttle");
        var steeringRad = (double?)_sdk.Data.GetFloat("SteeringWheelAngle");
        var rpm = (double?)_sdk.Data.GetFloat("RPM");
        var gear = (int?)_sdk.Data.GetInt("Gear");
        var speedMs = (double?)_sdk.Data.GetFloat("Speed");

        _engine.Update(new TelemetrySample(lapDistPct, brake, throttle, steeringRad, rpm, gear, speedMs));
    }

    public void Dispose()
    {
        _sdk.OnTelemetryData -= OnTelemetryData;
        _sdk.Stop();
    }
}
```

This matches the real, verified source (`IRacingSdkData.cs`, `mherbold/IRSDKSharper`): `GetFloat(string name, int index = 0)` and `GetInt(string name, int index = 0)` both have a default `index = 0`, so a bare `GetFloat("LapDistPct")` call is a real, correct single-value (not per-car-array) read — confirmed directly against the package's source before this plan was finalized, not assumed.

- [ ] **Step 3: Implement the overlay view model**

```csharp
// src/IracingLiveCoach.App/OverlayViewModel.cs
using System.Collections.ObjectModel;
using System.ComponentModel;
using IracingLiveCoach.Core;

namespace IracingLiveCoach.App;

/// <summary>Backs the overlay window's display -- holds the most recent CornerFeedback per
/// corner number so the UI can show "last time through this corner" rather than only a single
/// most-recent-of-any-corner value. Implements INotifyPropertyChanged so WPF data binding
/// updates the window automatically when LiveCoachEngine.CornerCompleted fires.</summary>
public class OverlayViewModel : INotifyPropertyChanged
{
    public ObservableCollection<CornerFeedback> RecentCorners { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public void OnCornerCompleted(CornerFeedback feedback)
    {
        RecentCorners.Insert(0, feedback);
        while (RecentCorners.Count > 5) RecentCorners.RemoveAt(RecentCorners.Count - 1);
    }
}
```

- [ ] **Step 4: Wire everything in MainWindow (the overlay window)**

Replace the WPF template's default `MainWindow.xaml` content with a transparent, borderless, always-on-top window bound to `OverlayViewModel`:

```xml
<!-- src/IracingLiveCoach.App/MainWindow.xaml -->
<Window x:Class="IracingLiveCoach.App.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Live Coach Overlay"
        Width="320" Height="200"
        WindowStyle="None"
        AllowsTransparency="True"
        Background="Transparent"
        Topmost="True"
        ShowInTaskbar="False">
    <ItemsControl ItemsSource="{Binding RecentCorners}">
        <ItemsControl.ItemTemplate>
            <DataTemplate>
                <StackPanel Orientation="Horizontal" Margin="4" Background="#AA000000">
                    <TextBlock Text="{Binding CornerName}" Foreground="White" Margin="4" />
                    <TextBlock Text="{Binding BrakingDeltaMeters}" Foreground="Yellow" Margin="4" />
                    <TextBlock Text="{Binding CorrectionDeg}" Foreground="Orange" Margin="4" />
                    <TextBlock Text="{Binding WheelspinDetected}" Foreground="Red" Margin="4" />
                </StackPanel>
            </DataTemplate>
        </ItemsControl.ItemTemplate>
    </ItemsControl>
</Window>
```

```csharp
// src/IracingLiveCoach.App/MainWindow.xaml.cs
using System;
using System.IO;
using System.Net.Http;
using System.Windows;
using IracingLiveCoach.Core;

namespace IracingLiveCoach.App;

public partial class MainWindow : Window
{
    private readonly OverlayViewModel _viewModel = new();
    private TelemetryReader? _telemetryReader;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += async (_, _) => await InitializeAsync();
    }

    private async System.Threading.Tasks.Task InitializeAsync()
    {
        // 12/09/2026: car/track selection UI is a follow-up -- hardcoded here so this task's own
        // scope (build-verified plumbing) stays testable-by-compilation without inventing a whole
        // settings screen. Replace with a real picker once this is validated against a live session.
        const int placeholderCarId = 0;
        const int placeholderTrackId = 0;

        var cacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "iracing-live-coach", "baselines");
        var importKey = Environment.GetEnvironmentVariable("LOCAL_COACH_SECRET") ?? "";
        var sync = new BaselineSync(new HttpClient(), cacheDir, "https://iracing-analytics.vercel.app", importKey);
        var baseline = await sync.GetBaselineAsync(placeholderCarId, placeholderTrackId);

        var engine = new LiveCoachEngine(baseline.Corners, baseline.GearModel, baseline.TrackLengthMeters);
        engine.CornerCompleted += feedback => Dispatcher.Invoke(() => _viewModel.OnCornerCompleted(feedback));

        _telemetryReader = new TelemetryReader(engine);
        _telemetryReader.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _telemetryReader?.Dispose();
        base.OnClosed(e);
    }
}
```

- [ ] **Step 5: Verify the whole solution builds**

```bash
dotnet build
```
Expected: `Build succeeded.` for all 3 projects. If `IRacingSdk`'s actual API differs from Step 2's assumption, fix `TelemetryReader.cs` to match the real API (see the note in Step 2) and rebuild until this is clean — do not stop at a build error here, this step's whole job is proving the real package's API was used correctly.

- [ ] **Step 6: Run the full test suite one more time** (nothing in this task should have touched the Core tests, but confirm)

```bash
dotnet test
```
Expected: same pass count as the end of Task 7.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: TelemetryReader (IRSDKSharper), overlay window, and app wiring"
```
