# F1-Style Overlay Suite — Design

## Context

The driver wants to replace a commercial tool ("Kapps") with a free, self-built overlay suite,
styled after F1 broadcast graphics, fully customizable in position/size per widget, hosting the
existing Live Coach (`iracing-live-coach`) alongside new widgets. Confirmed with the driver
(13/09/2026, "não precisa validar nada comigo, eu confio total no seu poder de execução" — design
decisions below are made directly, not by round-tripping questions):

- **Requested now, explicitly**: F1-styled Relative, Standings, Fuel Calculator, a combined
  Weather + Track Usage + Track Wetness + Precipitation widget, a radar-style spotter (replacing
  the traditional left/right proximity bar with a top-down view like Le Mans Ultimate's), real-time
  tire wear, and the existing Coach (corner-coaching card + P2P strip) repositionable inside the
  same suite instead of being its own separate pair of windows.
- **Corrected finding, not a design choice**: "real-time tire wear" is not achievable by any
  external tool, including this one. iRacing deliberately withholds `LFwearL/M/R` … `RRwearL/M/R`
  (and tire temperature) from live telemetry except while the car is in the pit stall — confirmed
  directly by a third-party overlay developer's own explanation ("iRacing has very specific rules
  regarding when certain data updates. If you run some laps then pit you will see tyre info will
  only then update") and by an independent write-up describing the same pit-stall-only refresh.
  This is a platform-level competitive-integrity restriction, not a Kapps limitation — every tool
  reading iRacing's public telemetry (SimHub, CrewChief, Kapps, this app) is bound by it equally.
  The tire widget below is designed around this reality rather than promising something impossible.
- **Reference for the widget/settings model**: `%AppData%\Kapps\settings.json` (inspected directly,
  13/09/2026) shows Kapps' own real widget set and layout mechanism — one host process
  (`racingOverlay`) with independently positioned "layers" (`relatives`, `fuelCalc`, `pitHelper`,
  `standings2`, `trackMap`, `carLeftRight`) plus a `layersControlPanel` for managing them. This
  app's own architecture (below) mirrors that *functional* pattern (one host, N independent
  windows, one control panel) — not Kapps' visual style, which is being replaced with F1 broadcast
  styling per the driver's request.
- **Open door, not yet built**: the driver is open to additional overlay ideas from competing
  tools (RaceLab, Grid&Go, iOverlay) *if* each is presented with a plain-language explanation and a
  concrete use case, so they can opt in or skip it. This spec does not pre-select any of those —
  they're proposed as a follow-up list once the core suite (below) is running, not bundled into
  this build.

## Phasing (this is too large for one implementation plan)

| Phase | Scope |
|---|---|
| **Phase 1** (this cycle) | Shared host architecture, Control Panel, F1 visual design system, migrate the existing Coach card + P2P strip into the new host, F1-styled Relative, F1-styled Standings. |
| **Phase 2** | Fuel Calculator, combined Weather/Track-Usage/Wetness/Precipitation widget. |
| **Phase 3** | Tire wear widget (pit-stall-refresh, honestly labeled), radar-style spotter. |
| **Phase 4 (opt-in)** | Additional widgets proposed from RaceLab/Grid&Go/iOverlay, each with an explanation + use case, driver picks which to build. |

Each phase gets its own plan + SDD execution cycle, same pattern as the Live Coach and Engineer
Chat work already completed this session. This document covers the whole vision so later phases
have a stable architecture to build against, but only Phase 1 gets a task-by-task plan right now.

## Architecture

```text
IracingLiveCoach.App (existing WPF process, extended)
  OverlayHost (new)
    - Owns ONE TelemetryReader instance (already exists) -- every widget subscribes to the SAME
      telemetry stream instead of each opening its own IRacingSdk connection (IRSDKSharper only
      supports one consumer per process cleanly; today's two-window MainWindow/RelativeOverlayWindow
      split already shares one TelemetryReader this way -- the pattern just extends to N windows).
    - Owns ONE AppSettings-equivalent store (WidgetLayoutStore, replacing today's ad-hoc
      Left/Top/Width/Height fields with a keyed dictionary: one entry per widget, by a stable
      string id -- "coach", "p2p", "relative", "standings", ... -- so adding a widget later never
      requires a schema migration, just a new key.
    - Owns the shared lock/unlock (click-through) toggle -- unchanged behavior, now applies to
      every registered widget window at once via the tray icon, exactly as today's two windows.
  ControlPanelWindow (new) -- the one non-click-through, always-interactive window. Lists every
    widget with a visibility checkbox and (when unlocked) shows the same drag/resize affordances
    the widgets themselves already have. This is the direct equivalent of Kapps'
    layersControlPanel, restyled to match the F1 visual language.
  Widget windows (each independently positioned/sized, each a thin WPF Window like today's
    RelativeOverlayWindow):
    - CoachWidget (migrated from today's MainWindow -- same LiveCoachEngine wiring, restyled)
    - P2PWidget (migrated from today's RelativeOverlayWindow -- same telemetry, restyled)
    - RelativeWidget (NEW -- full running order relative to the player, F1-style, not the
      P2P-focused strip)
    - StandingsWidget (NEW -- full race classification)
```

`TelemetryReader` (existing) gains additional events following the same pattern already
established for `SessionDetected`/`RelativeUpdated`: a `StandingsUpdated` event (per-car position,
gap, last lap, tire compound, driver name — all already-confirmed-real `CarIdx*` telemetry
variables) fired alongside the existing per-tick reads, so this stays consistent with how the P2P
strip already layered onto the same reader without duplicating the SDK connection.

## Visual design system ("F1 broadcast" look)

A shared WPF resource dictionary (`F1Theme.xaml`), referenced by every widget, so restyling later
means editing one file, not N:

- **Palette**: near-black panel background (`#0A0E14`, ~90% opacity), high-contrast off-white text
  (`#F2F4F7`), one strong accent color used for headers/dividers/position-change indicators
  (default `#E10600` — F1's own broadcast red is a public convention this app can echo without
  claiming affiliation; the accent is a theme value, not hard-coded per widget, so the driver can
  retint later without touching each widget's own file).
- **Typography**: a condensed, bold sans-serif for headers and numbers (`Archivo Narrow` or
  `Titillium Web` — both free/Google Fonts-licensed, matching the tight, numbers-forward style of
  real broadcast graphics) via `Fonts/` embedded resources (WPF supports embedding font files
  directly, no internet dependency at runtime — important, this is a local desktop app).
  Tabular figures for anything numeric (gaps, lap times) so columns of numbers align.
- **Layout convention**: sharp rectangular panels (no rounded corners, unlike the existing
  Coach/P2P widgets' rounded HUD look — intentionally different register, matching F1 broadcast's
  own flat rectangular lower-thirds), a colored accent bar on the leading edge of each row (this
  IS the real F1 broadcast convention for gained/lost-position rows — green/red left-edge stripe —
  not a generic decorative border), tabular-aligned columns for position/gap/interval data.
- **Motion**: position-change rows briefly flash the accent color on change (a `Storyboard`
  triggered from `INotifyPropertyChanged`), matching broadcast graphics' own emphasis convention
  for an on-air overtake — used sparingly (position changes only, not every telemetry tick).

## Widget specs (Phase 1)

### Control Panel

- One row per registered widget: name, a visibility checkbox, and (only while the suite is
  unlocked) the widget's current position/size shown as editable numbers for precise placement
  (a direct improvement over drag-only positioning, useful for exactly aligning a widget against
  iRacing's own native HUD elements — the same use case that drove the P2P strip's own manual
  alignment need).
- Its own window is excluded from the global click-through lock (it must always be interactive to
  be useful) but still hides from the click-through *content* when the suite is locked — i.e. it
  becomes invisible (not just non-interactive) when locked, matching Kapps' own control-panel
  behavior of only showing up when you actually want to configure something.

### RelativeWidget (F1-style, full running order — new, distinct from the existing P2P strip)

- Rows: 3 cars ahead, the player's own row (visually distinct — filled accent background), 3 cars
  behind. Each row: position-in-class or overall (driver's choice via Control Panel), driver
  three-letter code (from `DriverModel.AbbrevName`/`Initials`, session info, already read once for
  car/track auto-detection — extending that same read to cover all drivers, not just the player),
  live gap in seconds (`CarIdxLapDistPct` + the player's own speed, same delta-time approach
  standard relative widgets use), tire compound icon if available (`CarIdxTireCompound`, a
  documented telemetry variable), and the existing P2P indicator folded in as a small badge per
  row (replacing the separate P2P strip's need to exist as its own window, per the driver's own
  "embutir" request) — the P2P strip window itself is retired once this ships; positions already
  saved for it are not needed after migration since it no longer exists as a separate widget.

### StandingsWidget (F1-style, full classification — new)

- Full running order (not just nearby cars), position, driver code, class (for multiclass
  sessions), gap to leader, last lap time, tire compound. Same telemetry-reading approach as
  Relative, just iterating every classified car instead of only near-the-player ones.

### CoachWidget / (folded) P2P badge

- Visually restyled to the F1 theme (sharp rectangular panel, condensed font, accent-bar row
  style) but functionally unchanged from the already-shipped Coach: same `LiveCoachEngine` wiring,
  same corner-feedback content, same auto-detected car/track/baseline flow. This is a reskin, not
  a rebuild — the coaching logic (`LiveCoachEngine`, `BaselineSync`, `AppSettings`'s `ImportKey`)
  carries over unchanged.

## Widget specs (Phase 2 — detailed now, telemetry independently confirmed 13/09/2026)

Every variable below was independently confirmed against `sajax.github.io/irsdkdocs` before being
designed against, following this session's own established rule (see the tire-wear correction
above): `FuelLevel` (float, liters), `FuelUsePerHour` (float, liters/hour), `LapCompleted` (int,
player's own completed-lap counter — distinct from `Lap`, which reports the currently-*started*
lap and would double-count a lap-boundary tick), `LapLastLapTime` (float, seconds), `AirTemp`
(float), `TrackTemp` (float), `Precipitation` (float, 0-1 fraction — iRacing's own docs note
uncertainty whether this is track-wide or start/finish-line-local; disclosed as such in the UI,
not presented as more precise than it is), `TrackWetness` (enum 0-7:
Unknown/Dry/MostlyDry/VeryLightlyWet/LightlyWet/ModeratelyWet/VeryWet/ExtremelyWet),
`WeatherDeclaredWet` (bool), `CarIdxLapDistPct` (float 0-1 per car, already used by this same
session's own Phase 3 spotter design below).

### FuelWidget (new)

- Fuel calculator, matching the shape every established sim-racing fuel tool already uses (Kapps'
  own `fuelCalc` widget, confirmed present in `%AppData%\Kapps\settings.json` this session) —
  reading the same telemetry, not reinventing the calculation:
  - **Current fuel**: `FuelLevel`, shown as a plain liters readout.
  - **Fuel per lap (rolling average)**: `TelemetryReader` watches `LapCompleted` for an increment;
    on each increment it records `FuelLevel`'s delta since the previous increment as one lap's
    consumption, and keeps a rolling window of the last 5 completed laps (rather than 1, so an
    outlier lap — a spin, an off-track excursion burning extra fuel briefly, or a formation/caution
    lap — doesn't swing the estimate) . `FuelUsePerHour` is shown alongside as iRacing's own
    instantaneous secondary figure, not used for the primary estimate (an instantaneous rate is
    noisier than a rolling per-lap average, which is why standard fuel calculators use the latter).
  - **Laps remaining**: `FuelLevel / averageFuelPerLap`, `null`/"--" until at least one full lap's
    rolling average exists (never show a number computed from zero samples).
  - **Time remaining**: `lapsRemaining × averageLapTime`, where `averageLapTime` is the same
    rolling-5-lap average applied to `LapLastLapTime`.
  - No target-fuel-for-race-distance input in this pass (Kapps' own fuel calculator has one, but it
    requires the driver to enter a target lap count/time the app has no other source for) — logged
    as a natural, disclosed Phase 4 candidate rather than a half-built input field now.

### Weather / Track Usage widget (new — combines four related readouts the driver asked to see
### together, per "eu quero que você coloque no mesmo overlay")

- **Weather row**: `AirTemp`/`TrackTemp` as plain numeric readouts (°C, matching iRacing's own
  session default unit for this driver's region), `Precipitation` as a percentage bar with the
  disclosed track-vs-point-source caveat in a tooltip/label, `TrackWetness` rendered as its own
  7-step enum label (not a raw number) with a color ramp from dry (muted gray) to extremely wet
  (accent-saturated blue), `WeatherDeclaredWet` as a small badge that only appears when true (it's
  a boolean rules-flag, not a continuous value, so it doesn't need its own permanent row).
- **Track usage row**: a single-file horizontal bar representing one full lap (0% to 100%), with
  one dot per car positioned by that car's own current `CarIdxLapDistPct`. This is explicitly a
  **linear** representation, not a geometrically accurate track shape — this app has no per-track
  GPS boundary dataset (that data lives in the separate `iracing-analytics` web project's Supabase
  store, built from real OSM boundaries, and isn't available to this offline desktop app). Drawing
  a fake curved track shape without real geometry would be the same kind of overclaim this session
  already corrected once for tire wear; a plain linear bar honestly shows the same underlying
  information (where on the lap every car currently is) without pretending to know the track's
  actual layout.

## Widget specs (Phase 3 — designed now so Phase 1's architecture doesn't box it out later)

### Tire wear widget (honest about the pit-stall-only refresh)

- Four tire blocks (LF/RF/LR/RR), each showing the three tread-remaining zones
  (`{corner}wearL/M/R`, e.g. `LFwearL`, `LFwearM`, `LFwearR`) as a small three-segment bar, colored
  by remaining tread (green > 60%, amber 30-60%, red < 30%) — visually similar to real F1
  broadcast tire-wear graphics.
- A visible "atualizado às HH:MM:SS" (or "no pit stop") timestamp under the block, refreshed only
  when the underlying telemetry values actually change — so the driver always knows this is a
  snapshot from the last pit visit, never mistaking a static number for a live one. This labeling
  is the actual deliverable of this widget, not a caveat bolted on afterward — it is what makes an
  otherwise-misleading display honest.

### Spotter radar (Le Mans Ultimate-style, replacing the left/right bar)

- A top-down radar: the player's own car fixed at the center-bottom, blips for nearby cars placed
  by longitudinal gap (`CarIdxLapDistPct` delta, converted to an approximate distance using track
  length, same technique `TelemetryReader`'s own P2P proximity-by-position logic already uses) and
  lateral side (`CarLeftRight`, a documented bitfield telemetry variable reporting which side a
  nearby car is detected on — this is the SAME signal a traditional left/right bar reads, just
  rendered as a positioned blip instead of a binary left/right lit segment, which is the exact
  "radar instead of bar" visual upgrade requested). No fabricated lateral distance — `CarLeftRight`
  is a coarse (left/clear/right/2-cars) signal, not precise lane position, so blips snap to a few
  fixed lateral lanes rather than a smooth continuous radar sweep; this is disclosed as the
  signal's real resolution, not oversold as a precise radar.

## Visual redesign — broadcast UI overhaul (Phase 5, 14/09/2026)

The driver supplied a reference mockup (`iRacing / Overlay Concept`: a Standings panel plus two
side-by-side Relative panels, one per car class) asking for this exact visual language to replace
the F1-theme look Phases 1-3 shipped, while keeping the Kapps-equivalent UX (independent
draggable/resizable windows, Control Panel visibility list, shared lock).

**Correction, 14/09/2026:** this section originally concluded flags/team-logos/brake-bias/rubber
weren't available and needed honest substitutes, based on the community-maintained
`sajax.github.io/irsdkdocs` wiki and Kapps' own cached app data. The driver pushed back, correctly:
that wiki is stale. Direct reflection against the actual `IRSDKSharper.dll` this project already
references (NuGet `irsdksharper` 1.3.0, a build dated 24/07/2026 — materially newer than the
wiki's own last coverage) plus targeted fresh searches turned up real fields the wiki simply
doesn't list yet, most from iRacing's 2025 Season 3 patch (which added driver "flair"/flag support
and, per that patch's own release notes, "removed Clubs from the Simulation systems... and
telemetry data" — replacing club-based driver identity with the newer flair system the wiki never
picked up). Every item below marked "confirmed" was checked against this project's own actual
compiled dependency, not just secondhand documentation — the same standard as every other
telemetry claim in this document, just applied a second time after getting it wrong once.

### Confirmed buildable from real telemetry/session data

- **Palette**: dark navy background (`#0B1420`), a warm red-orange leading accent bar (`#E2483D`)
  replacing F1Theme's pure red, a cyan highlight (`#39D8E0`) for the player's own row (replacing
  the flat accent-tint fill Phases 1-3 used), off-white text (`#F2F4F7`, unchanged). This becomes
  the suite's one palette — `F1Theme.xaml`'s existing brush *values* are updated in place (not a
  second parallel theme), since every current widget keeps using the same resource *keys*.
- **National flags**: `DriverModel.FlairID` (int) / `FlairName` (string) — confirmed present by
  reflecting on the real `IRSDKSharper.dll` `DriverInfoModel.DriverModel` type this app already
  uses (added alongside the 2025 S3 flair feature, replacing the old `ClubName`/`ClubID` fields
  the wiki still documents as the "team & organization" data — both actually still exist
  side-by-side on the current type, `ClubName` just isn't the identity signal it used to be).
  `FlairName` is the country's display name (e.g. "Brazil") — rendered as a Unicode regional-
  indicator flag emoji (e.g. 🇧🇷) via a small country-name→ISO-3166-alpha-2 lookup table covering
  every country iRacing's own driver base realistically spans, with a plain globe glyph fallback
  for any name the table doesn't recognize (bounded, disclosed coverage — not every possible
  string `FlairName` could theoretically contain, but every country actually fielding drivers).
  No bundled flag image assets are needed; Windows' own emoji font (Segoe UI Emoji, present on
  every supported Windows version) renders these natively.
- **LIC badge**: `LicColor` (string)/`LicString`/`LicLevel` — confirmed present (and confirmed as
  `String`, not the packed-int this document originally assumed — corrected). Parsed defensively
  (same try/catch posture as every telemetry read in this app) as a standard CSS-style hex color
  string (`#RRGGBB` or `0xRRGGBB`, iRacing's own YAML convention for its color-string fields),
  with a neutral gray fallback if parsing fails. Rendered as a small colored chip showing
  `LicString` (e.g. "A 2.94").
- **iR**: `IRating`, real, already read for driver identification.
- **GAP / ÚLT. VOLTA / Δ VOLTA**: already real and shipped (`RelativeRow.GapSeconds`,
  `StandingsRow.LastLapTime`); `Δ VOLTA` (this lap vs. the driver's own last lap) is a new,
  cheaply-computable client-side delta, not a new telemetry read.
- **BRAKE BIAS**: `dcBrakeBias` — confirmed real, a live-updating "driver car" (`dc*`) telemetry
  channel reporting the current in-car brake-bias adjustment as a percentage (some cars report
  `dcPeakBrakeBias` instead — read both, matching whichever the current car publishes, with the
  same "channel not published this session" tolerance `RelativeUpdated`'s own P2P read already
  has for non-P2P classes).
- **EMBORRACHAMENTO (track rubber buildup)**: `SessionInfo.Sessions[current].SessionTrackRubberState`
  — confirmed real, reflected directly off the current `SessionInfoModel.SessionModel` type. A
  session-info string (read alongside `WeekendInfo.TrackID` at the same `OnSessionInfo` cadence
  this app's existing `_playerCarIdx` detection already uses, not a per-tick telemetry channel),
  shown as its own plain-text readout rather than parsed into a synthetic numeric scale iRacing
  itself doesn't publish as a number.
- **OT (Overtake/P2P) column, WITH a real countdown — corrected 14/09/2026**: no telemetry
  variable publishes the OTS's own duration/cooldown constants, but the SF23's Overtake System
  rules ARE publicly documented (iRacing's own car page): 20 seconds of activation per use, at
  least 100 seconds of cooldown ("ReTime") afterward, a 200-second total budget per race. This is
  the same kind of car-specific domain knowledge the sibling `iracing-analytics` project already
  hardcodes for setup engineering (ARB/differential/spring targets per car architecture) — a
  documented rule, not a telemetry read, combined with the REAL live signal
  (`CarIdxP2P_Status`'s true/false transitions) to compute an actual, accurate countdown: on a
  false→true transition, count down from 20s ("ATIVO, Ns"); on the matching true→false transition,
  count down a 100s cooldown ("RECARGA, Ns"); otherwise "PRONTO". `CarIdxP2P_Count` (uses
  remaining, real) is shown alongside. Disclosed caveat: these 20s/100s constants are specific to
  the SF23's own documented Overtake System as of this research and could drift if iRacing rebalances
  it in a future season, or not apply to a different P2P-enabled car this app hasn't researched —
  the column always shows the real `CarIdxP2P_Status`/`CarIdxP2P_Count` regardless, with the
  countdown numbers specifically flagged (in a code comment, not a UI caption per the driver's own
  "no explanatory legends" request) as SF23-sourced. This column only renders for classes where
  P2P is published — a GT3 panel simply omits it rather than showing a fake "N/A".
- **Manufacturer badge — corrected 14/09/2026**: the driver's own "team logo" ask is realistically
  every competing overlay's car-MANUFACTURER badge (Ferrari, Porsche, BMW, etc. — sim racing rarely
  has real teams outside league play, but every car has a real manufacturer), not a literal
  third-party "team" concept. `CarScreenName`/`CarPath` (confirmed real, already available on
  `DriverModel`) identify the car precisely enough to derive a manufacturer name via string
  matching (e.g. "Ferrari 296 GT3" → "FERRARI"). Real DATA, but NOT a real logo IMAGE — iRacing's
  SDK ships no manufacturer logo assets to third parties, and bundling actual trademarked
  manufacturer logo graphics into this app carries real (if small, for a personal single-user
  tool) trademark risk and would require sourcing/drawing dozens of logo assets, a materially
  larger effort than a data-availability question. Substituted with a plain text manufacturer
  badge (e.g. "FERRARI") in the car class's own color — real identity information, broadcast
  graphics also commonly use text constructor badges alongside logos, without the asset/legal
  overhead of real logo images.
- **Dual-class Relative**: Kapps' own `settings.json` (already inspected, Phase 1) keys widget
  instances by UUID under `layerWindowConfigs`, meaning Kapps genuinely supports multiple
  instances of the same widget type (e.g. two independently-positioned Relative panels). This
  spec adds exactly ONE second fixed instance (`relative2`) rather than building fully generic
  N-instance widget management (out of scope, see below) — each of the two Relative widgets gets
  its own car-class filter (a dropdown or Control-Panel-driven setting), defaulting to
  `relative`=player's own class and `relative2`=off until the driver picks a class.

### Still not available — honest substitution, not fabrication

- **Team/constructor logos**: `TeamName`/`TeamID` exist as text (confirmed, unchanged from the
  original finding) but carry no logo asset, and are empty for the vast majority of pickup/public
  races (only populated in real team-league events). Shown as plain text only when non-empty,
  never a fabricated or generic logo image — this is the one mockup element still without a real
  data-backed equivalent after the correction above.
- **ΔiR (projected iRating change)**: not published live by iRacing. Built as a disclosed
  *estimate* using the standard SoF-based projection formula every public iRating calculator
  already uses (session's own field-strength derived from the visible `IRating` spread) — shown
  with a small `*` marker as the mockup itself already did.
- **Explanatory legends** (e.g. "verde = mais rápido", "ΔiR projetado pela posição atual" as a
  spelled-out caption): removed per the driver's explicit request ("só não quero essas
  explicações do que cada coisa significa") — the visual convention itself (color, the `*` marker)
  stays, just without a caption spelling it out.

### Out of scope for this pass

- Fully generic N-instance widget management (Kapps' own UUID-keyed multi-instance model in
  general) — this pass adds exactly one extra fixed Relative instance (`relative2`), not an
  "add widget" button. A driver wanting a third Relative pane is a natural next-pass request.
  Named/saved layout profiles remain out of scope (unchanged from the original spec's own
  "Out of scope" section below).

## Out of scope for this spec

- Rewriting/regrading `.sto` setup files (unrelated, unchanged from the Engineer Chat work).
- Any telemetry not already confirmed to exist in iRacing's public SDK (this spec only uses
  variables independently confirmed this session or well-documented: `CarIdx*` family, tire wear,
  `CarLeftRight`, `TireCompound`).
- Named/saved layout *profiles* (Kapps supports multiple saved layouts per its own UUID-keyed
  config) — Phase 1 ships one layout per widget, matching the Coach/P2P strip's own current
  single-layout behavior; multi-profile support is a natural Phase 4 candidate if the driver wants
  it, not assumed now.
