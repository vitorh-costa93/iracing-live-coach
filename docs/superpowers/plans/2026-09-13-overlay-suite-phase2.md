# F1-Style Overlay Suite — Phase 2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add two new F1-styled overlay widgets on top of Phase 1's shared host — a Fuel calculator
and a combined Weather/Track-Wetness/Precipitation/Track-Usage widget — following the exact widget
pattern (`WidgetLayoutStore`, `F1Theme.xaml`, drag/resize/lock, Control Panel registration) Phase 1
already established and proved twice (`RelativeWidget`, `StandingsWidget`).

**Architecture:** `TelemetryReader` gains two new events (`FuelUpdated`, `WeatherUpdated`) computed
by two new private methods, wired into the same `OnTelemetryData` dispatch alongside the three
Phase 1 events, without touching any of them. Two new widget windows (`FuelWidget`,
`WeatherWidget`) consume these events and are registered into `MainWindow.xaml.cs`/
`ControlPanelWindow` exactly like `RelativeWidget`/`StandingsWidget` were.

**Tech Stack:** WPF (.NET 8, unchanged), IRSDKSharper (unchanged).

**Spec:** `docs/superpowers/specs/2026-09-13-overlay-suite-design.md` (the "Widget specs (Phase 2)"
section, added 13/09/2026 — read it before starting; it documents every telemetry variable this
plan uses and why "Track Usage" is scoped as a linear bar, not a real track shape).

## Global Constraints

- Every telemetry variable this plan uses was independently confirmed against
  `sajax.github.io/irsdkdocs` before this plan was written: `FuelLevel`, `FuelUsePerHour`,
  `LapCompleted`, `LapLastLapTime`, `AirTemp`, `TrackTemp`, `Precipitation`, `TrackWetness`,
  `WeatherDeclaredWet`, `CarIdxLapDistPct`. Do not introduce any other telemetry variable without
  the same level of confirmation.
- No Phase 1 code path changes behavior. `RelativeCarStatus`/`RelativeUpdated`, `RelativeRow`/
  `FullRelativeUpdated`, `StandingsRow`/`StandingsUpdated`, and every existing widget's own visible
  behavior stay exactly as they are — this plan is purely additive.
- Both new widgets follow the EXACT `(WidgetLayout layout, Action onChanged)` constructor and
  drag/resize/lock pattern already used by `RelativeWidget.xaml.cs`/`StandingsWidget.xaml.cs` (read
  those two real, current files as the literal reference before writing the new ones — do not
  invent a different shape). Both use `F1BorderIdleBrush`/`F1BorderActiveBrush` for their
  `SetLocked` (the Phase 1 final-review fix that gave those two widgets a real lock/unlock visual
  affordance — the two new widgets must have it from the start, not as a follow-up fix).
- `WeatherUpdated` is throttled to roughly 10 Hz (once every 6 telemetry ticks), NOT fired every
  tick like Phase 1's events — its own per-car `CarIdxLapDistPct` scan is exactly as expensive as
  `UpdateStandings`'s scan, and the Phase 1 final review flagged firing three full-`MaxNumCars`
  scans at 60 Hz (each ending in a synchronous `Dispatcher.Invoke` and a full `ObservableCollection`
  rebuild) as unnecessary cost worth avoiding in new code rather than repeating a third time. Weather
  and track position both change over seconds, not milliseconds, so 10 Hz is visually indistinguishable
  from 60 Hz here. `FuelUpdated` is NOT throttled (it reads a handful of scalars, no per-car scan,
  and drives no `ObservableCollection` — negligible cost at 60 Hz).
- Every new/changed file is verified by `dotnet build` (0 errors, 0 warnings) and `dotnet test`
  (same pass count as before this plan, 26 tests — this plan adds no new automated tests, since
  both new methods depend on the real IRSDKSharper SDK type, the same established, disclosed
  boundary as every other `TelemetryReader` method and every other widget window in this app).

---

### Task 1: `TelemetryReader` — `FuelUpdated` and `WeatherUpdated` events

**Files:**
- Modify: `src/IracingLiveCoach.App/TelemetryReader.cs`

**Interfaces:**
- Consumes: nothing new from other tasks.
- Produces: `public record FuelStatus(double FuelLevelLiters, double FuelUsePerHourLiters, double?
  AverageFuelPerLapLiters, double? LapsRemaining, double? TimeRemainingSeconds)`, `public record
  TrackPositionDot(string DriverCode, double LapDistPct, bool IsPlayer)`, `public record
  WeatherStatus(double AirTempC, double TrackTempC, double PrecipitationPct, int TrackWetness, bool
  WeatherDeclaredWet, List<TrackPositionDot> CarPositions)`, `public event Action<FuelStatus>?
  FuelUpdated;`, `public event Action<WeatherStatus>? WeatherUpdated;` — Task 2's `FuelWidget`
  consumes `FuelUpdated`/`FuelStatus`; Task 3's `WeatherWidget` consumes `WeatherUpdated`/
  `WeatherStatus`/`TrackPositionDot`.

- [ ] **Step 1: Add the new record types**

At the top of the file, alongside the existing `RelativeRow`/`StandingsRow` records:

```csharp
/// <summary>One tick's fuel state. AverageFuelPerLapLiters/LapsRemaining/TimeRemainingSeconds are
/// null until at least one full lap has completed since the app started watching (see UpdateFuel's
/// own doc comment) -- never show a number computed from zero samples.</summary>
public record FuelStatus(double FuelLevelLiters, double FuelUsePerHourLiters, double? AverageFuelPerLapLiters, double? LapsRemaining, double? TimeRemainingSeconds);

/// <summary>One car's current position around the lap (0.0 at start/finish, approaching 1.0 as it
/// completes the lap) -- feeds the Weather widget's linear "track usage" bar. Deliberately NOT a
/// real track shape (see the spec's own Phase 2 section for why).</summary>
public record TrackPositionDot(string DriverCode, double LapDistPct, bool IsPlayer);

/// <summary>One (throttled, ~10Hz) weather/track-usage snapshot.</summary>
public record WeatherStatus(double AirTempC, double TrackTempC, double PrecipitationPct, int TrackWetness, bool WeatherDeclaredWet, List<TrackPositionDot> CarPositions);
```

- [ ] **Step 2: Add fuel-tracking fields and the two new events**

Alongside the existing `_driverCodesByCarIdx` field:

```csharp
    // 13/09/2026: rolling-average fuel calculator state -- see UpdateFuel's own doc comment for
    // why a per-lap rolling average is used instead of the SDK's own instantaneous FuelUsePerHour.
    private const int FuelWindowSize = 5;
    private double? _lastFuelLevel;
    private int _lastLapCompleted = -1;
    private readonly Queue<double> _fuelPerLapWindow = new();
    private readonly Queue<double> _lapTimeWindow = new();

    // 13/09/2026: WeatherUpdated is throttled to ~10Hz (every 6th telemetry tick, 60Hz/6=10) --
    // see this plan's own Global Constraints for why a full-MaxNumCars scan doesn't need 60Hz here.
    private const int WeatherTickInterval = 6;
    private int _weatherTickCounter;
```

Alongside the existing `StandingsUpdated` event declaration:

```csharp
    /// <summary>Fires every telemetry tick once the session is detected, with the player's own
    /// current fuel state and a rolling-average-based remaining-laps/time estimate.</summary>
    public event Action<FuelStatus>? FuelUpdated;

    /// <summary>Fires roughly every 10th of a second (throttled -- see WeatherTickInterval) once
    /// the session is detected, with the current weather/track-wetness readout and every car's
    /// current lap position.</summary>
    public event Action<WeatherStatus>? WeatherUpdated;
```

- [ ] **Step 3: Wire both into `OnTelemetryData`**

Change:
```csharp
        if (_playerCarIdx >= 0)
        {
            UpdateRelative();
            UpdateFullRelative();
            UpdateStandings();
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

            _weatherTickCounter++;
            if (_weatherTickCounter >= WeatherTickInterval)
            {
                _weatherTickCounter = 0;
                UpdateWeather();
            }
        }
```

- [ ] **Step 4: Implement `UpdateFuel` and `UpdateWeather`**

Add these two private methods, after the existing `UpdateStandings`:

```csharp
    // 13/09/2026: a rolling average over the last FuelWindowSize completed laps, not the SDK's own
    // instantaneous FuelUsePerHour -- an instantaneous rate swings with throttle/braking on any
    // single sample, while the rolling average is what every established fuel calculator actually
    // uses for a stable "laps remaining" estimate. LapCompleted (not Lap) is the correct edge to
    // watch: it increments exactly once per finished lap, where Lap reports the currently-STARTED
    // lap and would double-count the boundary tick (see LapCompleted's own confirmed SDK doc).
    private void UpdateFuel()
    {
        try
        {
            var fuelLevel = _sdk.Data.GetFloat("FuelLevel");
            var fuelUsePerHour = _sdk.Data.GetFloat("FuelUsePerHour");
            var lapCompleted = _sdk.Data.GetInt("LapCompleted");

            if (_lastLapCompleted < 0)
            {
                _lastLapCompleted = lapCompleted;
                _lastFuelLevel = fuelLevel;
            }
            else if (lapCompleted > _lastLapCompleted && _lastFuelLevel is double previousFuel)
            {
                var used = previousFuel - fuelLevel;
                // Only a positive, plausible consumption sample is trusted -- a pit stop refuel
                // between ticks would otherwise register as a large negative "used" value and
                // corrupt the rolling average with a nonsense sample.
                if (used > 0)
                {
                    _fuelPerLapWindow.Enqueue(used);
                    if (_fuelPerLapWindow.Count > FuelWindowSize) _fuelPerLapWindow.Dequeue();

                    var lapTime = _sdk.Data.GetFloat("LapLastLapTime");
                    if (lapTime > 0)
                    {
                        _lapTimeWindow.Enqueue(lapTime);
                        if (_lapTimeWindow.Count > FuelWindowSize) _lapTimeWindow.Dequeue();
                    }
                }
                _lastLapCompleted = lapCompleted;
                _lastFuelLevel = fuelLevel;
            }

            double? avgFuelPerLap = _fuelPerLapWindow.Count > 0 ? _fuelPerLapWindow.Average() : null;
            double? avgLapTime = _lapTimeWindow.Count > 0 ? _lapTimeWindow.Average() : null;
            double? lapsRemaining = avgFuelPerLap is double perLap && perLap > 0 ? fuelLevel / perLap : null;
            double? timeRemaining = lapsRemaining is double laps && avgLapTime is double lapTime2 ? laps * lapTime2 : null;

            FuelUpdated?.Invoke(new FuelStatus(fuelLevel, fuelUsePerHour, avgFuelPerLap, lapsRemaining, timeRemaining));
        }
        catch
        {
            // Skip this tick.
        }
    }

    private void UpdateWeather()
    {
        try
        {
            var airTemp = _sdk.Data.GetFloat("AirTemp");
            var trackTemp = _sdk.Data.GetFloat("TrackTemp");
            var precipitation = _sdk.Data.GetFloat("Precipitation") * 100.0;
            var trackWetness = _sdk.Data.GetInt("TrackWetness");
            var declaredWet = _sdk.Data.GetBool("WeatherDeclaredWet");

            var maxCars = IRacingSdkConst.MaxNumCars;
            var positions = new List<TrackPositionDot>();
            for (var idx = 0; idx < maxCars; idx++)
            {
                var lapDistPct = _sdk.Data.GetFloat("CarIdxLapDistPct", idx);
                if (lapDistPct < 0) continue; // car not currently on track / not in this session
                var code = _driverCodesByCarIdx.TryGetValue(idx, out var driverCode) ? driverCode : "?";
                positions.Add(new TrackPositionDot(code, lapDistPct, idx == _playerCarIdx));
            }

            WeatherUpdated?.Invoke(new WeatherStatus(airTemp, trackTemp, precipitation, trackWetness, declaredWet, positions));
        }
        catch
        {
            // Skip this tick.
        }
    }
```

- [ ] **Step 5: Verify the build and tests**

Run: `dotnet build` — expect `Build succeeded.`, 0 errors, 0 warnings.
Run: `dotnet test` — expect the same 26 tests passing, no regressions (this task adds none, per
the file's established SDK-dependent boundary).

- [ ] **Step 6: Commit**

```bash
git add src/IracingLiveCoach.App/TelemetryReader.cs
git commit -m "feat: FuelUpdated and WeatherUpdated telemetry events for the Fuel and Weather widgets"
```

---

### Task 2: `FuelWidget` — F1-style fuel calculator

**Files:**
- Create: `src/IracingLiveCoach.App/FuelWidget.xaml`
- Create: `src/IracingLiveCoach.App/FuelWidget.xaml.cs`
- Create: `src/IracingLiveCoach.App/FuelWidgetViewModel.cs`
- Modify: `src/IracingLiveCoach.App/MainWindow.xaml.cs` (create + register this widget)

**Interfaces:**
- Consumes: `TelemetryReader.FuelUpdated`/`FuelStatus` (Task 1), `WidgetLayoutStore`/`WidgetLayout`
  (Phase 1), F1 theme resources (Phase 1: `F1PanelBorder`, `F1HeaderFont`, `F1MonoFont`,
  `F1AccentBrush`, `F1TextBrush`, `F1MutedTextBrush`, `F1BorderIdleBrush`, `F1BorderActiveBrush`),
  the `(WidgetLayout layout, Action onChanged)` constructor pattern.

- [ ] **Step 1: Write `FuelWidgetViewModel.cs`**

```csharp
// src/IracingLiveCoach.App/FuelWidgetViewModel.cs
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace IracingLiveCoach.App;

public class FuelWidgetViewModel : INotifyPropertyChanged
{
    private string _fuelLevelText = "--";
    private string _fuelUsePerHourText = "--";
    private string _averagePerLapText = "--";
    private string _lapsRemainingText = "--";
    private string _timeRemainingText = "--";

    public string FuelLevelText { get => _fuelLevelText; private set => Set(ref _fuelLevelText, value); }
    public string FuelUsePerHourText { get => _fuelUsePerHourText; private set => Set(ref _fuelUsePerHourText, value); }
    public string AveragePerLapText { get => _averagePerLapText; private set => Set(ref _averagePerLapText, value); }
    public string LapsRemainingText { get => _lapsRemainingText; private set => Set(ref _lapsRemainingText, value); }
    public string TimeRemainingText { get => _timeRemainingText; private set => Set(ref _timeRemainingText, value); }

    public void Apply(FuelStatus status)
    {
        FuelLevelText = status.FuelLevelLiters.ToString("0.0", CultureInfo.InvariantCulture) + " L";
        FuelUsePerHourText = status.FuelUsePerHourLiters.ToString("0.0", CultureInfo.InvariantCulture) + " L/h";
        AveragePerLapText = status.AverageFuelPerLapLiters is double perLap
            ? perLap.ToString("0.00", CultureInfo.InvariantCulture) + " L/lap"
            : "--";
        LapsRemainingText = status.LapsRemaining is double laps
            ? laps.ToString("0.0", CultureInfo.InvariantCulture)
            : "--";
        TimeRemainingText = status.TimeRemainingSeconds is double seconds
            ? System.TimeSpan.FromSeconds(seconds).ToString(@"mm\:ss")
            : "--";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set(ref string field, string value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
```

- [ ] **Step 2: Write `FuelWidget.xaml`**

```xml
<!-- src/IracingLiveCoach.App/FuelWidget.xaml -->
<Window x:Class="IracingLiveCoach.App.FuelWidget"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Fuel"
        WindowStyle="None"
        AllowsTransparency="True"
        Background="Transparent"
        Topmost="True"
        ShowInTaskbar="False"
        MinWidth="180" MinHeight="150"
        SizeToContent="Manual">
    <Grid Background="Transparent" MouseLeftButtonDown="OnBackgroundMouseLeftButtonDown">
        <Border x:Name="OuterBorder" Style="{StaticResource F1PanelBorder}">
            <StackPanel Margin="10">
                <TextBlock Text="FUEL" FontFamily="{StaticResource F1HeaderFont}" FontSize="11"
                           Foreground="{StaticResource F1AccentBrush}" Margin="0,0,0,8" />

                <Grid Margin="0,2">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="Auto" />
                    </Grid.ColumnDefinitions>
                    <TextBlock Grid.Column="0" Text="Nível" FontFamily="{StaticResource F1MonoFont}" FontSize="12" Foreground="{StaticResource F1MutedTextBrush}" />
                    <TextBlock Grid.Column="1" Text="{Binding FuelLevelText}" FontFamily="{StaticResource F1MonoFont}" FontSize="14" FontWeight="Bold" Foreground="{StaticResource F1TextBrush}" />
                </Grid>
                <Grid Margin="0,2">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="Auto" />
                    </Grid.ColumnDefinitions>
                    <TextBlock Grid.Column="0" Text="Consumo/h" FontFamily="{StaticResource F1MonoFont}" FontSize="11" Foreground="{StaticResource F1MutedTextBrush}" />
                    <TextBlock Grid.Column="1" Text="{Binding FuelUsePerHourText}" FontFamily="{StaticResource F1MonoFont}" FontSize="12" Foreground="{StaticResource F1MutedTextBrush}" />
                </Grid>
                <Grid Margin="0,2">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="Auto" />
                    </Grid.ColumnDefinitions>
                    <TextBlock Grid.Column="0" Text="Média/volta" FontFamily="{StaticResource F1MonoFont}" FontSize="11" Foreground="{StaticResource F1MutedTextBrush}" />
                    <TextBlock Grid.Column="1" Text="{Binding AveragePerLapText}" FontFamily="{StaticResource F1MonoFont}" FontSize="12" Foreground="{StaticResource F1MutedTextBrush}" />
                </Grid>

                <Rectangle Height="1" Fill="{StaticResource F1MutedTextBrush}" Opacity="0.25" Margin="0,8" />

                <Grid Margin="0,2">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="Auto" />
                    </Grid.ColumnDefinitions>
                    <TextBlock Grid.Column="0" Text="Voltas restantes" FontFamily="{StaticResource F1MonoFont}" FontSize="12" Foreground="{StaticResource F1TextBrush}" />
                    <TextBlock Grid.Column="1" Text="{Binding LapsRemainingText}" FontFamily="{StaticResource F1MonoFont}" FontSize="16" FontWeight="Bold" Foreground="{StaticResource F1AccentBrush}" />
                </Grid>
                <Grid Margin="0,2">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="Auto" />
                    </Grid.ColumnDefinitions>
                    <TextBlock Grid.Column="0" Text="Tempo restante" FontFamily="{StaticResource F1MonoFont}" FontSize="12" Foreground="{StaticResource F1TextBrush}" />
                    <TextBlock Grid.Column="1" Text="{Binding TimeRemainingText}" FontFamily="{StaticResource F1MonoFont}" FontSize="16" FontWeight="Bold" Foreground="{StaticResource F1AccentBrush}" />
                </Grid>
            </StackPanel>
        </Border>
        <Thumb x:Name="ResizeGrip" Width="14" Height="14" Cursor="SizeNWSE"
               HorizontalAlignment="Right" VerticalAlignment="Bottom"
               Background="{StaticResource F1AccentBrush}" Opacity="0.5"
               DragDelta="OnResizeGripDragDelta" DragCompleted="OnResizeGripDragCompleted" />
    </Grid>
</Window>
```

- [ ] **Step 3: Write `FuelWidget.xaml.cs`**

Mirror `RelativeWidget.xaml.cs`'s exact drag/resize/lock/`Handle`/constructor shape (read that real,
current file for the literal reference), adapted for `FuelWidgetViewModel`:

```csharp
// src/IracingLiveCoach.App/FuelWidget.xaml.cs
using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using IracingLiveCoach.Core;
using Brush = System.Windows.Media.Brush;

namespace IracingLiveCoach.App;

/// <summary>F1-style fuel calculator widget. Same drag/resize/lock mechanics as every other
/// overlay widget window (see RelativeOverlayWindow's own comment for why they're duplicated
/// rather than shared).</summary>
public partial class FuelWidget : Window
{
    private readonly WidgetLayout _layout;
    private readonly Action _onChanged;
    private readonly FuelWidgetViewModel _viewModel = new();

    public IntPtr Handle => new WindowInteropHelper(this).Handle;

    public FuelWidget(WidgetLayout layout, Action onChanged)
    {
        InitializeComponent();
        _layout = layout;
        _onChanged = onChanged;
        DataContext = _viewModel;

        Width = _layout.Width;
        Height = _layout.Height;
        if (_layout.Left is double left && _layout.Top is double top)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }
    }

    public void UpdateStatus(FuelStatus status) => _viewModel.Apply(status);

    public void SetLocked(bool locked)
    {
        ClickThrough.Set(Handle, locked);
        OuterBorder.BorderBrush = locked
            ? (Brush)FindResource("F1BorderIdleBrush")
            : (Brush)FindResource("F1BorderActiveBrush");
    }

    private void OnBackgroundMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        DragMove();
        PersistLayout();
    }

    private void OnResizeGripDragDelta(object sender, DragDeltaEventArgs e)
    {
        Width = Math.Max(MinWidth, Width + e.HorizontalChange);
        Height = Math.Max(MinHeight, Height + e.VerticalChange);
    }

    private void OnResizeGripDragCompleted(object sender, DragCompletedEventArgs e) => PersistLayout();

    private void PersistLayout()
    {
        _layout.Left = Left;
        _layout.Top = Top;
        _layout.Width = Width;
        _layout.Height = Height;
        _onChanged();
    }
}
```

- [ ] **Step 4: Register it in `MainWindow.xaml.cs`**

Add the field `private FuelWidget? _fuelWidget;` alongside the other widget fields. In
`Initialize()`, after creating `_standingsWidget` and before creating `_controlPanel`, add:
```csharp
        _fuelWidget = new FuelWidget(_layoutStore.Get("fuel", 200, 220), () => _layoutStore.Save());
        _fuelWidget.Show();
```
Add `("fuel", "Fuel", _fuelWidget)` to the `ControlPanelWindow` constructor's widget array. After
the existing `_telemetryReader.StandingsUpdated += ...` line, add:
```csharp
        _telemetryReader.FuelUpdated += status => Dispatcher.Invoke(() => _fuelWidget?.UpdateStatus(status));
```
Add `_fuelWidget?.SetLocked(_locked);` to `ApplyClickThrough()`. Add `_fuelWidget?.Close();` to
`OnClosed`.

- [ ] **Step 5: Verify the build and tests**

Run: `dotnet build` — expect `Build succeeded.`, 0 errors, 0 warnings.
Run: `dotnet test` — expect the same 26 tests passing.

- [ ] **Step 6: Commit**

```bash
git add src/IracingLiveCoach.App/FuelWidget.xaml src/IracingLiveCoach.App/FuelWidget.xaml.cs src/IracingLiveCoach.App/FuelWidgetViewModel.cs src/IracingLiveCoach.App/MainWindow.xaml.cs
git commit -m "feat: FuelWidget -- F1-style fuel calculator"
```

---

### Task 3: `WeatherWidget` — combined weather / track-wetness / precipitation / track-usage widget

**Files:**
- Create: `src/IracingLiveCoach.App/WeatherWidget.xaml`
- Create: `src/IracingLiveCoach.App/WeatherWidget.xaml.cs`
- Create: `src/IracingLiveCoach.App/WeatherWidgetViewModel.cs`
- Modify: `src/IracingLiveCoach.App/MainWindow.xaml.cs` (create + register this widget)

**Interfaces:**
- Consumes: `TelemetryReader.WeatherUpdated`/`WeatherStatus`/`TrackPositionDot` (Task 1), same
  `WidgetLayoutStore`/F1 theme/`(WidgetLayout, Action onChanged)` pattern as Task 2.

- [ ] **Step 1: Write `WeatherWidgetViewModel.cs`**

The "track usage" bar needs each dot's horizontal position as a pixel offset within a fixed-width
bar — computed here (not in XAML) since `LapDistPct` (0.0-1.0) needs multiplying by the bar's own
width, a value the view model is told about, not something a plain data binding can multiply by
inline in XAML without a converter:

```csharp
// src/IracingLiveCoach.App/WeatherWidgetViewModel.cs
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace IracingLiveCoach.App;

public class TrackUsageDotViewModel
{
    public double Left { get; }
    public string ToolTip { get; }
    public Brush Fill { get; }

    private static readonly Brush PlayerBrush = new SolidColorBrush(Color.FromRgb(0xE1, 0x06, 0x00));
    private static readonly Brush OtherBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0xA3, 0xAF));

    public TrackUsageDotViewModel(TrackPositionDot dot, double barWidth, double dotSize)
    {
        Left = System.Math.Clamp(dot.LapDistPct, 0.0, 1.0) * (barWidth - dotSize);
        ToolTip = dot.DriverCode;
        Fill = dot.IsPlayer ? PlayerBrush : OtherBrush;
    }
}

public class WeatherWidgetViewModel : INotifyPropertyChanged
{
    // Matches the bar's own Width in WeatherWidget.xaml -- kept in sync manually since this view
    // model has no direct reference to the XAML element (the same "computed here, not in XAML"
    // constraint that motivates this whole class -- see this task's own Step 1 comment above).
    private const double BarWidth = 240.0;
    private const double DotSize = 8.0;

    private static readonly string[] WetnessLabels =
    {
        "Desconhecido", "Seca", "Maioria seca", "Levemente úmida (muito)",
        "Levemente úmida", "Moderadamente úmida", "Muito úmida", "Extremamente úmida",
    };

    private string _airTempText = "--";
    private string _trackTempText = "--";
    private string _precipitationText = "--";
    private string _wetnessText = "--";
    private bool _declaredWetVisible;

    public string AirTempText { get => _airTempText; private set => Set(ref _airTempText, value); }
    public string TrackTempText { get => _trackTempText; private set => Set(ref _trackTempText, value); }
    public string PrecipitationText { get => _precipitationText; private set => Set(ref _precipitationText, value); }
    public string WetnessText { get => _wetnessText; private set => Set(ref _wetnessText, value); }
    public bool DeclaredWetVisible { get => _declaredWetVisible; private set => Set(ref _declaredWetVisible, value); }

    public ObservableCollection<TrackUsageDotViewModel> TrackDots { get; } = new();

    public void Apply(WeatherStatus status)
    {
        AirTempText = status.AirTempC.ToString("0.#", CultureInfo.InvariantCulture) + "°C";
        TrackTempText = status.TrackTempC.ToString("0.#", CultureInfo.InvariantCulture) + "°C";
        PrecipitationText = status.PrecipitationPct.ToString("0", CultureInfo.InvariantCulture) + "%";
        WetnessText = status.TrackWetness >= 0 && status.TrackWetness < WetnessLabels.Length
            ? WetnessLabels[status.TrackWetness]
            : "--";
        DeclaredWetVisible = status.WeatherDeclaredWet;

        TrackDots.Clear();
        foreach (var dot in status.CarPositions)
            TrackDots.Add(new TrackUsageDotViewModel(dot, BarWidth, DotSize));
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

- [ ] **Step 2: Write `WeatherWidget.xaml`**

```xml
<!-- src/IracingLiveCoach.App/WeatherWidget.xaml -->
<Window x:Class="IracingLiveCoach.App.WeatherWidget"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Weather"
        WindowStyle="None"
        AllowsTransparency="True"
        Background="Transparent"
        Topmost="True"
        ShowInTaskbar="False"
        MinWidth="260" MinHeight="150"
        SizeToContent="Manual">
    <Grid Background="Transparent" MouseLeftButtonDown="OnBackgroundMouseLeftButtonDown">
        <Border x:Name="OuterBorder" Style="{StaticResource F1PanelBorder}">
            <StackPanel Margin="10">
                <Grid Margin="0,0,0,8">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="Auto" />
                    </Grid.ColumnDefinitions>
                    <TextBlock Grid.Column="0" Text="WEATHER" FontFamily="{StaticResource F1HeaderFont}" FontSize="11"
                               Foreground="{StaticResource F1AccentBrush}" />
                    <Border Grid.Column="1" Background="{StaticResource F1AccentBrush}" Padding="5,1"
                            Visibility="{Binding DeclaredWetVisible, Converter={StaticResource BoolToVisibilityConverter}}">
                        <TextBlock Text="CHUVA" FontFamily="{StaticResource F1MonoFont}" FontSize="9" FontWeight="Bold" Foreground="White" />
                    </Border>
                </Grid>

                <UniformGrid Rows="1" Columns="2" Margin="0,0,0,6">
                    <StackPanel Margin="0,0,8,0">
                        <TextBlock Text="Ar" FontFamily="{StaticResource F1MonoFont}" FontSize="10" Foreground="{StaticResource F1MutedTextBrush}" />
                        <TextBlock Text="{Binding AirTempText}" FontFamily="{StaticResource F1MonoFont}" FontSize="14" Foreground="{StaticResource F1TextBrush}" />
                    </StackPanel>
                    <StackPanel>
                        <TextBlock Text="Pista" FontFamily="{StaticResource F1MonoFont}" FontSize="10" Foreground="{StaticResource F1MutedTextBrush}" />
                        <TextBlock Text="{Binding TrackTempText}" FontFamily="{StaticResource F1MonoFont}" FontSize="14" Foreground="{StaticResource F1TextBrush}" />
                    </StackPanel>
                </UniformGrid>

                <Grid Margin="0,2">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="Auto" />
                    </Grid.ColumnDefinitions>
                    <TextBlock Grid.Column="0" Text="Umidade da pista" FontFamily="{StaticResource F1MonoFont}" FontSize="11" Foreground="{StaticResource F1MutedTextBrush}" />
                    <TextBlock Grid.Column="1" Text="{Binding WetnessText}" FontFamily="{StaticResource F1MonoFont}" FontSize="11" Foreground="{StaticResource F1TextBrush}" />
                </Grid>
                <Grid Margin="0,2">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="Auto" />
                    </Grid.ColumnDefinitions>
                    <TextBlock Grid.Column="0" Text="Precipitação" FontFamily="{StaticResource F1MonoFont}" FontSize="11" Foreground="{StaticResource F1MutedTextBrush}" />
                    <TextBlock Grid.Column="1" Text="{Binding PrecipitationText}" FontFamily="{StaticResource F1MonoFont}" FontSize="11" Foreground="{StaticResource F1TextBrush}" />
                </Grid>

                <TextBlock Text="TRACK USAGE" FontFamily="{StaticResource F1HeaderFont}" FontSize="9"
                           Foreground="{StaticResource F1MutedTextBrush}" Margin="0,10,0,4" />
                <Border Height="16" Width="240" Background="#22FFFFFF" HorizontalAlignment="Left">
                    <Canvas>
                        <ItemsControl ItemsSource="{Binding TrackDots}">
                            <ItemsControl.ItemsPanel>
                                <ItemsPanelTemplate>
                                    <Canvas Width="240" Height="16" />
                                </ItemsPanelTemplate>
                            </ItemsControl.ItemsPanel>
                            <ItemsControl.ItemTemplate>
                                <DataTemplate>
                                    <Ellipse Width="8" Height="8" Fill="{Binding Fill}" ToolTip="{Binding ToolTip}">
                                        <Ellipse.RenderTransform>
                                            <TranslateTransform X="{Binding Left}" Y="4" />
                                        </Ellipse.RenderTransform>
                                    </Ellipse>
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                    </Canvas>
                </Border>
            </StackPanel>
        </Border>
        <Thumb x:Name="ResizeGrip" Width="14" Height="14" Cursor="SizeNWSE"
               HorizontalAlignment="Right" VerticalAlignment="Bottom"
               Background="{StaticResource F1AccentBrush}" Opacity="0.5"
               DragDelta="OnResizeGripDragDelta" DragCompleted="OnResizeGripDragCompleted" />
    </Grid>
</Window>
```

This XAML references a `BoolToVisibilityConverter` static resource that does not yet exist anywhere
in this app. Add it to `src/IracingLiveCoach.App/App.xaml`'s resources (the flat `<ResourceDictionary>`
block that already holds the `Hud*` brushes, NOT `F1Theme.xaml` — this converter is a generic WPF
utility, not part of the F1 visual theme):

```xml
<BooleanToVisibilityConverter x:Key="BoolToVisibilityConverter" />
```

(`System.Windows.Controls.BooleanToVisibilityConverter` is a built-in WPF type — the default
`xmlns` on `Application.xaml`'s root already covers `System.Windows.Controls`, so no new `xmlns`
import is needed.)

- [ ] **Step 3: Write `WeatherWidget.xaml.cs`**

Identical structure to `FuelWidget.xaml.cs` (Task 2, Step 3) — same drag/resize/lock/`Handle`
pattern, same `(WidgetLayout layout, Action onChanged)` constructor, renamed to `WeatherWidget`,
wired to `WeatherWidgetViewModel`/`UpdateStatus(WeatherStatus)` instead. Copy that file's structure
exactly, changing only the class name, the view-model type, and the `UpdateStatus` parameter type.

- [ ] **Step 4: Register it in `MainWindow.xaml.cs`**

Same pattern as Task 2 Step 4: add `private WeatherWidget? _weatherWidget;`, create it in
`Initialize()` (`_layoutStore.Get("weather", 280, 200)`), `.Show()` it, subscribe
`_telemetryReader.WeatherUpdated += status => Dispatcher.Invoke(() => _weatherWidget?.UpdateStatus(status));`,
add `("weather", "Weather", _weatherWidget)` to the `ControlPanelWindow` widget array, wire it into
`ApplyClickThrough()` and `OnClosed`.

- [ ] **Step 5: Verify the build and tests**

Run: `dotnet build` — expect `Build succeeded.`, 0 errors, 0 warnings.
Run: `dotnet test` — expect the same 26 tests passing.

- [ ] **Step 6: Commit**

```bash
git add src/IracingLiveCoach.App/WeatherWidget.xaml src/IracingLiveCoach.App/WeatherWidget.xaml.cs src/IracingLiveCoach.App/WeatherWidgetViewModel.cs src/IracingLiveCoach.App/App.xaml src/IracingLiveCoach.App/MainWindow.xaml.cs
git commit -m "feat: WeatherWidget -- combined weather/wetness/precipitation/track-usage widget"
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
Expected: `Build succeeded.`, 0 errors, 0 warnings; all 26 tests passing.

- [ ] **Step 2: Cross-check every widget-owning file's `Save()`/lock/shutdown wiring**

Per the Phase 1 final review's own biggest lesson (cross-cutting wiring gaps are invisible to a
single task's diff), read the final, current `MainWindow.xaml.cs` in full and confirm ALL SEVEN
windows (Coach/`this`, P2P/`RelativeOverlayWindow`, `ControlPanelWindow`, `RelativeWidget`,
`StandingsWidget`, `FuelWidget`, `WeatherWidget`) are each: registered in the `ControlPanelWindow`
widget array, included in `ApplyClickThrough()`'s `SetLocked` calls, and included in `OnClosed()`'s
`.Close()` calls. Report any window missing from any of these three lists as a finding, not a
silent fix — the final whole-branch review (next task) is the right place to confirm the fix if one
is made now.

- [ ] **Step 3: Commit** (only if Step 2 required a fix; otherwise nothing new to commit here)
