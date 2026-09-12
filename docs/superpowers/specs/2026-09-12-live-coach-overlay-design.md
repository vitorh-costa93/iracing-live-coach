# Live Coach Overlay — Design (copy)

This is a copy of the spec from `iracing-analytics`
(`docs/superpowers/specs/2026-09-12-live-coach-overlay-design.md`), kept here so this
repo's own implementation plan travels with the document it argues from. The
`iracing-analytics` copy is authoritative if the two ever diverge.

## Context

The user wants a local, free replica of Trophi.ai's core value: real-time, in-session
coaching feedback while driving in iRacing. `iracing-analytics` already has rich
post-session analysis of the same telemetry (corner detection, per-corner consistency
scoring, steering-correction/wheelspin detection), all validated against real driving
data. The new capability is doing an equivalent comparison LIVE, while the car is on
track, shown as an on-screen overlay.

Confirmed with the user (12/09/2026):
- Core gap vs. today: live, in-session feedback (not another post-session view).
- Delivery: visual overlay (not voice).
- Signals for v1: all four — braking point, steering correction, throttle/wheelspin,
  and lap-time delta/consistency per corner (lap-time-per-corner LIVE comparison is
  deferred past v1 in this plan — see "Out of scope" below; it needs a running
  session clock synced to lap start that the other three signals don't).
- No new Supabase project. This app never touches Supabase directly — it calls
  `iracing-analytics`'s own `GET /api/telemetry/local-coach/baselines` endpoint.
- Language/runtime: C#/.NET, chosen for performance (native Windows overlay, no
  Chromium/Electron overhead) — matches SimHub/CrewChief's own approach.

## Endpoint contract (already live in `iracing-analytics`)

`GET https://iracing-analytics.vercel.app/api/telemetry/local-coach/baselines?car=<id>&track=<id>`
Header: `x-import-key: <LOCAL_COACH_SECRET>`

```json
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
    }
  ]
}
```
Any field can be `null` (not enough historical laps to trust that signal for that
corner) — never treat `null` as zero. `corners` can be `[]` (no history yet for this
car/track) — this is not an error.

## Architecture (this repo)

```text
BaselineSync: HTTPS GET to the endpoint above, cached to a local JSON file
  (one file per car+track combo under %APPDATA%/iracing-live-coach/baselines/),
  refreshed on app start / on demand. Reads use whatever is on disk if the
  network call fails -- must never block driving on a failed sync.
  -> TelemetryReader: iRacing SDK live feed via IRSDKSharper (NuGet), at the
     SDK's native update rate (60Hz on track).
  -> LiveCoachEngine: per-sample comparison against the cached baseline for
     whichever corner the car is currently in (looked up by LapDistPct, not
     detected fresh live -- corner boundaries are static per track, already
     computed server-side).
  -> OverlayWindow: WPF, transparent, borderless, always-on-top, click-through
     when idle -- renders live deltas as the car approaches/passes each corner.
```

## Error handling

- No network / endpoint down at sync time: use last cached baseline; if none exists
  yet for this car/track, overlay shows "no history yet" and stays otherwise idle.
- iRacing SDK not running / not in a session: overlay shows an idle state, polls for
  a session rather than erroring out.
- A corner with a `null` signal: overlay skips showing that one signal for that
  corner, never fabricates a comparison.

## Out of scope for this plan

- Voice feedback.
- Any write path back into Supabase or `iracing-analytics` from this app.
- Live lap-time-per-corner comparison (needs a session clock synced to lap start;
  `lapTimeContributionSeconds`/`lapTimeStdDev` are fetched and cached but not yet
  compared live in this plan — a natural follow-up once the other three signals are
  proven out).
- Corner detection running live (corners are looked up from the cached baseline).
- Multi-driver/team comparison, live strategy, fuel/tire modeling.
