# F1-Style Overlay Suite — Phase 3 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the last two Phase 3 widgets from the design spec — an honestly-labeled, pit-stall-refresh tire wear widget, and a radar-style spotter replacing the traditional left/right proximity bar — on top of Phase 1/2's shared host (both already merged to main by the time this plan executes).

**Architecture:** `TelemetryReader` gains two more events (`TireWearUpdated`, `RadarUpdated`) and one new public method (`SetTrackLength(double? meters)`, called once by `MainWindow` right after a baseline is fetched — see Task 1) computed by two new private methods, wired into the same `OnTelemetryData` dispatch alongside every Phase 1/2 event, without touching any of them. Two new widget windows (`TireWearWidget`, `RadarWidget`) consume these and are registered into `MainWindow.xaml.cs`/`ControlPanelWindow` exactly like every Phase 1/2 widget was.

**Tech Stack:** WPF (.NET 8, unchanged), IRSDKSharper (unchanged).

**Spec:** `docs/superpowers/specs/2026-09-13-overlay-suite-design.md` (the "Widget specs (Phase 3)"
section — read it before starting; it documents the pit-stall-only tire wear restriction and the
radar's own honest scoping, both already established this session).

## Global Constraints

- Every telemetry variable this plan uses was independently confirmed before this plan was
  written: `LFwearL/M/R`, `RFwearL/M/R`, `LRwearL/M/R`, `RRwearL/M/R` (tire tread, confirmed earlier
  this session and already documented in the spec's own Phase 3 section), `CarLeftRight` (enum
  0-6: `LROff`, `LRClear`, `LRCarLeft`, `LRCarRight`, `LRCarLeftRight`, `LR2CarsLeft`,
  `LR2CarsRight`, confirmed against `sajax.github.io/irsdkdocs`), `CarIdxLapDistPct` (already used
  by Phase 2's Weather widget). Do not introduce any other telemetry variable without the same
  level of confirmation.
- **Tire wear MUST be honestly labeled as pit-stall-refresh-only.** iRacing does not update these
  values while driving (a platform-wide restriction, not a limitation of this app — see the spec's
  own Context section for the confirmed source). The widget's displayed "atualizado às HH:MM:SS"
  (or "sem parada ainda") timestamp is not a caveat bolted on afterward — it is the actual
  deliverable that makes this widget honest rather than misleading. Do not remove it, shrink it, or
  make it less prominent than the wear bars themselves.
- **The radar is explicitly NOT a precise multi-car positioning display.** `CarLeftRight` is a
  coarse, non-indexed (not per-car) blind-spot signal — it tells you SOMETHING is near your left
  and/or right, never which car or how many beyond "1" vs "2". This plan's `RadarWidget` therefore
  shows TWO kinds of information side by side, styled differently on purpose so neither is mistaken
  for the other: a prominent near-field left/right blind-spot indicator (from `CarLeftRight`,
  matching what a traditional spotter bar already conveys, just redrawn as blips beside the player
  instead of edge-lit bars) and a dimmer, centered far-field distance scale (from `CarIdxLapDistPct`
  deltas converted to meters, for the same up-to-100m ahead/behind window) that never claims a
  lateral position for those farther cars, because the SDK does not expose one. Do not conflate the
  two into a single "always the same" lane -- see Task 2's own doc comments for the exact reasoning.
- Both new widgets follow the EXACT `(WidgetLayout layout, Action onChanged)` constructor and
  drag/resize/lock pattern already used four times over in Phase 1/2 (read the real, current
  `RelativeWidget.xaml.cs` as the literal reference). Both use `F1BorderIdleBrush`/
  `F1BorderActiveBrush` for `SetLocked`.
- `TireWearUpdated` fires every tick (cheap: 12 scalar reads, no per-car loop, no
  `ObservableCollection`). `RadarUpdated` performs a full `MaxNumCars` scan (like
  `UpdateStandings`/`UpdateWeather`) and MUST be throttled the same way Phase 2's `WeatherUpdated`
  was (~10Hz, every 6th tick) — a blind-spot/distance display does not need 60Hz any more than
  weather did, and this plan does not want to reintroduce the exact cost Phase 2 already fixed once.
- Every new/changed file is verified by `dotnet build` (0 errors, 0 warnings) and `dotnet test`
  (same pass count as before this plan — this plan adds no new automated tests, for the same
  established, disclosed SDK-dependency boundary as every other `TelemetryReader`/widget file).

---

### Task 1: `TelemetryReader` — `TireWearUpdated`, `RadarUpdated`, and `SetTrackLength`

**Files:**
- Modify: `src/IracingLiveCoach.App/TelemetryReader.cs`
- Modify: `src/IracingLiveCoach.App/MainWindow.xaml.cs` (call the new `SetTrackLength` once a
  baseline is available)

**Interfaces:**
- Consumes: nothing new from other tasks in this plan.
- Produces: `public record TireCornerWear(double TreadL, double TreadM, double TreadR)`, `public
  record TireWearStatus(TireCornerWear LF, TireCornerWear RF, TireCornerWear LR, TireCornerWear RR,
  DateTime? LastChangedAtUtc)`, `public record RadarBlip(double DistanceMeters, string
  DriverCode)`, `public record RadarStatus(bool BlindSpotLeft, bool BlindSpotRight,
  List<RadarBlip> Blips)`, `public event Action<TireWearStatus>? TireWearUpdated;`, `public event
  Action<RadarStatus>? RadarUpdated;`, `public void SetTrackLength(double? meters)` — Task 2's
  `TireWearWidget` consumes `TireWearUpdated`/`TireWearStatus`/`TireCornerWear`; Task 3's
  `RadarWidget` consumes `RadarUpdated`/`RadarStatus`/`RadarBlip`.

- [ ] **Step 1: Add the new record types**

At the top of the file, alongside the existing records:

```csharp
/// <summary>One tire corner's tread-remaining zones (0.0-1.0 fraction, L/M/R across the tread
/// face) -- see TireWearStatus's own doc comment for why these only change during a pit stop.</summary>
public record TireCornerWear(double TreadL, double TreadM, double TreadR);

/// <summary>A snapshot of all four tires' tread. LastChangedAtUtc is null until the FIRST real
/// change is observed relative to the session's starting values -- iRacing only updates these
/// while the car is in the pit stall (a platform-wide restriction, not specific to this app or to
/// Kapps -- see the spec's own Context section), so a driver who hasn't pitted yet sees "sem
/// parada ainda" rather than a timestamp implying a live reading that never happened.</summary>
public record TireWearStatus(TireCornerWear LF, TireCornerWear RF, TireCornerWear LR, TireCornerWear RR, DateTime? LastChangedAtUtc);

/// <summary>One far-field car's signed distance from the player along the lap (negative = behind,
/// positive = ahead), converted from CarIdxLapDistPct using the track's own length. Deliberately
/// carries NO lateral position -- the SDK does not expose one for cars outside the immediate
/// blind-spot window (see CarLeftRight's own doc comment on RadarStatus).</summary>
public record RadarBlip(double DistanceMeters, string DriverCode);

/// <summary>One (throttled, ~10Hz) radar snapshot. BlindSpotLeft/Right come from CarLeftRight, a
/// coarse, NON-per-car signal -- true means "something is right next to you on that side", not
/// "car X is on that side". Blips are the separate far-field distance list (see RadarBlip); the
/// two are rendered differently on purpose (see this plan's own Global Constraints) so a driver
/// never mistakes a far blip's centered position for "directly in my lane".</summary>
public record RadarStatus(bool BlindSpotLeft, bool BlindSpotRight, List<RadarBlip> Blips);
```

- [ ] **Step 2: Add fields**

Alongside the existing fuel/weather fields:

```csharp
    private TireWearStatus? _lastTireWear;
    private DateTime? _tireWearChangedAtUtc;

    // 13/09/2026: set once per session (MainWindow calls this right after a baseline is fetched,
    // reusing BaselineSync's own already-fetched TrackLengthMeters rather than re-parsing
    // WeekendInfo.TrackLength here) -- null until then, so UpdateRadar's meter conversion simply
    // skips far-field blips (not fabricate a wrong distance) until a real length is known.
    private double? _trackLengthMeters;

    private const int RadarTickInterval = 6;
    private int _radarTickCounter;
    private const double RadarMaxRangeMeters = 100.0;
```

- [ ] **Step 3: Add `SetTrackLength` and the two new events**

```csharp
    /// <summary>Called once by MainWindow after BaselineSync resolves the detected car+track's
    /// history (see OnSessionDetectedAsync) -- reuses that already-fetched track length instead of
    /// this class re-parsing WeekendInfo.TrackLength itself.</summary>
    public void SetTrackLength(double? meters) => _trackLengthMeters = meters;
```

Alongside the existing `WeatherUpdated` event declaration:

```csharp
    /// <summary>Fires every telemetry tick once the session is detected, with the player's own
    /// current tire tread state. See TireWearStatus's own doc comment for the pit-stall-only
    /// refresh this event is honest about.</summary>
    public event Action<TireWearStatus>? TireWearUpdated;

    /// <summary>Fires roughly every 10th of a second (throttled, same reasoning as
    /// WeatherUpdated) once the session is detected, with the current blind-spot state and every
    /// nearby car's far-field distance. See RadarStatus's own doc comment for why these two halves
    /// are kept visually distinct.</summary>
    public event Action<RadarStatus>? RadarUpdated;
```

- [ ] **Step 4: Wire both into `OnTelemetryData`**

Change:
```csharp
        if (_playerCarIdx >= 0)
        {
            UpdateRelative();
            UpdateFullRelative();
            UpdateStandings();
            UpdateFuel();

            _weatherTickCounter++;
            if (_weatherTickCounter >= WeatherTickInterval)
            {
                _weatherTickCounter = 0;
                UpdateWeather();
            }
        }
```
to:
```csharp
        if (_playerCarIdx >= 0)
        {
            UpdateRelative();
            UpdateFullRelative();
            UpdateStandings();
            UpdateFuel();
            UpdateTireWear();

            _weatherTickCounter++;
            if (_weatherTickCounter >= WeatherTickInterval)
            {
                _weatherTickCounter = 0;
                UpdateWeather();
            }

            _radarTickCounter++;
            if (_radarTickCounter >= RadarTickInterval)
            {
                _radarTickCounter = 0;
                UpdateRadar();
            }
        }
```

- [ ] **Step 5: Implement `UpdateTireWear` and `UpdateRadar`**

Add these two private methods, after the existing `UpdateWeather`:

```csharp
    private void UpdateTireWear()
    {
        try
        {
            var lf = new TireCornerWear(_sdk.Data.GetFloat("LFwearL"), _sdk.Data.GetFloat("LFwearM"), _sdk.Data.GetFloat("LFwearR"));
            var rf = new TireCornerWear(_sdk.Data.GetFloat("RFwearL"), _sdk.Data.GetFloat("RFwearM"), _sdk.Data.GetFloat("RFwearR"));
            var lr = new TireCornerWear(_sdk.Data.GetFloat("LRwearL"), _sdk.Data.GetFloat("LRwearM"), _sdk.Data.GetFloat("LRwearR"));
            var rr = new TireCornerWear(_sdk.Data.GetFloat("RRwearL"), _sdk.Data.GetFloat("RRwearM"), _sdk.Data.GetFloat("RRwearR"));

            if (_lastTireWear is TireWearStatus previous && TireWearChanged(previous, lf, rf, lr, rr))
                _tireWearChangedAtUtc = DateTime.UtcNow;

            _lastTireWear = new TireWearStatus(lf, rf, lr, rr, _tireWearChangedAtUtc);
            TireWearUpdated?.Invoke(_lastTireWear);
        }
        catch
        {
            // Skip this tick.
        }
    }

    // A small epsilon guards against float noise across ticks -- iRacing's own restriction means
    // these values should be bit-identical outside a pit stall, but a defensive tolerance costs
    // nothing and avoids a false "changed" from float representation jitter.
    private static bool TireWearChanged(TireWearStatus previous, TireCornerWear lf, TireCornerWear rf, TireCornerWear lr, TireCornerWear rr)
    {
        const double Epsilon = 0.0005;
        return CornerChanged(previous.LF, lf) || CornerChanged(previous.RF, rf) || CornerChanged(previous.LR, lr) || CornerChanged(previous.RR, rr);

        static bool CornerChanged(TireCornerWear a, TireCornerWear b) =>
            Math.Abs(a.TreadL - b.TreadL) > Epsilon || Math.Abs(a.TreadM - b.TreadM) > Epsilon || Math.Abs(a.TreadR - b.TreadR) > Epsilon;
    }

    private void UpdateRadar()
    {
        try
        {
            var leftRight = _sdk.Data.GetInt("CarLeftRight");
            // irsdk_CarLeftRight: 0=Off, 1=Clear, 2=CarLeft, 3=CarRight, 4=CarLeftRight, 5=2CarsLeft, 6=2CarsRight.
            var blindLeft = leftRight is 2 or 4 or 5;
            var blindRight = leftRight is 3 or 4 or 6;

            var blips = new List<RadarBlip>();
            if (_trackLengthMeters is double trackLength && trackLength > 0)
            {
                var myDistPct = _sdk.Data.GetFloat("CarIdxLapDistPct", _playerCarIdx);
                var maxCars = IRacingSdkConst.MaxNumCars;
                for (var idx = 0; idx < maxCars; idx++)
                {
                    if (idx == _playerCarIdx) continue;
                    var theirDistPct = _sdk.Data.GetFloat("CarIdxLapDistPct", idx);
                    if (theirDistPct < 0) continue; // car not currently on track / not in this session

                    // Shortest signed distance around the lap, wrapping at the start/finish line so
                    // a car just ahead across the line doesn't register as almost a full lap behind.
                    var delta = theirDistPct - myDistPct;
                    if (delta > 0.5) delta -= 1.0;
                    if (delta < -0.5) delta += 1.0;

                    var distanceMeters = delta * trackLength;
                    if (Math.Abs(distanceMeters) > RadarMaxRangeMeters) continue;

                    var code = _driverCodesByCarIdx.TryGetValue(idx, out var driverCode) ? driverCode : "?";
                    blips.Add(new RadarBlip(distanceMeters, code));
                }
            }

            RadarUpdated?.Invoke(new RadarStatus(blindLeft, blindRight, blips));
        }
        catch
        {
            // Skip this tick.
        }
    }
```

- [ ] **Step 6: Call `SetTrackLength` from `MainWindow.xaml.cs`**

In `OnSessionDetectedAsync`, right after `var baseline = await sync.GetBaselineAsync(carId, trackId);`
and before the `LiveCoachEngine` is constructed, add:
```csharp
        _telemetryReader?.SetTrackLength(baseline.TrackLengthMeters);
```
(`BaselineSync`'s `GetBaselineAsync` result already carries `TrackLengthMeters` — the same field
`LiveCoachEngine`'s own constructor call two lines below already reads — so this reuses data
already being fetched, rather than adding a new session-info parse.)

- [ ] **Step 7: Verify the build and tests**

Run: `dotnet build` — expect `Build succeeded.`, 0 errors, 0 warnings.
Run: `dotnet test` — expect the same test count passing as before this task (28, after Phase 2's
own WidgetLayoutStore/telemetry additions — no new automated tests in this task, same established
boundary).

- [ ] **Step 8: Commit**

```bash
git add src/IracingLiveCoach.App/TelemetryReader.cs src/IracingLiveCoach.App/MainWindow.xaml.cs
git commit -m "feat: TireWearUpdated and RadarUpdated telemetry events for the Tire and Radar widgets"
```

---

### Task 2: `TireWearWidget` — honest pit-stall-refresh tire wear display

**Files:**
- Create: `src/IracingLiveCoach.App/TireWearWidget.xaml`
- Create: `src/IracingLiveCoach.App/TireWearWidget.xaml.cs`
- Create: `src/IracingLiveCoach.App/TireWearWidgetViewModel.cs`
- Modify: `src/IracingLiveCoach.App/MainWindow.xaml.cs` (create + register this widget)

**Interfaces:**
- Consumes: `TelemetryReader.TireWearUpdated`/`TireWearStatus`/`TireCornerWear` (Task 1), same
  `WidgetLayoutStore`/F1 theme/`(WidgetLayout, Action onChanged)` pattern as every other widget.

- [ ] **Step 1: Write `TireWearWidgetViewModel.cs`**

Each tire corner renders as three colored segments (green/amber/red by remaining tread, per the
spec's own thresholds: green > 60%, amber 30-60%, red < 30%) — computed here so the XAML only
needs to bind a `Fill` brush per segment, no converters:

```csharp
// src/IracingLiveCoach.App/TireWearWidgetViewModel.cs
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace IracingLiveCoach.App;

public class TireCornerViewModel
{
    public Brush FillL { get; }
    public Brush FillM { get; }
    public Brush FillR { get; }

    private static readonly Brush GreenBrush = new SolidColorBrush(Color.FromRgb(0x2D, 0xE2, 0xB2));
    private static readonly Brush AmberBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xA5, 0x2C));
    private static readonly Brush RedBrush = new SolidColorBrush(Color.FromRgb(0xE1, 0x06, 0x00));

    public TireCornerViewModel(TireCornerWear wear)
    {
        FillL = ColorFor(wear.TreadL);
        FillM = ColorFor(wear.TreadM);
        FillR = ColorFor(wear.TreadR);
    }

    private static Brush ColorFor(double remainingFraction) => remainingFraction switch
    {
        > 0.6 => GreenBrush,
        > 0.3 => AmberBrush,
        _ => RedBrush,
    };
}

public class TireWearWidgetViewModel : INotifyPropertyChanged
{
    private TireCornerViewModel _lf = new(new TireCornerWear(1, 1, 1));
    private TireCornerViewModel _rf = new(new TireCornerWear(1, 1, 1));
    private TireCornerViewModel _lr = new(new TireCornerWear(1, 1, 1));
    private TireCornerViewModel _rr = new(new TireCornerWear(1, 1, 1));
    private string _statusText = "Sem parada ainda";

    public TireCornerViewModel LF { get => _lf; private set => Set(ref _lf, value); }
    public TireCornerViewModel RF { get => _rf; private set => Set(ref _rf, value); }
    public TireCornerViewModel LR { get => _lr; private set => Set(ref _lr, value); }
    public TireCornerViewModel RR { get => _rr; private set => Set(ref _rr, value); }
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }

    public void Apply(TireWearStatus status)
    {
        LF = new TireCornerViewModel(status.LF);
        RF = new TireCornerViewModel(status.RF);
        LR = new TireCornerViewModel(status.LR);
        RR = new TireCornerViewModel(status.RR);
        // This label IS the deliverable, not a footnote -- see this plan's own Global Constraints
        // and TireWearStatus's own doc comment for why "sem parada ainda" (rather than a timestamp
        // implying a live reading) is the honest default state.
        StatusText = status.LastChangedAtUtc is System.DateTime changedAtUtc
            ? "Atualizado às " + changedAtUtc.ToLocalTime().ToString("HH:mm:ss")
            : "Sem parada ainda";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
```

- [ ] **Step 2: Write `TireWearWidget.xaml`**

One reusable corner block (LF/RF/LR/RR), each a small horizontal three-segment bar with a corner
label above it, arranged in a 2x2 grid matching the car's own physical layout (LF top-left, RF
top-right, LR bottom-left, RR bottom-right) — the same spatial convention every real F1 broadcast
tire graphic and every competing sim overlay already uses:

```xml
<!-- src/IracingLiveCoach.App/TireWearWidget.xaml -->
<Window x:Class="IracingLiveCoach.App.TireWearWidget"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Tire Wear"
        WindowStyle="None"
        AllowsTransparency="True"
        Background="Transparent"
        Topmost="True"
        ShowInTaskbar="False"
        MinWidth="220" MinHeight="150"
        SizeToContent="Manual">
    <Grid Background="Transparent" MouseLeftButtonDown="OnBackgroundMouseLeftButtonDown">
        <Border x:Name="OuterBorder" Style="{StaticResource F1PanelBorder}">
            <StackPanel Margin="10">
                <TextBlock Text="TIRE WEAR" FontFamily="{StaticResource F1HeaderFont}" FontSize="11"
                           Foreground="{StaticResource F1AccentBrush}" Margin="0,0,0,8" />

                <UniformGrid Rows="2" Columns="2">
                    <StackPanel Margin="0,0,10,8">
                        <TextBlock Text="LF" FontFamily="{StaticResource F1MonoFont}" FontSize="10" Foreground="{StaticResource F1MutedTextBrush}" Margin="0,0,0,2" />
                        <StackPanel Orientation="Horizontal" Height="14">
                            <Rectangle Width="20" Fill="{Binding LF.FillL}" Margin="0,0,1,0" />
                            <Rectangle Width="20" Fill="{Binding LF.FillM}" Margin="0,0,1,0" />
                            <Rectangle Width="20" Fill="{Binding LF.FillR}" />
                        </StackPanel>
                    </StackPanel>
                    <StackPanel Margin="0,0,0,8">
                        <TextBlock Text="RF" FontFamily="{StaticResource F1MonoFont}" FontSize="10" Foreground="{StaticResource F1MutedTextBrush}" Margin="0,0,0,2" />
                        <StackPanel Orientation="Horizontal" Height="14">
                            <Rectangle Width="20" Fill="{Binding RF.FillL}" Margin="0,0,1,0" />
                            <Rectangle Width="20" Fill="{Binding RF.FillM}" Margin="0,0,1,0" />
                            <Rectangle Width="20" Fill="{Binding RF.FillR}" />
                        </StackPanel>
                    </StackPanel>
                    <StackPanel Margin="0,0,10,0">
                        <TextBlock Text="LR" FontFamily="{StaticResource F1MonoFont}" FontSize="10" Foreground="{StaticResource F1MutedTextBrush}" Margin="0,0,0,2" />
                        <StackPanel Orientation="Horizontal" Height="14">
                            <Rectangle Width="20" Fill="{Binding LR.FillL}" Margin="0,0,1,0" />
                            <Rectangle Width="20" Fill="{Binding LR.FillM}" Margin="0,0,1,0" />
                            <Rectangle Width="20" Fill="{Binding LR.FillR}" />
                        </StackPanel>
                    </StackPanel>
                    <StackPanel>
                        <TextBlock Text="RR" FontFamily="{StaticResource F1MonoFont}" FontSize="10" Foreground="{StaticResource F1MutedTextBrush}" Margin="0,0,0,2" />
                        <StackPanel Orientation="Horizontal" Height="14">
                            <Rectangle Width="20" Fill="{Binding RR.FillL}" Margin="0,0,1,0" />
                            <Rectangle Width="20" Fill="{Binding RR.FillM}" Margin="0,0,1,0" />
                            <Rectangle Width="20" Fill="{Binding RR.FillR}" />
                        </StackPanel>
                    </StackPanel>
                </UniformGrid>

                <!-- This status line is the widget's actual deliverable -- see this plan's own
                     Global Constraints. It must never be smaller/dimmer than the tire bars
                     themselves, and it must always be visible, not tucked away or collapsible. -->
                <TextBlock Text="{Binding StatusText}" FontFamily="{StaticResource F1MonoFont}" FontSize="11"
                           Foreground="{StaticResource F1TextBrush}" Margin="0,8,0,0" HorizontalAlignment="Center" />
            </StackPanel>
        </Border>
        <Thumb x:Name="ResizeGrip" Width="14" Height="14" Cursor="SizeNWSE"
               HorizontalAlignment="Right" VerticalAlignment="Bottom"
               Background="{StaticResource F1AccentBrush}" Opacity="0.5"
               DragDelta="OnResizeGripDragDelta" DragCompleted="OnResizeGripDragCompleted" />
    </Grid>
</Window>
```

- [ ] **Step 3: Write `TireWearWidget.xaml.cs`**

Identical drag/resize/lock/`Handle`/constructor structure to `FuelWidget.xaml.cs` (Phase 2, Task 2
— read that real, current file), renamed to `TireWearWidget`, wired to `TireWearWidgetViewModel`/
`UpdateStatus(TireWearStatus)` instead. Copy that file's structure exactly, changing only the class
name, the view-model type, and the `UpdateStatus` parameter type.

- [ ] **Step 4: Register it in `MainWindow.xaml.cs`**

Same pattern as every prior widget: add `private TireWearWidget? _tireWidget;`, create it in
`Initialize()` (`_layoutStore.Get("tires", 240, 220)`), `.Show()` it, subscribe
`_telemetryReader.TireWearUpdated += status => Dispatcher.Invoke(() => _tireWidget?.UpdateStatus(status));`,
add `("tires", "Tire Wear", _tireWidget)` to the `ControlPanelWindow` widget array, wire it into
`ApplyClickThrough()` and `OnClosed`.

- [ ] **Step 5: Verify the build and tests**

Run: `dotnet build` — expect `Build succeeded.`, 0 errors, 0 warnings.
Run: `dotnet test` — expect the same test count passing as before this task.

- [ ] **Step 6: Commit**

```bash
git add src/IracingLiveCoach.App/TireWearWidget.xaml src/IracingLiveCoach.App/TireWearWidget.xaml.cs src/IracingLiveCoach.App/TireWearWidgetViewModel.cs src/IracingLiveCoach.App/MainWindow.xaml.cs
git commit -m "feat: TireWearWidget -- honest pit-stall-refresh tire wear display"
```

---

### Task 3: `RadarWidget` — radar-style spotter (Le Mans Ultimate-inspired, honestly scoped)

**Files:**
- Create: `src/IracingLiveCoach.App/RadarWidget.xaml`
- Create: `src/IracingLiveCoach.App/RadarWidget.xaml.cs`
- Create: `src/IracingLiveCoach.App/RadarWidgetViewModel.cs`
- Modify: `src/IracingLiveCoach.App/MainWindow.xaml.cs` (create + register this widget)

**Interfaces:**
- Consumes: `TelemetryReader.RadarUpdated`/`RadarStatus`/`RadarBlip` (Task 1), same
  `WidgetLayoutStore`/F1 theme/`(WidgetLayout, Action onChanged)` pattern as every other widget.

- [ ] **Step 1: Write `RadarWidgetViewModel.cs`**

The far-field blips need a pixel `Top` position computed from `DistanceMeters` — same
"computed here, not in XAML" reasoning as Phase 2's `TrackUsageDotViewModel`:

```csharp
// src/IracingLiveCoach.App/RadarWidgetViewModel.cs
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IracingLiveCoach.App;

public class RadarBlipViewModel
{
    public double Top { get; }
    public string ToolTip { get; }

    // Matches RadarWidget.xaml's own canvas height and player-marker position -- kept in sync
    // manually since this view model has no direct reference to the XAML element (see Phase 2's
    // TrackUsageDotViewModel for the same, already-established pattern in this app).
    public RadarBlipViewModel(RadarBlip blip, double canvasHeight, double playerY, double maxRangeMeters)
    {
        var clamped = System.Math.Clamp(blip.DistanceMeters, -maxRangeMeters, maxRangeMeters);
        // Ahead (positive meters) draws ABOVE the player marker; behind draws below -- matching
        // the spec's own "player fixed at center-bottom, ahead is up" radar convention.
        Top = playerY - (clamped / maxRangeMeters) * playerY;
        ToolTip = blip.DriverCode;
    }
}

public class RadarWidgetViewModel : INotifyPropertyChanged
{
    private const double CanvasHeight = 200.0;
    private const double PlayerY = 170.0; // near the bottom, leaving headroom for "ahead" blips
    private const double MaxRangeMeters = 100.0;

    private bool _blindLeft;
    private bool _blindRight;

    public bool BlindLeft { get => _blindLeft; private set => Set(ref _blindLeft, value); }
    public bool BlindRight { get => _blindRight; private set => Set(ref _blindRight, value); }
    public ObservableCollection<RadarBlipViewModel> Blips { get; } = new();

    public void Apply(RadarStatus status)
    {
        BlindLeft = status.BlindSpotLeft;
        BlindRight = status.BlindSpotRight;

        Blips.Clear();
        foreach (var blip in status.Blips)
            Blips.Add(new RadarBlipViewModel(blip, CanvasHeight, PlayerY, MaxRangeMeters));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set(ref bool field, bool value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
```

- [ ] **Step 2: Write `RadarWidget.xaml`**

A fixed-size canvas: the player marker at a constant position near the bottom, two blind-spot
indicator circles immediately beside it (bright when active, per `BoolToVisibilityConverter` —
already added to `App.xaml` in Phase 2, reused here, not re-added), and far-field blips as small
dim ticks positioned by the view model's own computed `Top`, always horizontally centered (see this
plan's own Global Constraints for why they never get a lateral offset):

```xml
<!-- src/IracingLiveCoach.App/RadarWidget.xaml -->
<Window x:Class="IracingLiveCoach.App.RadarWidget"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Radar"
        WindowStyle="None"
        AllowsTransparency="True"
        Background="Transparent"
        Topmost="True"
        ShowInTaskbar="False"
        MinWidth="140" MinHeight="220"
        SizeToContent="Manual">
    <Grid Background="Transparent" MouseLeftButtonDown="OnBackgroundMouseLeftButtonDown">
        <Border x:Name="OuterBorder" Style="{StaticResource F1PanelBorder}">
            <StackPanel Margin="8">
                <TextBlock Text="RADAR" FontFamily="{StaticResource F1HeaderFont}" FontSize="10"
                           Foreground="{StaticResource F1AccentBrush}" Margin="0,0,0,4" HorizontalAlignment="Center" />
                <Canvas Width="120" Height="200">
                    <!-- Far-field distance ticks: centered, dim -- honestly convey "somewhere at
                         this distance", never a lateral lane (see this plan's own Global Constraints). -->
                    <ItemsControl ItemsSource="{Binding Blips}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Rectangle Width="30" Height="3" Fill="{StaticResource F1MutedTextBrush}"
                                           Canvas.Left="45" ToolTip="{Binding ToolTip}">
                                    <Rectangle.RenderTransform>
                                        <TranslateTransform Y="{Binding Top}" />
                                    </Rectangle.RenderTransform>
                                </Rectangle>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>

                    <!-- Player marker, fixed. -->
                    <Ellipse Width="12" Height="12" Fill="{StaticResource F1TextBrush}" Canvas.Left="54" Canvas.Top="164" />

                    <!-- Near-field blind-spot indicators: bright, immediate, beside the player --
                         this IS the traditional left/right bar's own information, just redrawn as
                         blips instead of edge-lit bars. -->
                    <Ellipse Width="16" Height="16" Fill="{StaticResource F1AccentBrush}" Canvas.Left="20" Canvas.Top="162"
                              Visibility="{Binding BlindLeft, Converter={StaticResource BoolToVisibilityConverter}}" />
                    <Ellipse Width="16" Height="16" Fill="{StaticResource F1AccentBrush}" Canvas.Left="84" Canvas.Top="162"
                              Visibility="{Binding BlindRight, Converter={StaticResource BoolToVisibilityConverter}}" />
                </Canvas>
            </StackPanel>
        </Border>
        <Thumb x:Name="ResizeGrip" Width="14" Height="14" Cursor="SizeNWSE"
               HorizontalAlignment="Right" VerticalAlignment="Bottom"
               Background="{StaticResource F1AccentBrush}" Opacity="0.5"
               DragDelta="OnResizeGripDragDelta" DragCompleted="OnResizeGripDragCompleted" />
    </Grid>
</Window>
```

`BoolToVisibilityConverter` already exists in `App.xaml` (added in Phase 2's `WeatherWidget` task)
— do not redefine it; reference it by the same `x:Key`.

- [ ] **Step 3: Write `RadarWidget.xaml.cs`**

Identical drag/resize/lock/`Handle`/constructor structure to `FuelWidget.xaml.cs`/
`TireWearWidget.xaml.cs` (read the real, current file), renamed to `RadarWidget`, wired to
`RadarWidgetViewModel`/`UpdateStatus(RadarStatus)` instead.

- [ ] **Step 4: Register it in `MainWindow.xaml.cs`**

Same pattern as every prior widget: add `private RadarWidget? _radarWidget;`, create it in
`Initialize()` (`_layoutStore.Get("radar", 140, 240)`), `.Show()` it, subscribe
`_telemetryReader.RadarUpdated += status => Dispatcher.Invoke(() => _radarWidget?.UpdateStatus(status));`,
add `("radar", "Radar", _radarWidget)` to the `ControlPanelWindow` widget array, wire it into
`ApplyClickThrough()` and `OnClosed`.

- [ ] **Step 5: Verify the build and tests**

Run: `dotnet build` — expect `Build succeeded.`, 0 errors, 0 warnings.
Run: `dotnet test` — expect the same test count passing as before this task.

- [ ] **Step 6: Commit**

```bash
git add src/IracingLiveCoach.App/RadarWidget.xaml src/IracingLiveCoach.App/RadarWidget.xaml.cs src/IracingLiveCoach.App/RadarWidgetViewModel.cs src/IracingLiveCoach.App/MainWindow.xaml.cs
git commit -m "feat: RadarWidget -- radar-style spotter, honestly scoped to what CarLeftRight/CarIdxLapDistPct actually provide"
```

---

### Task 4: Final integration verification

**Files:**
- None (build/test verification only)

- [ ] **Step 1: Full build and test run**

```bash
dotnet build
dotnet test
```
Expected: `Build succeeded.`, 0 errors, 0 warnings; all tests passing.

- [ ] **Step 2: Cross-check every widget-owning file's `Save()`/lock/shutdown wiring**

Per both prior phases' own final-review lesson (cross-cutting wiring gaps are invisible to a single
task's diff), read the final, current `MainWindow.xaml.cs` in full and confirm ALL NINE windows
(Coach, P2P, Control Panel, Relative, Standings, Fuel, Weather, Tire Wear, Radar) are each:
registered in the `ControlPanelWindow` widget array, included in `ApplyClickThrough()`'s
`SetLocked` calls, and included in `OnClosed()`'s `.Close()` calls.

- [ ] **Step 3: Commit** (only if Step 2 required a fix; otherwise nothing new to commit here)
