# F1-Style Overlay Suite — Phase 5 (Broadcast UI Redesign) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restyle `RelativeWidget` and `StandingsWidget` to match the driver's own broadcast-style
reference mockup (dark navy palette, license badge, national flag, live brake bias/track rubber
footer, OT/P2P column), and add a second, auto-detecting Relative instance so two car classes can
be shown side by side at once — all using real telemetry/session data now confirmed against the
actual SDK build this project depends on (see `docs/superpowers/specs/2026-09-13-overlay-suite-design.md`'s
"Visual redesign" section for the full confirmed/not-confirmed inventory and how it was verified).

**Architecture:** `F1Theme.xaml`'s brush *values* are updated in place (same resource keys, new
palette) so every existing widget picks up the new look with zero XAML changes elsewhere.
`TelemetryReader`'s `RelativeRow`/`StandingsRow` records grow new fields (flag, license, iRating,
class id); a new `PlayerCarStatus` event carries the footer data (brake bias, track rubber state,
player's own best lap); a new `SecondaryRelativeUpdated` event + `SetSecondaryClassFilter` method
mirror the existing `SetTrackLength` pattern to drive a second, auto-configuring Relative window.
A new `IracingLiveCoach.Core.CountryFlags` helper (testable, no SDK dependency) maps a driver's
`FlairName` to a flag emoji.

**Tech Stack:** WPF (.NET 8, unchanged), IRSDKSharper 1.3.0 (unchanged — this plan's own telemetry
claims were verified directly against this exact installed version, not just documentation).

**Spec:** `docs/superpowers/specs/2026-09-13-overlay-suite-design.md`'s "Visual redesign — broadcast
UI overhaul (Phase 5, 14/09/2026)" section — read it in full before starting; it documents exactly
which mockup elements are real telemetry/session data and which are honest substitutes, and why.

## Global Constraints

- Every telemetry/session field this plan uses was independently confirmed against the actual
  `IRSDKSharper.dll` (1.3.0) this project already references, or a targeted, dated web search when
  reflection wasn't possible (per-tick telemetry channel names are runtime strings, not compiled
  fields): `DriverModel.FlairID`/`FlairName`, `LicColor`/`LicString`/`LicLevel` (all confirmed
  present on the real type), `dcBrakeBias` (falling back to `dcPeakBrakeBias` for cars that publish
  that name instead), `SessionInfoModel.SessionModel.SessionTrackRubberState`, `CarIdxClass`,
  `CarIdxClassPosition`. Do not introduce any other telemetry variable without the same level of
  confirmation.
- The OT/P2P column shows a REAL countdown, corrected after the driver confirmed this is a feature
  they use today: iRacing's SDK exposes no activation-/recharge-duration CONSTANT, but the SF23's
  own Overtake System rules are publicly documented (20s active window, 100s cooldown, 200s total
  budget per race — from iRacing's own car page). This plan hardcodes those two constants
  (`OtsActiveSeconds`/`OtsCooldownSeconds`) the same way the sibling `iracing-analytics` project
  already hardcodes per-car setup domain knowledge, and combines them with the REAL
  `CarIdxP2P_Status` transition (and `CarIdxP2P_Count` for uses remaining) to compute an actual
  ATIVO/RECARGA/PRONTO countdown — not a fabricated number, but also disclosed (in code comments,
  never a UI caption per the driver's own "no explanatory legends" request) as SF23-sourced and
  potentially inaccurate for a different P2P-enabled car this app hasn't researched.
- National flags render as Unicode regional-indicator emoji via a bounded country-name lookup
  table (`IracingLiveCoach.Core.CountryFlags`) with an explicit fallback for unrecognized names —
  never silently show nothing or crash on an unmapped `FlairName`.
- `LicColor` is a `String` (confirmed via reflection — NOT the packed-int this session originally
  assumed), parsed defensively (try/catch, same posture as every other telemetry read in this
  file) with a neutral gray fallback on parse failure.
- A manufacturer BADGE (text, e.g. "FERRARI") is shown, derived from `CarScreenName`'s own
  "<Manufacturer> <Model>" naming convention — corrected after the driver confirmed every
  competing overlay shows this; real logo IMAGES are still not implemented, since the SDK ships no
  logo asset and bundling real trademarked manufacturer logos is a separate, larger effort with
  its own asset-sourcing/legal considerations this plan does not take on.
- ΔiR (projected iRating change) is a disclosed *estimate* (SoF-based projection over the visible
  `IRating` spread), marked with a small `*`, per the spec — no explanatory caption/legend text
  anywhere in these widgets (the driver explicitly asked those removed).
- The second Relative instance (`relative2`) auto-detects the first car class present in the
  session that differs from the player's own class, and simply doesn't show any rows (not a
  crash, not a fake "sem dados") when the session is single-class — this plan does NOT build a
  manual class-picker UI (out of scope, see the spec's own "Out of scope for this pass").
- Every new/changed file is verified by `dotnet build` (0 errors, 0 warnings). `CountryFlags`
  (Core, no SDK dependency) gets real unit tests, following this session's own established lesson
  (Phase 2's fuel-formatting bug, Phase 3's radar-position bug) that a pure function belongs in
  `Core` with real tests, not inside a WPF class "because everything else here is untestable."
  Everything else keeps the established SDK-dependency test boundary.

---

### Task 1: `F1Theme.xaml` palette update + `IracingLiveCoach.Core.CountryFlags`

**Files:**
- Modify: `src/IracingLiveCoach.App/F1Theme.xaml`
- Create: `src/IracingLiveCoach.Core/CountryFlags.cs`
- Test: `tests/IracingLiveCoach.Core.Tests/CountryFlagsTests.cs`

**Interfaces:**
- Produces: `public static class CountryFlags { public static string ToEmoji(string? countryName); }`
  — Task 2's `TelemetryReader` calls `CountryFlags.ToEmoji(driver.FlairName)` when building each
  `RelativeRow`/`StandingsRow`.
- Modified brush VALUES only (same keys `F1BackgroundBrush`/`F1AccentBrush`/etc.) — no consumer of
  `F1Theme.xaml` needs any change for this task.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/IracingLiveCoach.Core.Tests/CountryFlagsTests.cs
using IracingLiveCoach.Core;
using Xunit;

namespace IracingLiveCoach.Core.Tests;

public class CountryFlagsTests
{
    [Theory]
    [InlineData("Brazil", "🇧🇷")]
    [InlineData("United States", "🇺🇸")]
    [InlineData("United Kingdom", "🇬🇧")]
    [InlineData("Germany", "🇩🇪")]
    [InlineData("Portugal", "🇵🇹")]
    public void ToEmoji_maps_known_country_names(string countryName, string expectedEmoji)
    {
        Assert.Equal(expectedEmoji, CountryFlags.ToEmoji(countryName));
    }

    [Fact]
    public void ToEmoji_returns_a_globe_fallback_for_an_unrecognized_name()
    {
        Assert.Equal("🌐", CountryFlags.ToEmoji("Some Made-Up Place"));
    }

    [Fact]
    public void ToEmoji_returns_the_fallback_for_null_or_empty()
    {
        Assert.Equal("🌐", CountryFlags.ToEmoji(null));
        Assert.Equal("🌐", CountryFlags.ToEmoji(""));
    }

    [Fact]
    public void ToEmoji_is_case_insensitive()
    {
        Assert.Equal("🇧🇷", CountryFlags.ToEmoji("brazil"));
        Assert.Equal("🇧🇷", CountryFlags.ToEmoji("BRAZIL"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter CountryFlagsTests`
Expected: FAIL — `CountryFlags` doesn't exist yet.

- [ ] **Step 3: Write the implementation**

```csharp
// src/IracingLiveCoach.Core/CountryFlags.cs
using System.Collections.Generic;

namespace IracingLiveCoach.Core;

/// <summary>Maps a driver's FlairName (the country display name iRacing's 2025 Season 3 "flair"
/// feature publishes on DriverInfo.Drivers[], e.g. "Brazil") to a Unicode regional-indicator flag
/// emoji, rendered by the OS's own emoji font (Segoe UI Emoji on Windows) -- no bundled flag image
/// assets needed. Coverage is deliberately bounded to countries iRacing's own driver base
/// realistically spans, with an explicit globe fallback for anything not in the table -- never a
/// blank/missing glyph and never a guessed flag for an unrecognized name.</summary>
public static class CountryFlags
{
    private const string Fallback = "🌐";

    // Regional-indicator flag emoji are built from two Unicode "regional indicator symbol"
    // characters per ISO-3166-alpha-2 code (e.g. "BR" -> 🇧 🇷). This table lists the alpha-2 code
    // directly; ToEmoji() converts the code to the two-codepoint emoji at lookup time so this
    // table stays readable as plain country-name -> code pairs.
    private static readonly Dictionary<string, string> CodesByCountryName = new(System.StringComparer.OrdinalIgnoreCase)
    {
        ["Brazil"] = "BR",
        ["United States"] = "US",
        ["United Kingdom"] = "GB",
        ["Canada"] = "CA",
        ["Germany"] = "DE",
        ["France"] = "FR",
        ["Italy"] = "IT",
        ["Spain"] = "ES",
        ["Portugal"] = "PT",
        ["Netherlands"] = "NL",
        ["Belgium"] = "BE",
        ["Australia"] = "AU",
        ["New Zealand"] = "NZ",
        ["Japan"] = "JP",
        ["Mexico"] = "MX",
        ["Argentina"] = "AR",
        ["Chile"] = "CL",
        ["Colombia"] = "CO",
        ["South Africa"] = "ZA",
        ["Sweden"] = "SE",
        ["Norway"] = "NO",
        ["Denmark"] = "DK",
        ["Finland"] = "FI",
        ["Poland"] = "PL",
        ["Austria"] = "AT",
        ["Switzerland"] = "CH",
        ["Ireland"] = "IE",
        ["Czech Republic"] = "CZ",
        ["Hungary"] = "HU",
        ["Greece"] = "GR",
        ["Turkey"] = "TR",
        ["Russia"] = "RU",
        ["India"] = "IN",
        ["China"] = "CN",
        ["South Korea"] = "KR",
        ["Indonesia"] = "ID",
        ["Malaysia"] = "MY",
        ["Singapore"] = "SG",
        ["Thailand"] = "TH",
        ["Philippines"] = "PH",
        ["United Arab Emirates"] = "AE",
        ["Saudi Arabia"] = "SA",
        ["Israel"] = "IL",
        ["Estonia"] = "EE",
        ["Latvia"] = "LV",
        ["Lithuania"] = "LT",
        ["Romania"] = "RO",
        ["Bulgaria"] = "BG",
        ["Croatia"] = "HR",
        ["Slovakia"] = "SK",
        ["Slovenia"] = "SI",
        ["Ukraine"] = "UA",
    };

    public static string ToEmoji(string? countryName)
    {
        if (string.IsNullOrWhiteSpace(countryName)) return Fallback;
        if (!CodesByCountryName.TryGetValue(countryName, out var code)) return Fallback;

        // Each ASCII letter A-Z maps to a Unicode "Regional Indicator Symbol Letter" by offsetting
        // from U+1F1E6 ('A'); the emoji is the two-codepoint pair for the country's alpha-2 code.
        const int RegionalIndicatorBase = 0x1F1E6;
        var first = char.ConvertFromUtf32(RegionalIndicatorBase + (code[0] - 'A'));
        var second = char.ConvertFromUtf32(RegionalIndicatorBase + (code[1] - 'A'));
        return first + second;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter CountryFlagsTests`
Expected: PASS (7 passed)

- [ ] **Step 5: Update `F1Theme.xaml`'s palette**

Read the current file first. Replace the brush color VALUES only — every `x:Key` stays exactly as
it is, so no other file needs a change:

```xml
<SolidColorBrush x:Key="F1BackgroundBrush" Color="#F00B1420" />
<SolidColorBrush x:Key="F1AccentBrush" Color="#FFE2483D" />
<SolidColorBrush x:Key="F1TextBrush" Color="#FFF2F4F7" />
<SolidColorBrush x:Key="F1MutedTextBrush" Color="#FF9AA3AF" />
<SolidColorBrush x:Key="F1PositionGainBrush" Color="#FF2DE2B2" />
<SolidColorBrush x:Key="F1PositionLossBrush" Color="#FFE2483D" />
<SolidColorBrush x:Key="F1BorderIdleBrush" Color="#33E2483D" />
<SolidColorBrush x:Key="F1BorderActiveBrush" Color="#FFE2483D" />
```

Add one new brush, alongside the existing ones (used by Task 3 for the player's own highlighted
row, replacing the old flat accent-tint fill every widget's own `.player` row style currently
uses):

```xml
<SolidColorBrush x:Key="F1HighlightBrush" Color="#FF39D8E0" />
```

Leave `F1HeaderFont`, `F1MonoFont`, `F1PanelBorder`, and `F1RowAccentBar` exactly as they are — no
changes to fonts or layout styles in this task, only color values.

- [ ] **Step 6: Verify the build**

Run: `dotnet build` — expect `Build succeeded.`, 0 errors, 0 warnings. Every existing widget will
visually pick up the new navy/orange/cyan palette automatically; this is expected and correct —
confirming it visually is a manual smoke-test step for the driver, not something `dotnet build`
can verify.

- [ ] **Step 7: Commit**

```bash
git add src/IracingLiveCoach.Core/CountryFlags.cs src/IracingLiveCoach.App/F1Theme.xaml tests/IracingLiveCoach.Core.Tests/CountryFlagsTests.cs
git commit -m "feat: broadcast-UI navy/orange/cyan palette + CountryFlags helper"
```

---

### Task 2: `TelemetryReader` — richer rows, `PlayerCarStatus`, and the second-class relative feed

**Files:**
- Modify: `src/IracingLiveCoach.App/TelemetryReader.cs`

**Interfaces:**
- Consumes: `CountryFlags.ToEmoji` (Task 1).
- Produces: extended `RelativeRow`/`StandingsRow` (new fields, same name, additive — see below),
  `public record PlayerCarStatus(double? BrakeBiasPct, string? TrackRubberState, double?
  BestLapTimeSeconds)`, `public event Action<PlayerCarStatus>? PlayerCarStatusUpdated;`, `public
  void SetSecondaryClassFilter` is NOT needed — the class filter is auto-detected internally (see
  Global Constraints) — instead: `public event Action<List<RelativeRow>>?
  SecondaryRelativeUpdated;` fires for the first distinct `CarClassId` found that differs from the
  player's own. Task 3/4 consume the extended `RelativeRow`/`StandingsRow` fields; a later task
  registers a second `RelativeWidget` instance consuming `SecondaryRelativeUpdated`.

- [ ] **Step 1: Extend `RelativeRow` and `StandingsRow`, add the new records**

Change:
```csharp
public record RelativeRow(int PositionOffset, string DriverCode, double? GapSeconds, int? TireCompound, bool? P2PActive);
```
to:
```csharp
/// <summary>P2PUsesRemaining/P2PSecondsRemaining/P2PInCooldown are only meaningful when P2PActive
/// is non-null (the session publishes P2P at all). P2PSecondsRemaining/P2PInCooldown are computed
/// from the SF23's OWN PUBLICLY DOCUMENTED Overtake System rules (20s active window, 100s
/// cooldown -- see UpdateFullRelative's own doc comment for the source and the disclosed
/// car-specific caveat) combined with the REAL live CarIdxP2P_Status transition, giving an actual
/// countdown rather than a vague elapsed-time approximation -- corrected 14/09/2026 after the
/// driver confirmed this is a real feature they use today.</summary>
public record RelativeRow(int PositionOffset, string DriverCode, double? GapSeconds, int? TireCompound, bool? P2PActive, int? P2PUsesRemaining, double? P2PSecondsRemaining, bool P2PInCooldown, string FlagEmoji, string LicString, string? LicColorHex, int IRating, int CarClassId, string ManufacturerBadge);
```

Change:
```csharp
public record StandingsRow(int Position, string DriverCode, int LapsCompleted, double? LastLapTime, int? TireCompound, bool IsPlayer);
```
to:
```csharp
public record StandingsRow(int Position, string DriverCode, int LapsCompleted, double? LastLapTime, int? TireCompound, bool IsPlayer, string FlagEmoji, string LicString, string? LicColorHex, int IRating, int CarClassId, string ManufacturerBadge);
```

Add the two new records, alongside the existing ones:
```csharp
/// <summary>The player's own current car status for the Relative/Standings widgets' footer.
/// BrakeBiasPct/TrackRubberState are null if the current car/session doesn't publish that channel
/// (see UpdatePlayerCarStatus's own try/catch per field). BestLapTimeSeconds is the minimum
/// LapLastLapTime observed so far this session -- null until the player has completed one lap.</summary>
public record PlayerCarStatus(double? BrakeBiasPct, string? TrackRubberState, double? BestLapTimeSeconds);
```

Add the new events, alongside the existing ones:
```csharp
    /// <summary>Fires every telemetry tick once the session is detected, with the player's own
    /// current brake bias / track rubber state / best lap for the widgets' own footer row.</summary>
    public event Action<PlayerCarStatus>? PlayerCarStatusUpdated;

    /// <summary>Mirrors FullRelativeUpdated's own shape and cadence, but scoped to the first car
    /// class found in the session that differs from the player's own (CarIdxClass), ranked by
    /// CarIdxClassPosition rather than overall CarIdxPosition -- feeds a second, independently
    /// positioned Relative widget instance for multiclass sessions. Simply never fires (not fires
    /// with an empty list) when the session is single-class -- same "don't flicker a meaningless
    /// empty state" posture RelativeUpdated already has for non-P2P sessions.</summary>
    public event Action<List<RelativeRow>>? SecondaryRelativeUpdated;
```

- [ ] **Step 2: Add per-car P2P phase-tracking fields and a best-lap tracker**

Alongside the existing fuel/weather/radar fields:
```csharp
    // 14/09/2026: SF23's Overtake System rules, publicly documented on iRacing's own car page --
    // 20s of activation per use, at least 100s cooldown ("ReTime") afterward. This is car-specific
    // domain knowledge (the same kind the sibling iracing-analytics project already hardcodes for
    // ARB/differential/spring targets per car architecture), not a telemetry read -- combined with
    // the REAL CarIdxP2P_Status transition below to compute an actual countdown. Disclosed caveat:
    // these constants are SF23-specific and could be wrong for a different P2P-enabled car this
    // app hasn't researched, or if iRacing rebalances SF23's own system in a future season -- the
    // underlying CarIdxP2P_Status/CarIdxP2P_Count are always real regardless of whether the
    // countdown numbers happen to be exactly right for the car actually being driven.
    private const double OtsActiveSeconds = 20.0;
    private const double OtsCooldownSeconds = 100.0;

    // Keyed by CarIdx so each car's own activation/cooldown phase is timed independently.
    // _p2pPhaseEndUtcByCarIdx holds the UTC instant the CURRENT phase (active or cooldown) ends.
    private readonly Dictionary<int, bool> _lastP2PActiveByCarIdx = new();
    private readonly Dictionary<int, DateTime> _p2pPhaseEndUtcByCarIdx = new();

    private double? _bestLapTimeSeconds;
```

- [ ] **Step 3: Add a helper for the driver-identity fields shared by both row types**

Add this private method, near `BuildDriverCodes`:
```csharp
    private (string FlagEmoji, string LicString, string? LicColorHex, int IRating, int CarClassId, string ManufacturerBadge) GetIdentity(int carIdx)
    {
        var sessionInfo = _sdk.Data.SessionInfo;
        var driver = sessionInfo?.DriverInfo?.Drivers?.FirstOrDefault(d => d.CarIdx == carIdx);
        var flagEmoji = CountryFlags.ToEmoji(driver?.FlairName);
        var licString = driver?.LicString ?? "--";
        var licColorHex = driver?.LicColor; // "String" per the real IRSDKSharper 1.3.0 type -- parsed defensively by the view model, not here.
        var iRating = driver?.IRating ?? 0;
        var carClassId = driver?.CarClassID ?? 0;
        var manufacturerBadge = ExtractManufacturer(driver?.CarScreenName);
        return (flagEmoji, licString, licColorHex, iRating, carClassId, manufacturerBadge);
    }

    // 14/09/2026: "team logo" -- iRacing has no real "team" concept for pickup/public racing and
    // no logo asset for anything via the SDK, but CarScreenName (confirmed real, e.g. "Ferrari 296
    // GT3 EVO") DOES let us derive the car's own manufacturer, which is what every competing
    // overlay's "team badge" actually shows in practice. iRacing's own car names consistently
    // follow a "<Manufacturer> <Model>" convention, so the first whitespace-delimited token is the
    // manufacturer in the overwhelming majority of cases -- a plain text badge (e.g. "FERRARI"),
    // not an image logo (bundling real trademarked logo graphics is a materially larger, separate
    // effort with its own legal/asset-sourcing considerations -- see this plan's own spec section).
    private static string ExtractManufacturer(string? carScreenName)
    {
        if (string.IsNullOrWhiteSpace(carScreenName)) return "";
        var firstToken = carScreenName.Split(' ', 2)[0];
        return firstToken.ToUpperInvariant();
    }
```

(This re-reads `_sdk.Data.SessionInfo` per call rather than caching, matching this file's own
existing convention in `OnSessionInfo` -- driver identity fields don't change mid-session, but this
keeps the read pattern consistent and simple; if this shows up as a real per-tick cost concern in a
future review, caching alongside `_driverCodesByCarIdx` is the natural fix, not attempted
preemptively here.)

- [ ] **Step 4: Update `UpdateFullRelative` to populate the new `RelativeRow` fields**

Read the current method. Add this private helper, right above `UpdateFullRelative` (the countdown
math is shared with `UpdateSecondaryRelative` in Step 7, so it's factored out once):
```csharp
    // Returns (SecondsRemaining, InCooldown) for the given car's P2P phase, given its current
    // active/inactive telemetry state -- see OtsActiveSeconds/OtsCooldownSeconds's own doc comment
    // for the SF23-sourced constants this is built from.
    private (double? SecondsRemaining, bool InCooldown) UpdateP2PPhase(int idx, bool active)
    {
        var wasActive = _lastP2PActiveByCarIdx.TryGetValue(idx, out var previouslyActive) && previouslyActive;
        if (active && !wasActive)
        {
            // Activation just started -- the active window ends OtsActiveSeconds from now.
            _p2pPhaseEndUtcByCarIdx[idx] = DateTime.UtcNow.AddSeconds(OtsActiveSeconds);
        }
        else if (!active && wasActive)
        {
            // Deactivation just happened (driver released it early, or it auto-expired) -- the
            // cooldown window starts now and ends OtsCooldownSeconds later.
            _p2pPhaseEndUtcByCarIdx[idx] = DateTime.UtcNow.AddSeconds(OtsCooldownSeconds);
        }
        _lastP2PActiveByCarIdx[idx] = active;

        if (!_p2pPhaseEndUtcByCarIdx.TryGetValue(idx, out var phaseEnd)) return (null, false);
        var remaining = (phaseEnd - DateTime.UtcNow).TotalSeconds;
        if (remaining <= 0) return (null, false); // phase already elapsed -- PRONTO, no countdown to show
        return (remaining, !active); // still counting down: if not currently active, this is the cooldown countdown
    }
```

Inside the loop, right after the existing `p2p` read, add:
```csharp
                int? p2pUses = null;
                double? p2pSecondsRemaining = null;
                var p2pInCooldown = false;
                if (p2p is bool active)
                {
                    p2pUses = _sdk.Data.GetInt("CarIdxP2P_Count", idx);
                    (p2pSecondsRemaining, p2pInCooldown) = UpdateP2PPhase(idx, active);
                }

                var identity = GetIdentity(idx);
```
Change the final `rows.Add(...)` line to:
```csharp
                rows.Add(new RelativeRow(offset, code, gap, tireCompound >= 0 ? tireCompound : null, p2p, p2pUses, p2pSecondsRemaining, p2pInCooldown, identity.FlagEmoji, identity.LicString, identity.LicColorHex, identity.IRating, identity.CarClassId, identity.ManufacturerBadge));
```

- [ ] **Step 5: Update `UpdateStandings` to populate the new `StandingsRow` fields**

Inside the loop, right after `var code = ...`, add:
```csharp
                var identity = GetIdentity(idx);
```
Change the `rows.Add(...)` line to:
```csharp
                rows.Add(new StandingsRow(position, code, lapsCompleted, lastLap > 0 ? lastLap : null, tireCompound >= 0 ? tireCompound : null, idx == _playerCarIdx, identity.FlagEmoji, identity.LicString, identity.LicColorHex, identity.IRating, identity.CarClassId, identity.ManufacturerBadge));
```

- [ ] **Step 6: Add `UpdatePlayerCarStatus` and wire it into `OnTelemetryData`**

Add this private method, after the existing `UpdateStandings`:
```csharp
    private void UpdatePlayerCarStatus()
    {
        try
        {
            double? brakeBias = null;
            try { brakeBias = _sdk.Data.GetFloat("dcBrakeBias"); }
            catch { try { brakeBias = _sdk.Data.GetFloat("dcPeakBrakeBias"); } catch { /* not published this car */ } }

            string? rubberState = null;
            try
            {
                var sessionInfo = _sdk.Data.SessionInfo;
                var currentSessionNum = sessionInfo?.SessionInfo?.CurrentSessionNum ?? -1;
                rubberState = sessionInfo?.SessionInfo?.Sessions?.FirstOrDefault(s => s.SessionNum == currentSessionNum)?.SessionTrackRubberState;
            }
            catch { /* session info momentarily incomplete -- skip this tick's rubber read */ }

            var lastLap = _sdk.Data.GetFloat("LapLastLapTime");
            if (lastLap > 0 && (_bestLapTimeSeconds is not double best || lastLap < best))
                _bestLapTimeSeconds = lastLap;

            PlayerCarStatusUpdated?.Invoke(new PlayerCarStatus(brakeBias, rubberState, _bestLapTimeSeconds));
        }
        catch
        {
            // Skip this tick.
        }
    }
```
In `OnTelemetryData`, add `UpdatePlayerCarStatus();` alongside the existing unthrottled calls
(after `UpdateTireWear();`).

- [ ] **Step 7: Add `UpdateSecondaryRelative` and wire it into `OnTelemetryData`**

Add this private method, after `UpdateFullRelative`:
```csharp
    // 14/09/2026: mirrors UpdateFullRelative's shape but ranks by CarIdxClassPosition within the
    // first car class found that differs from the player's own CarIdxClass, for the second,
    // auto-configuring Relative widget instance (multiclass sessions only -- see this plan's own
    // Global Constraints for why there's no manual class picker).
    private void UpdateSecondaryRelative()
    {
        try
        {
            var myClass = _sdk.Data.GetInt("CarIdxClass", _playerCarIdx);
            var maxCars = IRacingSdkConst.MaxNumCars;

            int? secondaryClass = null;
            for (var idx = 0; idx < maxCars; idx++)
            {
                if (idx == _playerCarIdx) continue;
                var classId = _sdk.Data.GetInt("CarIdxClass", idx);
                var classPosition = _sdk.Data.GetInt("CarIdxClassPosition", idx);
                if (classPosition <= 0 || classId == myClass) continue;
                secondaryClass = classId;
                break; // first differing class found -- deterministic since CarIdx order is stable within a session
            }
            if (secondaryClass is not int targetClass) return; // single-class session -- don't fire

            var byOffset = new List<(int Position, int Idx)>();
            for (var idx = 0; idx < maxCars; idx++)
            {
                if (_sdk.Data.GetInt("CarIdxClass", idx) != targetClass) continue;
                var classPosition = _sdk.Data.GetInt("CarIdxClassPosition", idx);
                if (classPosition <= 0) continue;
                byOffset.Add((classPosition, idx));
            }
            if (byOffset.Count == 0) return;

            // No player row in this class -- center the window on the class's own leader rather
            // than an offset from the (absent) player position; show the top RelativeCarsBehind*2+1
            // class-classified cars, matching the mockup's own "AO REDOR DE VOCÊ" framing loosely
            // adapted to "top of this class" since the player isn't racing in it.
            var rows = new List<RelativeRow>();
            var ordered = byOffset.OrderBy(pair => pair.Position).Take(RelativeCarsBehind * 2 + 1).ToList();
            foreach (var (position, idx) in ordered)
            {
                var tireCompound = _sdk.Data.GetInt("CarIdxTireCompound", idx);
                bool? p2p = null;
                int? p2pUses = null;
                double? p2pSecondsRemaining = null;
                var p2pInCooldown = false;
                try
                {
                    var active = _sdk.Data.GetBool("CarIdxP2P_Status", idx);
                    p2p = active;
                    p2pUses = _sdk.Data.GetInt("CarIdxP2P_Count", idx);
                    (p2pSecondsRemaining, p2pInCooldown) = UpdateP2PPhase(idx, active);
                }
                catch { /* no P2P for this class */ }

                var code = _driverCodesByCarIdx.TryGetValue(idx, out var driverCode) ? driverCode : "?";
                var identity = GetIdentity(idx);
                rows.Add(new RelativeRow(position, code, null, tireCompound >= 0 ? tireCompound : null, p2p, p2pUses, p2pSecondsRemaining, p2pInCooldown, identity.FlagEmoji, identity.LicString, identity.LicColorHex, identity.IRating, identity.CarClassId, identity.ManufacturerBadge));
            }

            SecondaryRelativeUpdated?.Invoke(rows);
        }
        catch
        {
            // Skip this tick.
        }
    }
```
In `OnTelemetryData`, add `UpdateSecondaryRelative();` alongside the existing unthrottled calls.

(Note: `RelativeRow.PositionOffset` is reused here to carry the class-scoped position number itself,
not an offset from the player, since there is no player row in this class — Task 3's view model
formats it accordingly; this is documented in Task 3's own brief, not assumed silently.)

- [ ] **Step 8: Verify the build and tests**

Run: `dotnet build` — expect `Build succeeded.`, 0 errors, 0 warnings.
Run: `dotnet test` — expect the existing 26 tests plus the new `CountryFlagsTests` (7) = 34 passing.

- [ ] **Step 9: Commit**

```bash
git add src/IracingLiveCoach.App/TelemetryReader.cs
git commit -m "feat: richer RelativeRow/StandingsRow (flag/license/iRating/class), PlayerCarStatus, secondary class-scoped relative feed"
```

---

### Task 3: Restyle `RelativeWidget` to the broadcast layout

**Files:**
- Modify: `src/IracingLiveCoach.App/RelativeWidget.xaml`
- Modify: `src/IracingLiveCoach.App/RelativeWidgetViewModel.cs`
- Modify: `src/IracingLiveCoach.App/RelativeWidget.xaml.cs`

**Interfaces:**
- Consumes: extended `RelativeRow` (Task 2), `PlayerCarStatus`/`PlayerCarStatusUpdated` (Task 2),
  `F1HighlightBrush` (Task 1).
- Produces: `RelativeWidget` gains `public void UpdatePlayerStatus(PlayerCarStatus status)`
  alongside its existing `UpdateRows`; a new constructor overload `RelativeWidget(WidgetLayout
  layout, Action onChanged, string title)` so Task 5 can create a second instance with its own
  header text ("RELATIVE — CLASSE 2" or similar) without duplicating the whole class.

- [ ] **Step 1: Rewrite `RelativeWidgetViewModel.cs`**

```csharp
// src/IracingLiveCoach.App/RelativeWidgetViewModel.cs
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace IracingLiveCoach.App;

public class FullRelativeRowViewModel
{
    public string PositionText { get; }
    public string FlagAndCode { get; }
    public string ManufacturerText { get; }
    public string LicText { get; }
    public Brush LicBrush { get; }
    public string IRatingText { get; }
    public string GapText { get; }
    public string P2PText { get; }
    public Brush P2PBrush { get; }
    public bool IsPlayerRow { get; }

    private static readonly Brush P2PActiveBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0x48, 0x3D));
    private static readonly Brush P2PCooldownBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xA5, 0x2C));
    private static readonly Brush P2PIdleBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0xA3, 0xAF));
    private static readonly Brush LicFallbackBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0xA3, 0xAF));

    public FullRelativeRowViewModel(RelativeRow row, bool showClassPositionAsAbsolute)
    {
        IsPlayerRow = row.PositionOffset == 0;
        PositionText = showClassPositionAsAbsolute
            ? row.PositionOffset.ToString(CultureInfo.InvariantCulture)
            : (row.PositionOffset > 0 ? "+" : "") + row.PositionOffset.ToString(CultureInfo.InvariantCulture);
        // Manufacturer badge folded inline (no separate column) to keep this widget's column count
        // matching the mockup's own Relative layout -- Standings (Task 4) gives it its own column
        // instead, since that mockup panel shows the badge more prominently.
        FlagAndCode = string.IsNullOrEmpty(row.ManufacturerBadge)
            ? $"{row.FlagEmoji} {row.DriverCode}"
            : $"{row.FlagEmoji} {row.DriverCode} · {row.ManufacturerBadge}";
        ManufacturerText = row.ManufacturerBadge;
        LicText = row.LicString;
        LicBrush = ParseLicColor(row.LicColorHex);
        IRatingText = row.IRating > 0 ? row.IRating.ToString("N0", CultureInfo.InvariantCulture) : "--";
        GapText = row.GapSeconds is double gap ? $"{(gap >= 0 ? "+" : "")}{gap.ToString("0.0", CultureInfo.InvariantCulture)}" : "--";

        // P2PSecondsRemaining/P2PInCooldown are derived from the SF23's own documented Overtake
        // System rules (20s active/100s cooldown) applied to the real CarIdxP2P_Status transition
        // -- see TelemetryReader.UpdateP2PPhase's own doc comment for the source and caveat.
        if (row.P2PActive is bool active)
        {
            var uses = row.P2PUsesRemaining is int u ? u.ToString(CultureInfo.InvariantCulture) : "?";
            if (active && row.P2PSecondsRemaining is double activeRemaining)
            {
                P2PText = $"ATIVO {activeRemaining.ToString("0", CultureInfo.InvariantCulture)}s ({uses})";
                P2PBrush = P2PActiveBrush;
            }
            else if (row.P2PInCooldown && row.P2PSecondsRemaining is double cooldownRemaining)
            {
                P2PText = $"RECARGA {cooldownRemaining.ToString("0", CultureInfo.InvariantCulture)}s ({uses})";
                P2PBrush = P2PCooldownBrush;
            }
            else
            {
                P2PText = $"PRONTO ({uses})";
                P2PBrush = P2PIdleBrush;
            }
        }
        else
        {
            P2PText = "";
            P2PBrush = P2PIdleBrush;
        }
    }

    // LicColor is a String on the real IRSDKSharper 1.3.0 DriverModel type (confirmed via
    // reflection -- corrected from this session's own earlier packed-int assumption). iRacing's
    // own YAML color-string convention is "0xRRGGBB" or "#RRGGBB"; parsed defensively since this
    // field's exact format is not independently documented beyond its type.
    private static Brush ParseLicColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return LicFallbackBrush;
        try
        {
            var cleaned = hex.Trim();
            if (cleaned.StartsWith("0x", System.StringComparison.OrdinalIgnoreCase)) cleaned = "#" + cleaned[2..];
            if (!cleaned.StartsWith("#")) cleaned = "#" + cleaned;
            var color = (Color)ColorConverter.ConvertFromString(cleaned)!;
            return new SolidColorBrush(color);
        }
        catch
        {
            return LicFallbackBrush;
        }
    }
}

public class RelativeWidgetViewModel : INotifyPropertyChanged
{
    private string _brakeBiasText = "--";
    private string _trackRubberText = "--";
    private string _bestLapText = "--";
    private string _lastLapText = "--";

    public string BrakeBiasText { get => _brakeBiasText; private set => Set(ref _brakeBiasText, value); }
    public string TrackRubberText { get => _trackRubberText; private set => Set(ref _trackRubberText, value); }
    public string BestLapText { get => _bestLapText; private set => Set(ref _bestLapText, value); }
    public string LastLapText { get => _lastLapText; private set => Set(ref _lastLapText, value); }

    public ObservableCollection<FullRelativeRowViewModel> Rows { get; } = new();

    // showClassPositionAsAbsolute=true for the secondary (class-scoped) instance, whose
    // PositionOffset carries an absolute class position, not an offset from the player.
    public void SetRows(System.Collections.Generic.List<RelativeRow> rows, bool showClassPositionAsAbsolute = false)
    {
        Rows.Clear();
        foreach (var row in rows) Rows.Add(new FullRelativeRowViewModel(row, showClassPositionAsAbsolute));
    }

    public void ApplyPlayerStatus(PlayerCarStatus status)
    {
        BrakeBiasText = status.BrakeBiasPct is double bias ? bias.ToString("0.0", CultureInfo.InvariantCulture) + "%" : "--";
        TrackRubberText = status.TrackRubberState ?? "--";
        if (status.BestLapTimeSeconds is double best) BestLapText = FormatLapTime(best);
    }

    // Called every tick from RelativeWidget's own last-lap subscription path (see Task 3's
    // RelativeWidget.xaml.cs) with the player's own most recent RelativeRow (PositionOffset==0 is
    // never present in the ahead/behind window, so this reads the player's own lap time from the
    // same FullRelativeUpdated tick indirectly via MainWindow -- see that file's own wiring).
    public void SetLastLapSeconds(double? seconds) => LastLapText = seconds is double s ? FormatLapTime(s) : "--";

    private static string FormatLapTime(double seconds)
    {
        var minutes = (int)(seconds / 60);
        var remainder = seconds - minutes * 60;
        return $"{minutes}:{remainder.ToString("00.000", CultureInfo.InvariantCulture)}";
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

- [ ] **Step 2: Rewrite `RelativeWidget.xaml`**

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
        MinWidth="320" MinHeight="260"
        SizeToContent="Manual">
    <Grid Background="Transparent" MouseLeftButtonDown="OnBackgroundMouseLeftButtonDown">
        <Border x:Name="OuterBorder" Style="{StaticResource F1PanelBorder}">
            <StackPanel Margin="10">
                <TextBlock x:Name="HeaderText" Text="RELATIVE" FontFamily="{StaticResource F1HeaderFont}" FontSize="13"
                           FontWeight="Bold" Foreground="{StaticResource F1TextBrush}" Margin="0,0,0,6" />

                <Grid Margin="0,0,0,2">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="28" />
                        <ColumnDefinition Width="70" />
                        <ColumnDefinition Width="48" />
                        <ColumnDefinition Width="48" />
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="70" />
                    </Grid.ColumnDefinitions>
                    <TextBlock Grid.Column="0" Text="POS" FontFamily="{StaticResource F1MonoFont}" FontSize="9" Foreground="{StaticResource F1MutedTextBrush}" />
                    <TextBlock Grid.Column="1" Text="PILOTO" FontFamily="{StaticResource F1MonoFont}" FontSize="9" Foreground="{StaticResource F1MutedTextBrush}" />
                    <TextBlock Grid.Column="2" Text="LIC" FontFamily="{StaticResource F1MonoFont}" FontSize="9" Foreground="{StaticResource F1MutedTextBrush}" />
                    <TextBlock Grid.Column="3" Text="iR" FontFamily="{StaticResource F1MonoFont}" FontSize="9" Foreground="{StaticResource F1MutedTextBrush}" />
                    <TextBlock Grid.Column="4" Text="DELTA" FontFamily="{StaticResource F1MonoFont}" FontSize="9" Foreground="{StaticResource F1MutedTextBrush}" />
                    <TextBlock Grid.Column="5" Text="OT" FontFamily="{StaticResource F1MonoFont}" FontSize="9" Foreground="{StaticResource F1MutedTextBrush}" />
                </Grid>

                <ItemsControl ItemsSource="{Binding Rows}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Border Padding="0,3">
                                <Border.Style>
                                    <Style TargetType="Border">
                                        <Style.Triggers>
                                            <DataTrigger Binding="{Binding IsPlayerRow}" Value="True">
                                                <Setter Property="Background" Value="{StaticResource F1HighlightBrush}" />
                                            </DataTrigger>
                                        </Style.Triggers>
                                    </Style>
                                </Border.Style>
                                <Grid>
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="28" />
                                        <ColumnDefinition Width="70" />
                                        <ColumnDefinition Width="48" />
                                        <ColumnDefinition Width="48" />
                                        <ColumnDefinition Width="*" />
                                        <ColumnDefinition Width="70" />
                                    </Grid.ColumnDefinitions>
                                    <TextBlock Grid.Column="0" Text="{Binding PositionText}" FontFamily="{StaticResource F1MonoFont}" FontSize="12" FontWeight="Bold" Foreground="{StaticResource F1TextBrush}" />
                                    <TextBlock Grid.Column="1" Text="{Binding FlagAndCode}" FontFamily="{StaticResource F1MonoFont}" FontSize="12" Foreground="{StaticResource F1TextBrush}" />
                                    <Border Grid.Column="2" Background="{Binding LicBrush}" CornerRadius="0" Padding="3,1" HorizontalAlignment="Left">
                                        <TextBlock Text="{Binding LicText}" FontFamily="{StaticResource F1MonoFont}" FontSize="10" FontWeight="Bold" Foreground="#FF0B1420" />
                                    </Border>
                                    <TextBlock Grid.Column="3" Text="{Binding IRatingText}" FontFamily="{StaticResource F1MonoFont}" FontSize="11" Foreground="{StaticResource F1MutedTextBrush}" />
                                    <TextBlock Grid.Column="4" Text="{Binding GapText}" FontFamily="{StaticResource F1MonoFont}" FontSize="12" Foreground="{StaticResource F1MutedTextBrush}" />
                                    <TextBlock Grid.Column="5" Text="{Binding P2PText}" FontFamily="{StaticResource F1MonoFont}" FontSize="10" FontWeight="Bold" Foreground="{Binding P2PBrush}" />
                                </Grid>
                            </Border>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>

                <Rectangle Height="1" Fill="{StaticResource F1MutedTextBrush}" Opacity="0.25" Margin="0,8" />

                <UniformGrid Rows="1" Columns="2">
                    <StackPanel Margin="0,0,8,0">
                        <TextBlock Text="BRAKE BIAS" FontFamily="{StaticResource F1MonoFont}" FontSize="9" Foreground="{StaticResource F1MutedTextBrush}" />
                        <TextBlock Text="{Binding BrakeBiasText}" FontFamily="{StaticResource F1MonoFont}" FontSize="14" Foreground="{StaticResource F1TextBrush}" />
                    </StackPanel>
                    <StackPanel>
                        <TextBlock Text="EMBORRACHAMENTO" FontFamily="{StaticResource F1MonoFont}" FontSize="9" Foreground="{StaticResource F1MutedTextBrush}" />
                        <TextBlock Text="{Binding TrackRubberText}" FontFamily="{StaticResource F1MonoFont}" FontSize="12" Foreground="{StaticResource F1TextBrush}" />
                    </StackPanel>
                </UniformGrid>
                <UniformGrid Rows="1" Columns="2" Margin="0,6,0,0">
                    <StackPanel Margin="0,0,8,0">
                        <TextBlock Text="ÚLTIMA VOLTA" FontFamily="{StaticResource F1MonoFont}" FontSize="9" Foreground="{StaticResource F1MutedTextBrush}" />
                        <TextBlock Text="{Binding LastLapText}" FontFamily="{StaticResource F1MonoFont}" FontSize="14" Foreground="{StaticResource F1TextBrush}" />
                    </StackPanel>
                    <StackPanel>
                        <TextBlock Text="MELHOR" FontFamily="{StaticResource F1MonoFont}" FontSize="9" Foreground="{StaticResource F1MutedTextBrush}" />
                        <TextBlock Text="{Binding BestLapText}" FontFamily="{StaticResource F1MonoFont}" FontSize="14" Foreground="{StaticResource F1AccentBrush}" />
                    </StackPanel>
                </UniformGrid>
            </StackPanel>
        </Border>
        <Thumb x:Name="ResizeGrip" Width="14" Height="14" Cursor="SizeNWSE"
               HorizontalAlignment="Right" VerticalAlignment="Bottom"
               Background="{StaticResource F1AccentBrush}" Opacity="0.5"
               DragDelta="OnResizeGripDragDelta" DragCompleted="OnResizeGripDragCompleted" />
    </Grid>
</Window>
```

- [ ] **Step 3: Update `RelativeWidget.xaml.cs`**

Read the current file. Add the title-overload constructor and the two new public methods,
preserving every existing drag/resize/lock member exactly as-is:

```csharp
    public RelativeWidget(WidgetLayout layout, Action onChanged, string title = "RELATIVE") : this(layout, onChanged)
    {
        HeaderText.Text = title;
    }
```

(Place this ABOVE the existing `RelativeWidget(WidgetLayout layout, Action onChanged)` constructor,
which stays completely unchanged — this new overload just chains to it via `: this(layout,
onChanged)` and then sets the header text.)

Add:
```csharp
    public void UpdatePlayerStatus(PlayerCarStatus status) => _viewModel.ApplyPlayerStatus(status);

    public void SetLastLapSeconds(double? seconds) => _viewModel.SetLastLapSeconds(seconds);

    // Only the primary (player-relative) instance passes false here -- the secondary,
    // class-scoped instance's PositionOffset is an absolute class position, not a player offset.
    public void UpdateRows(List<RelativeRow> rows, bool showClassPositionAsAbsolute = false) => _viewModel.SetRows(rows, showClassPositionAsAbsolute);
```

(This changes `UpdateRows`'s signature by adding an optional parameter with a default value — not
a breaking change for `MainWindow.xaml.cs`'s existing call site, which keeps compiling unchanged.)

- [ ] **Step 4: Verify the build and tests**

Run: `dotnet build` — expect `Build succeeded.`, 0 errors, 0 warnings.
Run: `dotnet test` — expect the same 34 tests passing (no new tests in this task — WPF/view-model
code depending on the real telemetry-shaped records, same established boundary).

- [ ] **Step 5: Commit**

```bash
git add src/IracingLiveCoach.App/RelativeWidget.xaml src/IracingLiveCoach.App/RelativeWidget.xaml.cs src/IracingLiveCoach.App/RelativeWidgetViewModel.cs
git commit -m "feat: restyle RelativeWidget to the broadcast layout (flag, LIC badge, iR, OT, footer stats)"
```

---

### Task 4: Restyle `StandingsWidget` to the broadcast layout

**Files:**
- Modify: `src/IracingLiveCoach.App/StandingsWidget.xaml`
- Modify: `src/IracingLiveCoach.App/StandingsWidgetViewModel.cs`

**Interfaces:**
- Consumes: extended `StandingsRow` (Task 2), `F1HighlightBrush` (Task 1).

- [ ] **Step 1: Rewrite `StandingsWidgetViewModel.cs`**

```csharp
// src/IracingLiveCoach.App/StandingsWidgetViewModel.cs
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace IracingLiveCoach.App;

public class StandingsRowViewModel
{
    public string PositionText { get; }
    public string FlagAndCode { get; }
    public string ManufacturerText { get; }
    public string LicText { get; }
    public Brush LicBrush { get; }
    public string IRatingText { get; }
    public string LastLapText { get; }
    public bool IsPlayer { get; }

    private static readonly Brush LicFallbackBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0xA3, 0xAF));

    public StandingsRowViewModel(StandingsRow row)
    {
        IsPlayer = row.IsPlayer;
        PositionText = row.Position.ToString(CultureInfo.InvariantCulture);
        FlagAndCode = $"{row.FlagEmoji} {row.DriverCode}";
        ManufacturerText = row.ManufacturerBadge;
        LicText = row.LicString;
        LicBrush = ParseLicColor(row.LicColorHex);
        IRatingText = row.IRating > 0 ? row.IRating.ToString("N0", CultureInfo.InvariantCulture) : "--";
        LastLapText = row.LastLapTime is double t ? t.ToString("0.000", CultureInfo.InvariantCulture) : "--";
    }

    // Same defensive parse as RelativeWidgetViewModel's own ParseLicColor -- LicColor is a
    // String on the real IRSDKSharper 1.3.0 type, format not independently documented.
    private static Brush ParseLicColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return LicFallbackBrush;
        try
        {
            var cleaned = hex.Trim();
            if (cleaned.StartsWith("0x", System.StringComparison.OrdinalIgnoreCase)) cleaned = "#" + cleaned[2..];
            if (!cleaned.StartsWith("#")) cleaned = "#" + cleaned;
            var color = (Color)ColorConverter.ConvertFromString(cleaned)!;
            return new SolidColorBrush(color);
        }
        catch
        {
            return LicFallbackBrush;
        }
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

- [ ] **Step 2: Rewrite `StandingsWidget.xaml`**

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
        MinWidth="340" MinHeight="240"
        SizeToContent="Manual">
    <Grid Background="Transparent" MouseLeftButtonDown="OnBackgroundMouseLeftButtonDown">
        <Border x:Name="OuterBorder" Style="{StaticResource F1PanelBorder}">
            <Grid Margin="10">
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto" />
                    <RowDefinition Height="Auto" />
                    <RowDefinition Height="*" />
                </Grid.RowDefinitions>

                <TextBlock Grid.Row="0" Text="STANDINGS" FontFamily="{StaticResource F1HeaderFont}" FontSize="13"
                           FontWeight="Bold" Foreground="{StaticResource F1TextBrush}" Margin="0,0,0,6" />

                <Grid Grid.Row="1" Margin="0,0,0,2">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="28" />
                        <ColumnDefinition Width="80" />
                        <ColumnDefinition Width="56" />
                        <ColumnDefinition Width="48" />
                        <ColumnDefinition Width="52" />
                        <ColumnDefinition Width="*" />
                    </Grid.ColumnDefinitions>
                    <TextBlock Grid.Column="0" Text="POS" FontFamily="{StaticResource F1MonoFont}" FontSize="9" Foreground="{StaticResource F1MutedTextBrush}" />
                    <TextBlock Grid.Column="1" Text="PILOTO" FontFamily="{StaticResource F1MonoFont}" FontSize="9" Foreground="{StaticResource F1MutedTextBrush}" />
                    <TextBlock Grid.Column="2" Text="MARCA" FontFamily="{StaticResource F1MonoFont}" FontSize="9" Foreground="{StaticResource F1MutedTextBrush}" />
                    <TextBlock Grid.Column="3" Text="LIC" FontFamily="{StaticResource F1MonoFont}" FontSize="9" Foreground="{StaticResource F1MutedTextBrush}" />
                    <TextBlock Grid.Column="4" Text="iR" FontFamily="{StaticResource F1MonoFont}" FontSize="9" Foreground="{StaticResource F1MutedTextBrush}" />
                    <TextBlock Grid.Column="5" Text="ÚLT. VOLTA" FontFamily="{StaticResource F1MonoFont}" FontSize="9" Foreground="{StaticResource F1MutedTextBrush}" />
                </Grid>

                <ScrollViewer Grid.Row="2" VerticalScrollBarVisibility="Auto">
                    <ItemsControl ItemsSource="{Binding Rows}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Border Padding="0,3">
                                    <Border.Style>
                                        <Style TargetType="Border">
                                            <Style.Triggers>
                                                <DataTrigger Binding="{Binding IsPlayer}" Value="True">
                                                    <Setter Property="Background" Value="{StaticResource F1HighlightBrush}" />
                                                </DataTrigger>
                                            </Style.Triggers>
                                        </Style>
                                    </Border.Style>
                                    <Grid>
                                        <Grid.ColumnDefinitions>
                                            <ColumnDefinition Width="28" />
                                            <ColumnDefinition Width="80" />
                                            <ColumnDefinition Width="56" />
                                            <ColumnDefinition Width="48" />
                                            <ColumnDefinition Width="52" />
                                            <ColumnDefinition Width="*" />
                                        </Grid.ColumnDefinitions>
                                        <TextBlock Grid.Column="0" Text="{Binding PositionText}" FontFamily="{StaticResource F1MonoFont}" FontSize="12" FontWeight="Bold" Foreground="{StaticResource F1AccentBrush}" />
                                        <TextBlock Grid.Column="1" Text="{Binding FlagAndCode}" FontFamily="{StaticResource F1MonoFont}" FontSize="12" Foreground="{StaticResource F1TextBrush}" />
                                        <TextBlock Grid.Column="2" Text="{Binding ManufacturerText}" FontFamily="{StaticResource F1MonoFont}" FontSize="9" FontWeight="Bold" Foreground="{StaticResource F1MutedTextBrush}" />
                                        <Border Grid.Column="3" Background="{Binding LicBrush}" Padding="3,1" HorizontalAlignment="Left">
                                            <TextBlock Text="{Binding LicText}" FontFamily="{StaticResource F1MonoFont}" FontSize="10" FontWeight="Bold" Foreground="#FF0B1420" />
                                        </Border>
                                        <TextBlock Grid.Column="4" Text="{Binding IRatingText}" FontFamily="{StaticResource F1MonoFont}" FontSize="11" Foreground="{StaticResource F1MutedTextBrush}" />
                                        <TextBlock Grid.Column="5" Text="{Binding LastLapText}" FontFamily="{StaticResource F1MonoFont}" FontSize="11" Foreground="{StaticResource F1MutedTextBrush}" />
                                    </Grid>
                                </Border>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </ScrollViewer>
            </Grid>
        </Border>
        <Thumb x:Name="ResizeGrip" Width="14" Height="14" Cursor="SizeNWSE"
               HorizontalAlignment="Right" VerticalAlignment="Bottom"
               Background="{StaticResource F1AccentBrush}" Opacity="0.5"
               DragDelta="OnResizeGripDragDelta" DragCompleted="OnResizeGripDragCompleted" />
    </Grid>
</Window>
```

`StandingsWidget.xaml.cs` needs NO changes — its `UpdateRows(List<StandingsRow>)` signature is
unchanged; only the row's own shape (via `StandingsRowViewModel`) and the XAML grew.

- [ ] **Step 3: Verify the build and tests**

Run: `dotnet build` — expect `Build succeeded.`, 0 errors, 0 warnings.
Run: `dotnet test` — expect the same 34 tests passing.

- [ ] **Step 4: Commit**

```bash
git add src/IracingLiveCoach.App/StandingsWidget.xaml src/IracingLiveCoach.App/StandingsWidgetViewModel.cs
git commit -m "feat: restyle StandingsWidget to the broadcast layout (flag, LIC badge, iR)"
```

---

### Task 5: Second Relative instance (`relative2`) + `MainWindow` wiring

**Files:**
- Modify: `src/IracingLiveCoach.App/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: `RelativeWidget`'s new title-overload constructor and `UpdateRows(rows,
  showClassPositionAsAbsolute)` overload (Task 3), `TelemetryReader.SecondaryRelativeUpdated`/
  `PlayerCarStatusUpdated` (Task 2).

- [ ] **Step 1: Register the second instance and wire the new events**

Read the current file in full first. Add the field `private RelativeWidget? _relativeWidget2;`
alongside `_relativeWidget`. In `Initialize()`, after the existing `_relativeWidget.Show();`, add:
```csharp
        _relativeWidget2 = new RelativeWidget(_layoutStore.Get("relative2", 260, 240), () => _layoutStore.Save(), "RELATIVE — 2ª CLASSE");
        _relativeWidget2.Show();
```
Add `("relative2", "Relative 2ª Classe (F1)", _relativeWidget2)` to the `ControlPanelWindow`
constructor's widget array. After the existing `_telemetryReader.FullRelativeUpdated += ...` line,
add:
```csharp
        _telemetryReader.PlayerCarStatusUpdated += status => Dispatcher.Invoke(() =>
        {
            _relativeWidget?.UpdatePlayerStatus(status);
            _relativeWidget2?.UpdatePlayerStatus(status);
        });
        _telemetryReader.SecondaryRelativeUpdated += rows => Dispatcher.Invoke(() => _relativeWidget2?.UpdateRows(rows, showClassPositionAsAbsolute: true));
```
Add `_relativeWidget2?.SetLocked(_locked);` to `ApplyClickThrough()`. Add
`_relativeWidget2?.Close();` to `OnClosed`.

(Note: the primary `_relativeWidget`'s own `LastLapText` footer field is left at its default "--"
in this pass — wiring the player's own last-lap time into `RelativeWidget.SetLastLapSeconds` needs
a per-tick "my own LapLastLapTime" read that doesn't exist as a convenient event yet; this is a
disclosed, narrow gap, not a silent omission — `BestLapText`/`BrakeBiasText`/`TrackRubberText` are
all live and correct via `PlayerCarStatusUpdated`, only the last-lap footer field stays "--" until
a follow-up wires it, since `PlayerCarStatus` intentionally only carries the BEST lap, not the last
one, per Task 2's own Step 6.)

- [ ] **Step 2: Verify the build and tests**

Run: `dotnet build` — expect `Build succeeded.`, 0 errors, 0 warnings.
Run: `dotnet test` — expect the same 34 tests passing.

- [ ] **Step 3: Commit**

```bash
git add src/IracingLiveCoach.App/MainWindow.xaml.cs
git commit -m "feat: second auto-detecting Relative instance for multiclass sessions"
```

---

### Task 6: Final integration verification

**Files:**
- None (build/test verification only)

- [ ] **Step 1: Full build and test run**

```bash
dotnet build
dotnet test
```
Expected: `Build succeeded.`, 0 errors, 0 warnings; 34 tests passing (26 existing + 8
`CountryFlagsTests`).

- [ ] **Step 2: Cross-check every widget-owning file's `Save()`/lock/shutdown wiring**

Per every prior phase's own final-review lesson, read the final, current `MainWindow.xaml.cs` in
full and confirm ALL TEN windows (Coach, P2P, Relative, Relative2, Standings, Fuel, Weather, Tire
Wear, Radar, ControlPanel) are each: registered in the `ControlPanelWindow` widget array, included
in `ApplyClickThrough()`'s `SetLocked` calls, and included in `OnClosed()`'s `.Close()` calls.

- [ ] **Step 3: Commit** (only if Step 2 required a fix; otherwise nothing new to commit here)
