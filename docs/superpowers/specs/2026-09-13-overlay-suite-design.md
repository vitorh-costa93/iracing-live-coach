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

## Out of scope for this spec

- Rewriting/regrading `.sto` setup files (unrelated, unchanged from the Engineer Chat work).
- Any telemetry not already confirmed to exist in iRacing's public SDK (this spec only uses
  variables independently confirmed this session or well-documented: `CarIdx*` family, tire wear,
  `CarLeftRight`, `TireCompound`).
- Named/saved layout *profiles* (Kapps supports multiple saved layouts per its own UUID-keyed
  config) — Phase 1 ships one layout per widget, matching the Coach/P2P strip's own current
  single-layout behavior; multi-profile support is a natural Phase 4 candidate if the driver wants
  it, not assumed now.
