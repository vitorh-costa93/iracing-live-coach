# F1-Style Overlay Suite — Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a shared multi-widget foundation (keyed layout storage, a Control Panel window, an F1-styled visual theme) to the existing `iracing-live-coach` app, and add two brand-new widgets on top of it — a full-running-order Relative and a Standings widget — without touching the internals of the already-shipped Coach card or P2P strip beyond making them read their position/size from the new shared store.

**Architecture:** `MainWindow` (already the app's coordinator: owns the one `TelemetryReader`, the tray icon, the click-through lock) gains a `WidgetLayoutStore` (replacing today's flat `AppSettings` fields with a keyed dictionary, one entry per widget) and a new `ControlPanelWindow` that lists every registered widget with a visibility toggle. `TelemetryReader` gains one new event (`FullRelativeUpdated`) computing a richer per-car row (driver code, gap, tire compound, P2P badge) alongside its existing narrow `RelativeUpdated` (kept as-is for the P2P strip) and one new event (`StandingsUpdated`) for the full classification. Two new windows (`RelativeWidget`, `StandingsWidget`) consume these, styled via a new shared `F1Theme.xaml` resource dictionary.

**Tech Stack:** WPF (.NET 8, unchanged), IRSDKSharper (unchanged), `System.Text.Json` for the settings store (matches `AppSettings`'s existing approach).

**Spec:** `docs/superpowers/specs/2026-09-13-overlay-suite-design.md` (this plan covers only that spec's "Phase 1" table row — Fuel Calculator, the combined weather widget, tire wear, and the spotter radar are explicitly out of scope here, covered by later plans).

## Global Constraints

- Existing Coach card and P2P strip keep their exact current behavior and telemetry logic
  unchanged — this plan only changes WHERE they persist position/size (the new
  `WidgetLayoutStore` instead of `AppSettings`'s old flat fields) and adds them to the Control
  Panel's visibility list. Do not rewrite `MainWindow.xaml`'s coaching-card content or
  `RelativeOverlayWindow.xaml`'s P2P-row content.
- No new telemetry variable is used unless it was independently confirmed to exist this session:
  `CarIdxPosition`, `CarIdxP2P_Status` (already used), `CarIdxEstTime`, `CarIdxTireCompound`,
  `CarIdxLap`, `CarIdxLastLapTime` (all documented on `sajax.github.io/irsdkdocs`), plus
  `DriverModel.AbbrevName`/`CarNumber` (already used for car/track auto-detection this session).
- The lock/unlock click-through toggle (tray icon) must continue to apply to every registered
  widget window at once, including the two new ones — this is the established, already-shipped
  behavior for the existing two windows and must not regress.
- F1 visual styling uses only fonts already available on Windows (no embedded font files in this
  pass) — `Segoe UI Semibold`/`Segoe UI` for a bold condensed-enough broadcast feel, `Consolas` for
  tabular numeric columns. A later pass can embed real broadcast-style fonts if the driver wants
  that extra polish.
- Every new/changed file is verified by `dotnet build` at minimum; SDK-dependent runtime behavior
  (real telemetry reads) is build-verified only, same disclosed boundary the original Live Coach
  plan used for its own SDK-touching task.

---

### Task 1: `WidgetLayoutStore` — keyed layout persistence, replacing `AppSettings`'s flat fields

**Files:**
- Create: `src/IracingLiveCoach.Core/WidgetLayoutStore.cs` (namespace `IracingLiveCoach.Core`, NOT
  `IracingLiveCoach.App` — `IracingLiveCoach.Core.Tests` targets plain `net8.0` and only has a
  `ProjectReference` to `IracingLiveCoach.Core`, the same project `LiveCoachEngine`/`BaselineSync`
  already live in and are already tested from; `IracingLiveCoach.App` targets `net8.0-windows`
  (WPF) and a `net8.0` test project cannot reference a `net8.0-windows` project. `App`'s own files
  already have `using IracingLiveCoach.Core;` (see `MainWindow.xaml.cs`), so this class is usable
  from `App` with no new `using` needed.)
- Test: `tests/IracingLiveCoach.Core.Tests/WidgetLayoutStoreTests.cs` (this test project already
  exists and already references `IracingLiveCoach.Core`; a plain C# class with no WPF types can be
  tested from here the same way `LiveCoachEngine`/`BaselineSync` already are)
- Modify: `src/IracingLiveCoach.App/AppSettings.cs` (keep `ImportKey` here, since it's not a
  per-widget layout concern; nothing else changes in this file in this task)

**Interfaces:**
- Produces: `class WidgetLayout { public double? Left; public double? Top; public double Width;
  public double Height; public bool Visible = true; }`, `class WidgetLayoutStore { public static
  WidgetLayoutStore Load(); public void Save(); public WidgetLayout Get(string key, double
  defaultWidth, double defaultHeight); public IReadOnlyDictionary<string, WidgetLayout> All { get;
  } }` — Task 4 (Control Panel) enumerates `All` to build its widget list; every widget's own
  window class (Tasks 5-6, plus the existing `MainWindow`/`RelativeOverlayWindow` modified in
  Task 4) calls `Get("coach", 320, 200)` / `Get("p2p", 90, 130)` / `Get("relative", 260, 240)` /
  `Get("standings", 320, 360)` respectively.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/IracingLiveCoach.Core.Tests/WidgetLayoutStoreTests.cs
using System;
using System.IO;
using IracingLiveCoach.Core;
using Xunit;

namespace IracingLiveCoach.Core.Tests;

public class WidgetLayoutStoreTests : IDisposable
{
    private readonly string _originalAppData;
    private readonly string _tempAppData = Path.Combine(Path.GetTempPath(), "widget-layout-tests-" + Guid.NewGuid());

    public WidgetLayoutStoreTests()
    {
        _originalAppData = Environment.GetEnvironmentVariable("APPDATA") ?? "";
        Directory.CreateDirectory(_tempAppData);
        Environment.SetEnvironmentVariable("APPDATA", _tempAppData);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("APPDATA", _originalAppData);
        if (Directory.Exists(_tempAppData)) Directory.Delete(_tempAppData, recursive: true);
    }

    [Fact]
    public void Get_returns_the_given_defaults_for_a_never_seen_key()
    {
        var store = WidgetLayoutStore.Load();
        var layout = store.Get("standings", 320, 360);
        Assert.Equal(320, layout.Width);
        Assert.Equal(360, layout.Height);
        Assert.Null(layout.Left);
        Assert.Null(layout.Top);
        Assert.True(layout.Visible);
    }

    [Fact]
    public void Save_then_Load_round_trips_a_modified_layout()
    {
        var store = WidgetLayoutStore.Load();
        var layout = store.Get("relative", 260, 240);
        layout.Left = 100; layout.Top = 50; layout.Width = 300; layout.Visible = false;
        store.Save();

        var reloaded = WidgetLayoutStore.Load();
        var reloadedLayout = reloaded.Get("relative", 260, 240); // defaults ignored -- a saved value exists
        Assert.Equal(100, reloadedLayout.Left);
        Assert.Equal(50, reloadedLayout.Top);
        Assert.Equal(300, reloadedLayout.Width);
        Assert.False(reloadedLayout.Visible);
    }

    [Fact]
    public void Get_is_idempotent_for_the_same_key_within_one_store_instance()
    {
        var store = WidgetLayoutStore.Load();
        var first = store.Get("coach", 320, 200);
        first.Left = 42;
        var second = store.Get("coach", 999, 999); // different defaults, same key -- ignored, same instance returned
        Assert.Equal(42, second.Left);
        Assert.Equal(320, second.Width); // NOT 999 -- the already-created layout wins, defaults only apply once
    }

    [Fact]
    public void All_reflects_every_key_ever_requested_via_Get()
    {
        var store = WidgetLayoutStore.Load();
        store.Get("coach", 320, 200);
        store.Get("standings", 320, 360);
        Assert.Contains("coach", store.All.Keys);
        Assert.Contains("standings", store.All.Keys);
    }

    [Fact]
    public void Load_migrates_the_old_flat_AppSettings_shape_for_the_coach_widget_only()
    {
        var dir = Path.Combine(_tempAppData, "iracing-live-coach");
        Directory.CreateDirectory(dir);
        // Old shape (pre-this-task): flat Left/Top/Width/Height fields alongside ImportKey, no
        // "Widgets" dictionary at all. A driver upgrading from before this plan has this file on
        // disk and must not lose their already-positioned Coach card.
        File.WriteAllText(Path.Combine(dir, "settings.json"), """{"Left":111,"Top":222,"Width":333,"Height":444,"ImportKey":"abc"}""");

        var store = WidgetLayoutStore.Load();
        var coach = store.Get("coach", 320, 200);
        Assert.Equal(111, coach.Left);
        Assert.Equal(222, coach.Top);
        Assert.Equal(333, coach.Width);
        Assert.Equal(444, coach.Height);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter WidgetLayoutStoreTests`
Expected: FAIL — `WidgetLayoutStore` doesn't exist yet.

- [ ] **Step 3: Write the implementation**

```csharp
// src/IracingLiveCoach.Core/WidgetLayoutStore.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace IracingLiveCoach.Core;

/// <summary>One widget's own position/size/visibility. Left/Top are null until the driver has
/// actually moved the widget once (matching AppSettings's own existing Left/Top nullability
/// convention) -- a window with null Left/Top uses WPF's own default startup placement.</summary>
public class WidgetLayout
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool Visible { get; set; } = true;
}

/// <summary>Keyed replacement for AppSettings's old flat Left/Top/Width/Height fields -- one entry
/// per widget ("coach", "p2p", "relative", "standings", and any future key), so adding a widget
/// later never needs a schema change, just a new Get(key, ...) call. Plain JSON under
/// %APPDATA%\iracing-live-coach\settings.json -- the SAME file AppSettings already used, with a
/// "Widgets" dictionary added alongside the pre-existing ImportKey field (both classes read/write
/// disjoint parts of one JSON document via System.Text.Json's own tolerance for unknown
/// properties, so neither class needs to know about the other's fields).</summary>
public class WidgetLayoutStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "iracing-live-coach", "settings.json");

    public Dictionary<string, WidgetLayout> Widgets { get; set; } = new();

    public IReadOnlyDictionary<string, WidgetLayout> All => Widgets;

    public WidgetLayout Get(string key, double defaultWidth, double defaultHeight)
    {
        if (Widgets.TryGetValue(key, out var existing)) return existing;
        var layout = new WidgetLayout { Width = defaultWidth, Height = defaultHeight };
        Widgets[key] = layout;
        return layout;
    }

    public static WidgetLayoutStore Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("Widgets", out var widgetsElement))
                {
                    var widgets = JsonSerializer.Deserialize<Dictionary<string, WidgetLayout>>(widgetsElement.GetRawText());
                    if (widgets is not null) return new WidgetLayoutStore { Widgets = widgets };
                }
                // Pre-this-task shape: flat Left/Top/Width/Height with no "Widgets" dictionary at
                // all. Migrate the coach widget's own already-saved position so an upgrading
                // driver doesn't lose it -- every other (new) widget just starts at its defaults.
                var store = new WidgetLayoutStore();
                if (doc.RootElement.TryGetProperty("Width", out var widthEl))
                {
                    var coach = new WidgetLayout
                    {
                        Left = doc.RootElement.TryGetProperty("Left", out var l) && l.ValueKind != JsonValueKind.Null ? l.GetDouble() : null,
                        Top = doc.RootElement.TryGetProperty("Top", out var t) && t.ValueKind != JsonValueKind.Null ? t.GetDouble() : null,
                        Width = widthEl.GetDouble(),
                        Height = doc.RootElement.TryGetProperty("Height", out var h) ? h.GetDouble() : 200,
                    };
                    store.Widgets["coach"] = coach;
                }
                return store;
            }
        }
        catch
        {
            // Corrupted/unreadable settings file -- fall back to defaults rather than crash on startup.
        }
        return new WidgetLayoutStore();
    }

    public void Save()
    {
        try
        {
            // Preserve ImportKey (owned by AppSettings, read here only to avoid clobbering it --
            // this store never interprets or validates that field, just round-trips it).
            string? importKey = null;
            if (File.Exists(FilePath))
            {
                try
                {
                    var existingDoc = JsonDocument.Parse(File.ReadAllText(FilePath));
                    if (existingDoc.RootElement.TryGetProperty("ImportKey", out var keyEl) && keyEl.ValueKind == JsonValueKind.String)
                        importKey = keyEl.GetString();
                }
                catch { /* ignore -- best effort preservation only */ }
            }

            var dir = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(dir);
            var merged = new Dictionary<string, object?> { ["Widgets"] = Widgets, ["ImportKey"] = importKey };
            File.WriteAllText(FilePath, JsonSerializer.Serialize(merged, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Best-effort -- a failed save shouldn't crash the overlay, just means layout won't persist.
        }
    }
}
```

- [ ] **Step 4: Update `AppSettings.cs` to stop owning layout fields**

Read the current file first. Remove `Left`, `Top`, `Width`, `Height`, `RelativeLeft`, `RelativeTop`,
`RelativeWidth`, `RelativeHeight` and their doc comments — keep only `ImportKey` and the
`Load()`/`Save()` methods reading/writing just that one field (mirroring `WidgetLayoutStore.Save()`'s
own "preserve the other class's field" approach, but in reverse: `AppSettings.Save()` must
similarly read back any existing `Widgets` property from disk first and re-serialize it unchanged,
so saving `ImportKey` alone never wipes out `WidgetLayoutStore`'s own data). Follow the exact same
preserve-the-other-field pattern already written into `WidgetLayoutStore.Save()` above.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter WidgetLayoutStoreTests`
Expected: PASS (5 passed)

Run: `dotnet build`
Expected: `AppSettings.cs`'s existing consumers (`MainWindow.xaml.cs`, `RelativeOverlayWindow.xaml.cs`)
will fail to compile at this point, since they still reference the now-removed fields — this is
expected and is fixed in Task 4, not this task. Confirm the ONLY build errors are in those two
files, referencing the removed `Left`/`Top`/`Width`/`Height`/`Relative*` members specifically (not
some unrelated break), then stop -- do not fix those two files in this task.

- [ ] **Step 6: Commit**

```bash
git add src/IracingLiveCoach.Core/WidgetLayoutStore.cs src/IracingLiveCoach.App/AppSettings.cs tests/IracingLiveCoach.Core.Tests/WidgetLayoutStoreTests.cs
git commit -m "feat: WidgetLayoutStore -- keyed per-widget layout persistence, replacing AppSettings's flat fields"
```

---

### Task 2: `F1Theme.xaml` — shared visual resource dictionary

**Files:**
- Create: `src/IracingLiveCoach.App/F1Theme.xaml`
- Modify: `src/IracingLiveCoach.App/App.xaml` (merge the new dictionary)

**Interfaces:**
- Produces: brush resources `F1BackgroundBrush`, `F1AccentBrush`, `F1TextBrush`,
  `F1MutedTextBrush`, `F1PositionGainBrush`, `F1PositionLossBrush`; a `Style` named
  `F1PanelBorder` (sharp corners, `F1BackgroundBrush` fill); a `Style` named `F1RowAccentBar` (a
  thin `Border` meant to sit on a row's leading edge, colored via its own `BorderBrush` binding,
  not hard-coded, so a row can be colored green/red/neutral by setting one property) — Tasks 5-6
  (the two new widgets) use all of these directly by `StaticResource` key.

- [ ] **Step 1: Write the resource dictionary**

```xml
<!-- src/IracingLiveCoach.App/F1Theme.xaml -->
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- 13/09/2026: shared F1-broadcast-inspired look for the new widgets (Relative, Standings,
         and later Fuel/Weather/Tire/Radar widgets) -- sharp rectangles and a leading colored
         accent bar per row, distinct on purpose from the existing Coach/P2P widgets' rounded HUD
         look (that pair keeps its own established style unchanged). One file so a later re-theme
         touches this file only, not every widget's own XAML. -->
    <SolidColorBrush x:Key="F1BackgroundBrush" Color="#E60A0E14" />
    <SolidColorBrush x:Key="F1AccentBrush" Color="#FFE10600" />
    <SolidColorBrush x:Key="F1TextBrush" Color="#FFF2F4F7" />
    <SolidColorBrush x:Key="F1MutedTextBrush" Color="#FF9AA3AF" />
    <SolidColorBrush x:Key="F1PositionGainBrush" Color="#FF2DE2B2" />
    <SolidColorBrush x:Key="F1PositionLossBrush" Color="#FFE10600" />
    <FontFamily x:Key="F1HeaderFont">Segoe UI Semibold</FontFamily>
    <FontFamily x:Key="F1MonoFont">Consolas</FontFamily>

    <Style x:Key="F1PanelBorder" TargetType="Border">
        <Setter Property="Background" Value="{StaticResource F1BackgroundBrush}" />
        <Setter Property="BorderBrush" Value="{StaticResource F1AccentBrush}" />
        <Setter Property="BorderThickness" Value="0,2,0,0" />
        <Setter Property="CornerRadius" Value="0" />
    </Style>

    <!-- One row's own leading-edge accent bar -- BorderBrush is left unset here on purpose so
         each row template binds it per-row (neutral/gain/loss), matching real broadcast graphics'
         own gained/lost-position left-edge stripe convention, not a fixed decorative color. -->
    <Style x:Key="F1RowAccentBar" TargetType="Border">
        <Setter Property="Width" Value="3" />
        <Setter Property="HorizontalAlignment" Value="Left" />
    </Style>
</ResourceDictionary>
```

- [ ] **Step 2: Merge it into `App.xaml`**

Read the current `App.xaml` (already has an `<Application.Resources>` block with the existing
`Hud*` brushes from the Coach/P2P widgets — do not remove or rename any of those, they're still
used unchanged). Wrap the existing flat resources in a `ResourceDictionary.MergedDictionaries`
alongside the new file:

```xml
<Application.Resources>
    <ResourceDictionary>
        <ResourceDictionary.MergedDictionaries>
            <ResourceDictionary Source="F1Theme.xaml" />
        </ResourceDictionary.MergedDictionaries>
        <!-- existing HudBackgroundBrush / HudBorderBrush / etc. stay exactly as they are, moved
             here unchanged as this same ResourceDictionary's own direct children -->
    </ResourceDictionary>
</Application.Resources>
```

- [ ] **Step 3: Verify the build**

Run: `dotnet build`
Expected: `Build succeeded.` — a resource-only change; if this fails, the XAML has a syntax error
(unbalanced tag, bad key) — fix before proceeding.

- [ ] **Step 4: Commit**

```bash
git add src/IracingLiveCoach.App/F1Theme.xaml src/IracingLiveCoach.App/App.xaml
git commit -m "feat: F1Theme.xaml -- shared broadcast-style resource dictionary for the new widgets"
```

---

### Task 3: `TelemetryReader` — `FullRelativeUpdated` and `StandingsUpdated` events

**Files:**
- Modify: `src/IracingLiveCoach.App/TelemetryReader.cs`

**Interfaces:**
- Consumes: nothing new from other tasks (this task only touches `TelemetryReader.cs`).
- Produces: `public record RelativeRow(int PositionOffset, string DriverCode, double? GapSeconds,
  int? TireCompound, bool? P2PActive)`, `public record StandingsRow(int Position, string
  DriverCode, int LapsCompleted, double? LastLapTime, int? TireCompound, bool IsPlayer)`, `public
  event Action<List<RelativeRow>>? FullRelativeUpdated;`, `public event
  Action<List<StandingsRow>>? StandingsUpdated;` — Task 5's `RelativeWidget` consumes
  `FullRelativeUpdated`/`RelativeRow`; Task 6's `StandingsWidget` consumes
  `StandingsUpdated`/`StandingsRow`. The EXISTING `RelativeCarStatus`/`RelativeUpdated` (used by
  the already-shipped P2P strip) are untouched -- both the old narrow event and the new rich one
  fire from the same tick, computed independently, so the P2P strip's own behavior cannot regress.

- [ ] **Step 1: Add a driver-lookup helper and the new record types**

At the top of the file, alongside the existing `RelativeCarStatus` record:

```csharp
public record RelativeRow(int PositionOffset, string DriverCode, double? GapSeconds, int? TireCompound, bool? P2PActive);
public record StandingsRow(int Position, string DriverCode, int LapsCompleted, double? LastLapTime, int? TireCompound, bool IsPlayer);
```

Add a private field and helper method to the `TelemetryReader` class (near `_playerCarIdx`):

```csharp
    private Dictionary<int, string> _driverCodesByCarIdx = new();

    // Populated once alongside SessionDetected/_playerCarIdx -- driver identities don't change
    // mid-session, so this is read once from OnSessionInfo, not re-parsed every telemetry tick.
    private static Dictionary<int, string> BuildDriverCodes(IRacingSdkSessionInfo? sessionInfo)
    {
        var map = new Dictionary<int, string>();
        foreach (var driver in sessionInfo?.DriverInfo?.Drivers ?? new List<IRacingSdkSessionInfo.DriverInfoModel.DriverModel>())
        {
            var code = !string.IsNullOrWhiteSpace(driver.AbbrevName) ? driver.AbbrevName : driver.CarNumber ?? "?";
            map[driver.CarIdx] = code;
        }
        return map;
    }
```

- [ ] **Step 2: Populate `_driverCodesByCarIdx` in `OnSessionInfo`**

In `OnSessionInfo`, right after `_playerCarIdx = driverCarIdx;` (before `_sessionDetected = true;`),
add:

```csharp
            _driverCodesByCarIdx = BuildDriverCodes(sessionInfo);
```

- [ ] **Step 3: Add the two public events**

Alongside the existing `RelativeUpdated` event declaration:

```csharp
    /// <summary>Fires every telemetry tick once the player's own position is known, with one row
    /// per nearby car (3 ahead, 3 behind, same window as the existing P2P-only RelativeUpdated)
    /// but carrying driver code/gap/tire/P2P together -- feeds the new full RelativeWidget
    /// (Task 5), distinct from the existing narrow P2P strip which keeps consuming
    /// RelativeUpdated unchanged.</summary>
    public event Action<List<RelativeRow>>? FullRelativeUpdated;

    /// <summary>Fires every telemetry tick once the session is detected, with one row per
    /// currently-classified car (CarIdxPosition > 0), ordered by position.</summary>
    public event Action<List<StandingsRow>>? StandingsUpdated;
```

- [ ] **Step 4: Compute and fire both from `OnTelemetryData`**

In `OnTelemetryData`, change:
```csharp
        if (_playerCarIdx >= 0) UpdateRelative();
```
to:
```csharp
        if (_playerCarIdx >= 0)
        {
            UpdateRelative();
            UpdateFullRelative();
            UpdateStandings();
        }
```

Add the two new private methods, right after the existing `UpdateRelative`:

```csharp
    // 13/09/2026: full running-order relative (F1-style widget), extending 3-ahead/3-behind --
    // reads the SAME CarIdxPosition scan as UpdateRelative but is a SEPARATE pass (not merged into
    // it) so a change here can never affect the already-shipped P2P strip's own behavior.
    private void UpdateFullRelative()
    {
        try
        {
            var myPosition = _sdk.Data.GetInt("CarIdxPosition", _playerCarIdx);
            if (myPosition <= 0) return;
            var myEstTime = _sdk.Data.GetFloat("CarIdxEstTime", _playerCarIdx);

            var maxCars = IRacingSdkConst.MaxNumCars;
            var rows = new List<RelativeRow>();
            for (var idx = 0; idx < maxCars; idx++)
            {
                if (idx == _playerCarIdx) continue;
                var position = _sdk.Data.GetInt("CarIdxPosition", idx);
                if (position <= 0) continue;
                var offset = position - myPosition;
                if (Math.Abs(offset) > RelativeCarsBehind) continue;

                var theirEstTime = _sdk.Data.GetFloat("CarIdxEstTime", idx);
                // Simple same-lap gap estimate -- does not correct for a lap-count difference
                // between the two cars, a known, disclosed simplification for this first version
                // (see this task's own plan text / the spec's Phase 1 scope).
                double gap = theirEstTime - myEstTime;
                var tireCompound = _sdk.Data.GetInt("CarIdxTireCompound", idx);
                bool? p2p = null;
                try { p2p = _sdk.Data.GetBool("CarIdxP2P_Status", idx); } catch { /* no P2P this session */ }

                var code = _driverCodesByCarIdx.TryGetValue(idx, out var driverCode) ? driverCode : "?";
                rows.Add(new RelativeRow(offset, code, gap, tireCompound >= 0 ? tireCompound : null, p2p));
            }

            FullRelativeUpdated?.Invoke(rows.OrderBy(row => row.PositionOffset).ToList());
        }
        catch
        {
            // Skip this tick -- same defensive posture as every other telemetry read in this class.
        }
    }

    private void UpdateStandings()
    {
        try
        {
            var maxCars = IRacingSdkConst.MaxNumCars;
            var rows = new List<StandingsRow>();
            for (var idx = 0; idx < maxCars; idx++)
            {
                var position = _sdk.Data.GetInt("CarIdxPosition", idx);
                if (position <= 0) continue; // not currently classified

                var lapsCompleted = _sdk.Data.GetInt("CarIdxLap", idx);
                var lastLap = _sdk.Data.GetFloat("CarIdxLastLapTime", idx);
                var tireCompound = _sdk.Data.GetInt("CarIdxTireCompound", idx);
                var code = _driverCodesByCarIdx.TryGetValue(idx, out var driverCode) ? driverCode : "?";

                rows.Add(new StandingsRow(position, code, lapsCompleted, lastLap > 0 ? lastLap : null, tireCompound >= 0 ? tireCompound : null, idx == _playerCarIdx));
            }

            StandingsUpdated?.Invoke(rows.OrderBy(row => row.Position).ToList());
        }
        catch
        {
            // Skip this tick.
        }
    }
```

- [ ] **Step 5: Verify the build**

Run: `dotnet build`
Expected: `Build succeeded.` No existing tests reference `TelemetryReader` directly (it depends on
the real IRSDKSharper SDK type, same established boundary as the rest of this class) -- confirm
with `dotnet test` that the existing 21 tests still pass unchanged.

- [ ] **Step 6: Commit**

```bash
git add src/IracingLiveCoach.App/TelemetryReader.cs
git commit -m "feat: FullRelativeUpdated and StandingsUpdated telemetry events for the new F1-style widgets"
```

---

### Task 4: `ControlPanelWindow` + wire `MainWindow`/`RelativeOverlayWindow` onto `WidgetLayoutStore`

**Files:**
- Create: `src/IracingLiveCoach.App/ControlPanelWindow.xaml`
- Create: `src/IracingLiveCoach.App/ControlPanelWindow.xaml.cs`
- Create: `src/IracingLiveCoach.App/ControlPanelViewModel.cs`
- Modify: `src/IracingLiveCoach.App/MainWindow.xaml.cs` (replace `AppSettings _settings` layout
  reads with `WidgetLayoutStore`; add the `ControlPanelWindow`; extend `ApplyClickThrough`/
  `ToggleLock` to include it and the two new widgets from Tasks 5-6)
- Modify: `src/IracingLiveCoach.App/RelativeOverlayWindow.xaml.cs` (same `WidgetLayoutStore`
  swap, same shape of change as `MainWindow`)

**Interfaces:**
- Consumes: `WidgetLayoutStore`/`WidgetLayout` (Task 1). `F1Theme.xaml` resources (Task 2), for
  this window's own styling (it is itself one of the new-style widgets, per the spec: "restyled to
  match the F1 visual language").
- Produces: `ControlPanelWindow` with a public `void SetLocked(bool locked)` method (same
  signature/contract `RelativeOverlayWindow` already exposes, so `MainWindow`'s existing
  `ApplyClickThrough` can call it identically) and a constructor `ControlPanelWindow(WidgetLayoutStore
  store, IEnumerable<(string Key, string DisplayName, Window Window)> widgets)` -- Task 5/6's
  widgets are registered into this list by `MainWindow.xaml.cs`, alongside the Coach/P2P windows.

- [ ] **Step 1: Write `ControlPanelViewModel.cs`**

```csharp
// src/IracingLiveCoach.App/ControlPanelViewModel.cs
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace IracingLiveCoach.App;

public class ControlPanelRowViewModel : INotifyPropertyChanged
{
    private bool _visible;
    public string DisplayName { get; }
    public string Key { get; }

    public bool Visible
    {
        get => _visible;
        set
        {
            if (_visible == value) return;
            _visible = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Visible)));
            VisibilityChanged?.Invoke(value);
        }
    }

    public event System.Action<bool>? VisibilityChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    public ControlPanelRowViewModel(string key, string displayName, bool initiallyVisible)
    {
        Key = key;
        DisplayName = displayName;
        _visible = initiallyVisible;
    }
}

public class ControlPanelViewModel
{
    public ObservableCollection<ControlPanelRowViewModel> Rows { get; } = new();
}
```

- [ ] **Step 2: Write `ControlPanelWindow.xaml`**

```xml
<!-- src/IracingLiveCoach.App/ControlPanelWindow.xaml -->
<Window x:Class="IracingLiveCoach.App.ControlPanelWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Painel de Controle"
        WindowStyle="None"
        AllowsTransparency="True"
        Background="Transparent"
        Topmost="True"
        ShowInTaskbar="False"
        MinWidth="220" MinHeight="120"
        SizeToContent="Manual">
    <Border Style="{StaticResource F1PanelBorder}">
        <StackPanel Margin="10">
            <TextBlock Text="PAINEL DE CONTROLE" FontFamily="{StaticResource F1HeaderFont}"
                       FontSize="12" Foreground="{StaticResource F1AccentBrush}" Margin="0,0,0,8" />
            <ItemsControl ItemsSource="{Binding Rows}">
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <CheckBox IsChecked="{Binding Visible, Mode=TwoWay}" Content="{Binding DisplayName}"
                                  Foreground="{StaticResource F1TextBrush}" Margin="0,3" />
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </StackPanel>
    </Border>
</Window>
```

- [ ] **Step 3: Write `ControlPanelWindow.xaml.cs`**

```csharp
// src/IracingLiveCoach.App/ControlPanelWindow.xaml.cs
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Interop;
using IracingLiveCoach.Core;
using Brush = System.Windows.Media.Brush;

namespace IracingLiveCoach.App;

/// <summary>Lists every registered widget with a visibility checkbox -- the direct equivalent of
/// Kapps' own layersControlPanel (see the spec's own reference to %AppData%\Kapps\settings.json),
/// restyled to the F1 theme. Unlike every other widget window, this one is EXCLUDED from the
/// shared click-through lock (it must stay interactive to be useful) but hides its own content
/// entirely while the suite is locked, matching Kapps' own "only appears when configuring
/// something" behavior.</summary>
public partial class ControlPanelWindow : Window
{
    private readonly WidgetLayoutStore _store;
    private readonly ControlPanelViewModel _viewModel = new();
    private readonly Dictionary<string, Window> _widgetsByKey = new();

    public IntPtr Handle => new WindowInteropHelper(this).Handle;

    public ControlPanelWindow(WidgetLayoutStore store, IEnumerable<(string Key, string DisplayName, Window Window)> widgets)
    {
        InitializeComponent();
        _store = store;
        DataContext = _viewModel;

        var layout = _store.Get("controlPanel", 220, 160);
        Width = layout.Width;
        Height = layout.Height;
        if (layout.Left is double left && layout.Top is double top) { WindowStartupLocation = WindowStartupLocation.Manual; Left = left; Top = top; }

        foreach (var (key, displayName, window) in widgets)
        {
            _widgetsByKey[key] = window;
            var widgetLayout = _store.Get(key, window.Width, window.Height);
            var row = new ControlPanelRowViewModel(key, displayName, widgetLayout.Visible);
            row.VisibilityChanged += visible =>
            {
                widgetLayout.Visible = visible;
                window.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                _store.Save();
            };
            _viewModel.Rows.Add(row);
            window.Visibility = widgetLayout.Visible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    // Locking hides this window's own content entirely (Visibility), not just click-through --
    // per this class's own doc comment, it should never be visible while actually driving.
    public void SetLocked(bool locked) => Visibility = locked ? Visibility.Collapsed : Visibility.Visible;
}
```

- [ ] **Step 4: Update `MainWindow.xaml.cs`**

Read the current file in full first. Replace the `AppSettings _settings = AppSettings.Load();`
field with two fields:
```csharp
    private readonly AppSettings _appSettings = AppSettings.Load();
    private readonly WidgetLayoutStore _layoutStore = WidgetLayoutStore.Load();
```
Update every reference to `_settings.Width`/`_settings.Height`/`_settings.Left`/`_settings.Top`/
`_settings.ImportKey` in this file: the layout ones (`Width`/`Height`/`Left`/`Top`) now come from
`_layoutStore.Get("coach", 320, 200)` (call this once, store the returned `WidgetLayout` in a new
field `_coachLayout`, and read/write its properties instead of `_settings`'s old flat ones);
`ImportKey` stays reading from `_appSettings.ImportKey` unchanged. `PersistLayout()` now writes
into `_coachLayout` and calls `_layoutStore.Save()` instead of `_settings.Save()`.

In `Initialize()`, after creating `_relativeWindow` (unchanged) add the `ControlPanelWindow` and
register both existing windows plus a placeholder for Task 5/6's not-yet-created windows (Task 4
only wires up the two ALREADY-EXISTING widgets; Tasks 5-6 each add one more registration line to
this same list when they land):

```csharp
        _controlPanel = new ControlPanelWindow(_layoutStore, new (string, string, Window)[]
        {
            ("coach", "Coach", this),
            ("p2p", "P2P", _relativeWindow),
        });
        _controlPanel.Show();
```//

Add the field `private ControlPanelWindow? _controlPanel;` alongside the other fields. In
`ApplyClickThrough()`, add `_controlPanel?.SetLocked(_locked);` alongside the existing
`_relativeWindow?.SetLocked(_locked);` line. In `OnClosed`, add `_controlPanel?.Close();` and
`_layoutStore.Save();` (replacing the old `PersistLayout()`'s direct settings save if it isn't
already covered).

- [ ] **Step 5: Update `RelativeOverlayWindow.xaml.cs`**

Read the current file. It does not currently have a `using IracingLiveCoach.Core;` line (it only
uses `RelativeCarStatus`, which lives in `IracingLiveCoach.App`'s own `TelemetryReader.cs`) — add
one, since its constructor is changing to accept a `WidgetLayout` (Task 1, `IracingLiveCoach.Core`
namespace). Change its constructor from `RelativeOverlayWindow(AppSettings settings)`
to `RelativeOverlayWindow(WidgetLayout layout)` (it no longer needs a whole `AppSettings`/
`WidgetLayoutStore` reference, just its OWN already-resolved layout object, since `MainWindow` now
owns the one shared `WidgetLayoutStore` and passes each widget only its own slice). Update its
`PersistLayout()` to write into the passed-in `layout` object directly (no `.Save()` call inside
this class anymore -- `MainWindow`'s own `WidgetLayoutStore.Save()` call, made whenever ANY layout
changes, covers this window's data too, since they share the same in-memory `Widgets` dictionary
object). Update the call site in `MainWindow.xaml.cs`'s `Initialize()`:
```csharp
        _relativeWindow = new RelativeOverlayWindow(_layoutStore.Get("p2p", 90, 130));
```
Since `RelativeOverlayWindow` no longer calls `.Save()` itself, `MainWindow` must call
`_layoutStore.Save()` after any layout-affecting event from ANY window -- add a
`_relativeWindow.LayoutChanged += () => _layoutStore.Save();` style hook, or simplest: have
`RelativeOverlayWindow`'s `PersistLayout()` accept a callback `Action onChanged` passed in from
`MainWindow` (`new RelativeOverlayWindow(_layoutStore.Get("p2p", 90, 130), () => _layoutStore.Save())`)
and invoke it instead of `.Save()` directly. Use this same `Action onChanged` callback pattern for
Task 5/6's own widgets too, for consistency.

- [ ] **Step 6: Verify the build and existing tests**

Run: `dotnet build`
Expected: `Build succeeded.`, 0 errors (this is the point where Task 1's Step 5 deliberate build
errors get fixed).

Run: `dotnet test`
Expected: same pass count as before this task (21 Core tests + the new WidgetLayoutStoreTests from
Task 1 = 26), no regressions.

- [ ] **Step 7: Commit**

```bash
git add src/IracingLiveCoach.App/ControlPanelWindow.xaml src/IracingLiveCoach.App/ControlPanelWindow.xaml.cs src/IracingLiveCoach.App/ControlPanelViewModel.cs src/IracingLiveCoach.App/MainWindow.xaml.cs src/IracingLiveCoach.App/RelativeOverlayWindow.xaml.cs
git commit -m "feat: ControlPanelWindow, migrating Coach/P2P widgets onto WidgetLayoutStore"
```

---

### Task 5: `RelativeWidget` — F1-style full running order with P2P badge

**Files:**
- Create: `src/IracingLiveCoach.App/RelativeWidget.xaml`
- Create: `src/IracingLiveCoach.App/RelativeWidget.xaml.cs`
- Create: `src/IracingLiveCoach.App/RelativeWidgetViewModel.cs`
- Modify: `src/IracingLiveCoach.App/MainWindow.xaml.cs` (create + register this widget)

**Interfaces:**
- Consumes: `TelemetryReader.FullRelativeUpdated`/`RelativeRow` (Task 3), `WidgetLayoutStore`
  (Task 1), F1 theme resources (Task 2), the `(WidgetLayout layout, Action onChanged)` constructor
  pattern established in Task 4.

- [ ] **Step 1: Write `RelativeWidgetViewModel.cs`**

```csharp
// src/IracingLiveCoach.App/RelativeWidgetViewModel.cs
using System.Collections.ObjectModel;
using System.Globalization;
using Brush = System.Windows.Media.Brush;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using Color = System.Windows.Media.Color;
using IracingLiveCoach.App;

namespace IracingLiveCoach.App;

public class RelativeRowViewModel
{
    public string DriverCode { get; }
    public string GapText { get; }
    public string P2PText { get; }
    public Brush P2PBrush { get; }
    public bool IsPlayerRow { get; }

    private static readonly Brush WarnBrush = new SolidColorBrush(Color.FromRgb(0xE1, 0x06, 0x00));
    private static readonly Brush NeutralBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0xA3, 0xAF));

    public RelativeRowViewModel(RelativeRow row)
    {
        IsPlayerRow = row.PositionOffset == 0;
        DriverCode = row.DriverCode;
        GapText = row.GapSeconds is double gap ? $"{(gap >= 0 ? "+" : "")}{gap.ToString("0.0", CultureInfo.InvariantCulture)}" : "--";
        (P2PText, P2PBrush) = row.P2PActive switch
        {
            true => ("P2P", WarnBrush),
            false => ("", NeutralBrush),
            null => ("", NeutralBrush),
        };
    }
}

public class RelativeWidgetViewModel
{
    public ObservableCollection<RelativeRowViewModel> Rows { get; } = new();

    public void SetRows(System.Collections.Generic.List<RelativeRow> rows)
    {
        Rows.Clear();
        foreach (var row in rows) Rows.Add(new RelativeRowViewModel(row));
    }
}
```

- [ ] **Step 2: Write `RelativeWidget.xaml`**

```xml
<!-- src/IracingLiveCoach.App/RelativeWidget.xaml -->
<Window x:Class="IracingLiveCoach.App.RelativeWidget"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Relative"
        WindowStyle="None"
        AllowsTransparency="True"
        Background="Transparent"
        Topmost="True"
        ShowInTaskbar="False"
        MinWidth="200" MinHeight="150"
        SizeToContent="Manual">
    <Grid Background="Transparent" MouseLeftButtonDown="OnBackgroundMouseLeftButtonDown">
        <Border x:Name="OuterBorder" Style="{StaticResource F1PanelBorder}">
            <StackPanel Margin="8">
                <TextBlock Text="RELATIVE" FontFamily="{StaticResource F1HeaderFont}" FontSize="11"
                           Foreground="{StaticResource F1AccentBrush}" Margin="0,0,0,6" />
                <ItemsControl ItemsSource="{Binding Rows}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Grid Margin="0,2">
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="*" />
                                    <ColumnDefinition Width="Auto" />
                                    <ColumnDefinition Width="Auto" />
                                </Grid.ColumnDefinitions>
                                <TextBlock Grid.Column="0" Text="{Binding DriverCode}" FontFamily="{StaticResource F1MonoFont}"
                                           FontSize="12" Foreground="{StaticResource F1TextBrush}"
                                           FontWeight="Bold" />
                                <TextBlock Grid.Column="1" Text="{Binding GapText}" FontFamily="{StaticResource F1MonoFont}"
                                           FontSize="12" Foreground="{StaticResource F1MutedTextBrush}" Margin="8,0" />
                                <TextBlock Grid.Column="2" Text="{Binding P2PText}" FontFamily="{StaticResource F1MonoFont}"
                                           FontSize="11" FontWeight="Bold" Foreground="{Binding P2PBrush}" Width="28" />
                            </Grid>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </StackPanel>
        </Border>
        <Thumb x:Name="ResizeGrip" Width="14" Height="14" Cursor="SizeNWSE"
               HorizontalAlignment="Right" VerticalAlignment="Bottom"
               Background="{StaticResource F1AccentBrush}" Opacity="0.5"
               DragDelta="OnResizeGripDragDelta" DragCompleted="OnResizeGripDragCompleted" />
    </Grid>
</Window>
```

The XAML above already uses a plain static `FontWeight="Bold"` for every row's driver code, so
`RelativeRowViewModel.IsPlayerRow` (already computed in the view model above) is currently unused
by the XAML -- that's expected for this task. Visually distinguishing the player's own row (e.g. a
highlighted row background bound to `IsPlayerRow`) is a nice-to-have polish item, explicitly out of
scope here; do not introduce an `IValueConverter` or extra styling for it.

- [ ] **Step 3: Write `RelativeWidget.xaml.cs`**

Mirror `RelativeOverlayWindow.xaml.cs`'s exact drag/resize/lock structure (read that file for the
precise pattern: `OnBackgroundMouseLeftButtonDown` calling `DragMove()` then persisting,
`OnResizeGripDragDelta`/`OnResizeGripDragCompleted`, a `SetLocked(bool)` method toggling
`ClickThrough.Set` + the border brush, an `IntPtr Handle` property). Use the `(WidgetLayout
layout, Action onChanged)` constructor shape established in Task 4:

```csharp
// src/IracingLiveCoach.App/RelativeWidget.xaml.cs
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using IracingLiveCoach.Core;
using Brush = System.Windows.Media.Brush;

namespace IracingLiveCoach.App;

public partial class RelativeWidget : Window
{
    private readonly WidgetLayout _layout;
    private readonly Action _onChanged;
    private readonly RelativeWidgetViewModel _viewModel = new();

    public IntPtr Handle => new WindowInteropHelper(this).Handle;

    public RelativeWidget(WidgetLayout layout, Action onChanged)
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

    public void UpdateRows(List<RelativeRow> rows) => _viewModel.SetRows(rows);

    public void SetLocked(bool locked)
    {
        ClickThrough.Set(Handle, locked);
        OuterBorder.BorderBrush = locked
            ? (Brush)FindResource("F1AccentBrush")
            : (Brush)FindResource("F1AccentBrush"); // no separate "unlocked" accent yet in F1Theme -- same brush both states for this first version, a visual "you're in edit mode" cue is a follow-up polish item like the Coach widget's own HudBorderActiveBrush pattern
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

In `Initialize()`, after creating `_relativeWindow`, add:
```csharp
        _relativeWidget = new RelativeWidget(_layoutStore.Get("relative", 260, 240), () => _layoutStore.Save());
        _telemetryReader.FullRelativeUpdated += rows => Dispatcher.Invoke(() => _relativeWidget?.UpdateRows(rows));
```
(field declaration `private RelativeWidget? _relativeWidget;` alongside the others). Add
`("relative", "Relative (F1)", _relativeWidget)` to the `ControlPanelWindow` constructor's widget
list (from Task 4 Step 4). Add `_relativeWidget?.SetLocked(_locked);` to `ApplyClickThrough()`.
Add `_relativeWidget?.Close();` to `OnClosed`.

- [ ] **Step 5: Verify the build**

Run: `dotnet build`
Expected: `Build succeeded.`

Run: `dotnet test`
Expected: same pass count as after Task 4 (this task adds no new automated tests -- it's WPF UI
code depending on the real SDK types, same disclosed boundary as the rest of this app's widget
windows).

- [ ] **Step 6: Commit**

```bash
git add src/IracingLiveCoach.App/RelativeWidget.xaml src/IracingLiveCoach.App/RelativeWidget.xaml.cs src/IracingLiveCoach.App/RelativeWidgetViewModel.cs src/IracingLiveCoach.App/MainWindow.xaml.cs
git commit -m "feat: RelativeWidget -- F1-style full running order with P2P badge"
```

---

### Task 6: `StandingsWidget` — F1-style full classification

**Files:**
- Create: `src/IracingLiveCoach.App/StandingsWidget.xaml`
- Create: `src/IracingLiveCoach.App/StandingsWidget.xaml.cs`
- Create: `src/IracingLiveCoach.App/StandingsWidgetViewModel.cs`
- Modify: `src/IracingLiveCoach.App/MainWindow.xaml.cs` (create + register this widget)

**Interfaces:**
- Consumes: `TelemetryReader.StandingsUpdated`/`StandingsRow` (Task 3), same
  `(WidgetLayout, Action onChanged)` constructor pattern, same drag/resize/lock structure as
  `RelativeWidget` (Task 5) and `RelativeOverlayWindow`.

- [ ] **Step 1: Write `StandingsWidgetViewModel.cs`**

```csharp
// src/IracingLiveCoach.App/StandingsWidgetViewModel.cs
using System.Collections.ObjectModel;
using System.Globalization;

namespace IracingLiveCoach.App;

public class StandingsRowViewModel
{
    public string PositionText { get; }
    public string DriverCode { get; }
    public string LastLapText { get; }

    public StandingsRowViewModel(StandingsRow row)
    {
        PositionText = row.Position.ToString(CultureInfo.InvariantCulture);
        DriverCode = row.IsPlayer ? $"{row.DriverCode} *" : row.DriverCode;
        LastLapText = row.LastLapTime is double t ? t.ToString("0.000", CultureInfo.InvariantCulture) : "--";
    }
}

public class StandingsWidgetViewModel
{
    public ObservableCollection<StandingsRowViewModel> Rows { get; } = new();

    public void SetRows(System.Collections.Generic.List<StandingsRow> rows)
    {
        Rows.Clear();
        foreach (var row in rows) Rows.Add(new StandingsRowViewModel(row));
    }
}
```

- [ ] **Step 2: Write `StandingsWidget.xaml`**

```xml
<!-- src/IracingLiveCoach.App/StandingsWidget.xaml -->
<Window x:Class="IracingLiveCoach.App.StandingsWidget"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Standings"
        WindowStyle="None"
        AllowsTransparency="True"
        Background="Transparent"
        Topmost="True"
        ShowInTaskbar="False"
        MinWidth="220" MinHeight="180"
        SizeToContent="Manual">
    <Grid Background="Transparent" MouseLeftButtonDown="OnBackgroundMouseLeftButtonDown">
        <Border x:Name="OuterBorder" Style="{StaticResource F1PanelBorder}">
            <StackPanel Margin="8">
                <TextBlock Text="STANDINGS" FontFamily="{StaticResource F1HeaderFont}" FontSize="11"
                           Foreground="{StaticResource F1AccentBrush}" Margin="0,0,0,6" />
                <ScrollViewer VerticalScrollBarVisibility="Auto" MaxHeight="400">
                    <ItemsControl ItemsSource="{Binding Rows}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Grid Margin="0,2">
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="24" />
                                        <ColumnDefinition Width="*" />
                                        <ColumnDefinition Width="Auto" />
                                    </Grid.ColumnDefinitions>
                                    <TextBlock Grid.Column="0" Text="{Binding PositionText}" FontFamily="{StaticResource F1MonoFont}"
                                               FontSize="12" FontWeight="Bold" Foreground="{StaticResource F1AccentBrush}" />
                                    <TextBlock Grid.Column="1" Text="{Binding DriverCode}" FontFamily="{StaticResource F1MonoFont}"
                                               FontSize="12" Foreground="{StaticResource F1TextBrush}" Margin="6,0" />
                                    <TextBlock Grid.Column="2" Text="{Binding LastLapText}" FontFamily="{StaticResource F1MonoFont}"
                                               FontSize="11" Foreground="{StaticResource F1MutedTextBrush}" />
                                </Grid>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </ScrollViewer>
            </StackPanel>
        </Border>
        <Thumb x:Name="ResizeGrip" Width="14" Height="14" Cursor="SizeNWSE"
               HorizontalAlignment="Right" VerticalAlignment="Bottom"
               Background="{StaticResource F1AccentBrush}" Opacity="0.5"
               DragDelta="OnResizeGripDragDelta" DragCompleted="OnResizeGripDragCompleted" />
    </Grid>
</Window>
```

- [ ] **Step 3: Write `StandingsWidget.xaml.cs`**

Identical structure to `RelativeWidget.xaml.cs` (Task 5, Step 3) -- same drag/resize/lock/Handle
pattern, same `(WidgetLayout layout, Action onChanged)` constructor, just renamed to
`StandingsWidget` and wired to `StandingsWidgetViewModel`/`UpdateRows(List<StandingsRow>)`
instead. Copy that file's structure exactly, changing only the class name, the view-model type,
and the `UpdateRows` parameter type.

- [ ] **Step 4: Register it in `MainWindow.xaml.cs`**

Same pattern as Task 5 Step 4: create `_standingsWidget`, subscribe
`_telemetryReader.StandingsUpdated += rows => Dispatcher.Invoke(() => _standingsWidget?.UpdateRows(rows));`,
add `("standings", "Standings (F1)", _standingsWidget)` to the `ControlPanelWindow` widget list,
add it to `ApplyClickThrough()` and `OnClosed`.

- [ ] **Step 5: Verify the build**

Run: `dotnet build`
Expected: `Build succeeded.`

Run: `dotnet test`
Expected: same pass count as after Task 5.

- [ ] **Step 6: Commit**

```bash
git add src/IracingLiveCoach.App/StandingsWidget.xaml src/IracingLiveCoach.App/StandingsWidget.xaml.cs src/IracingLiveCoach.App/StandingsWidgetViewModel.cs src/IracingLiveCoach.App/MainWindow.xaml.cs
git commit -m "feat: StandingsWidget -- F1-style full classification"
```

---

### Task 7: Republish and smoke-test

**Files:**
- None (build/publish/manual verification only)

- [ ] **Step 1: Full build and test run**

```bash
dotnet build
dotnet test
```
Expected: `Build succeeded.`, all tests passing (26: 21 original Core tests + 5 new
WidgetLayoutStoreTests).

- [ ] **Step 2: Smoke-test launch (build-verified, per this app's established SDK-touching boundary)**

```bash
dotnet run --project src/IracingLiveCoach.App
```
Expected: FIVE windows appear (Coach, P2P, Relative, Standings, Control Panel), all showing their
own "aguardando sessão"-equivalent idle state, none throwing on startup. Confirm via the Control
Panel's checkboxes that toggling one off actually hides that specific window (`Visibility.Collapsed`)
without affecting the others. Close the app via the tray icon's "Sair" and confirm no exception on
shutdown.

- [ ] **Step 3: Republish**

```bash
powershell -ExecutionPolicy Bypass -File scripts\publish.ps1
```
Expected: same successful publish flow already established for this app; the existing Desktop
shortcut points at the same output path, so no `-Shortcut` re-run is needed.

- [ ] **Step 4: Commit** (only if Steps 1-3 required any fix; if everything passed as committed
in Tasks 1-6, there is nothing new to commit here)
