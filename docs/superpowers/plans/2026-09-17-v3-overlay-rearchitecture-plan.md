# V3 Overlay Rearchitecture — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the WPF layered-window overlay renderer with a GPU-composited renderer (Direct3D11 + Direct2D + DirectWrite + DirectComposition) matching the fluidity of Kapps/GoFast, while preserving every validated feature of the current app (telemetry logic, widget behavior, configuration options). The current app is frozen as a backup; the new app is built alongside it for side-by-side comparison.

**Architecture:** Extract all UI-framework-agnostic logic (telemetry reading, domain models, calculations, capabilities, persistence) into `IracingLiveCoach.Core`, already partially separated. Build a new `IracingLiveCoach.OverlayHost` project that owns a DirectComposition swapchain per overlay window and draws every widget through a shared declarative layout engine using Direct2D/DirectWrite. The Control Center (configuration UI) stays WPF, communicating with the overlay host over a local typed IPC channel so config changes apply live without restarting.

**Tech Stack:** .NET 8, C#, Vortice.Windows (Direct3D11/Direct2D1/DirectWrite/DirectComposition bindings — actively maintained, verify current version before pinning), IRSDKSharper (existing telemetry library, unchanged), WPF (Control Center only).

**Spec:** [docs/superpowers/specs/2026-09-17-v3-overlay-rearchitecture-spec.md](../specs/2026-09-17-v3-overlay-rearchitecture-spec.md) — this plan argues from that spec; read both.

## Global Constraints

(Copied verbatim from the spec — every task below implicitly inherits these.)

- Six widgets only: Standings, Relative, Weather Report, Fuel Calculator, Radar, Start Helper. No map, no inputs widget, no session summary, no separate delta widget.
- One shared visual/component system for all widgets — no per-widget bespoke layout code with hardcoded positions.
- Preview (Control Center) and real overlay render through the exact same layout/rendering engine — never two divergent visual implementations.
- No widget exceeds 25% of the target area's physical width; a 7-driver Relative/Standings never exceeds 35% of physical height, measured post-DPI-scale.
- No decorative widget titles or column-name headers during a race in Standings/Relative.
- Full first+last driver names by default; abbreviation is opt-in, never automatic.
- ΔiRating appears only in Standings, in the same badge as iRating, same row (`3.694 +12`), never in Relative.
- Class color strip keyed by session ClassId only — never manufacturer, licence, team, or row order.
- Overtake (OT) shows seconds + a proportional bar only — no PRONTO/ATIVO/RECARGA text, no icons, in the live cell. States (available/triggered/cooldown/depleted/unsupported/unknown) are modeled separately from the numeric balance.
- All colors come from the single normative HEX palette (spec §16), centralized as tokens — never scattered literals.
- Antialiasing is an acceptance requirement: DirectWrite grayscale AA on transparent surfaces, `D2D1_ANTIALIAS_MODE_PER_PRIMITIVE` for vector geometry, correct premultiplied-alpha handling, per-monitor DPI awareness with target/cache recreation on DPI or monitor change.
- Reference test hardware: Ryzen 5 5500X3D, GTX 1660 6GB, 16GB RAM, 1080p165Hz, iRacing borderless-windowed. No performance claims for other configurations without separate testing.

---

## Scope Decomposition

This spec spans multiple independent subsystems (GPU rendering core, shared layout engine, six widgets, Control Center, persistence/profiles, performance validation). Per plan-writing practice, it is broken into phase-plans below; **only Phase 0 is detailed to the step level in this document**, because the spec itself mandates proving GPU viability before committing to the full rewrite (§3: *"Primeiro construa uma prova técnica... Confirme a viabilidade no Windows antes de expandir todos os widgets"*). Phases 1–7 are scoped with concrete files and acceptance criteria; each gets its own bite-sized task breakdown (a follow-up plan document) once the phase before it is validated and merged. Re-planning between phases is intentional, not a shortcut — a GPU spike commonly changes assumptions the later phases depend on.

| Phase | Deliverable | Plan status |
|---|---|---|
| 0 | GPU technical proof (transparency, click-through, text, DPI, multi-monitor, Radar/Start Helper pacing) | **Detailed below** |
| 1 | Core extraction + telemetry adapter + IPC skeleton | Scoped below, detailed plan written after Phase 0 signs off |
| 2 | Shared declarative layout engine + visual token system | Scoped below |
| 3 | Standings + Relative (incl. OT) | Scoped below |
| 4 | Weather, Fuel, Radar, Start Helper | Scoped below |
| 5 | Control Center V3 (all tabs, live preview) | Scoped below |
| 6 | Profiles, persistence, import/export, undo/restore | Scoped below |
| 7 | Validation, benchmarking, packaging | Scoped below |

---

## Phase 0 — GPU Technical Proof

**Files:**
- Create: `src/IracingLiveCoach.OverlayHost/IracingLiveCoach.OverlayHost.csproj`
- Create: `src/IracingLiveCoach.OverlayHost/GpuOverlayWindow.cs`
- Create: `src/IracingLiveCoach.OverlayHost/DeviceResources.cs`
- Create: `src/IracingLiveCoach.OverlayHost/Program.cs`
- Test: manual/visual (documented below) — no automated UI test framework exists for this; acceptance is evidence-based per spec §13/§19.

**Interfaces:**
- Produces: `DeviceResources` — owns `ID3D11Device`, `IDCompositionDevice`, `IDCompositionTarget`, `ID2D1DeviceContext`, `IDWriteFactory`, with `Resize(int width, int height)` and `HandleDeviceLost()` methods that later phases (1+) depend on for all drawing.
- Produces: `GpuOverlayWindow` — a Win32 window wrapper exposing `SetClickThrough(bool)`, `BeginDraw()/EndDraw()`, and a `Dpi` property, which Phase 2's layout engine will render into.

- [ ] **Step 1: Confirm Vortice.Windows package versions**

Run: `dotnet add package Vortice.Direct3D11 --version-compat --dry-run` is not a real flag — instead check https://www.nuget.org/packages/Vortice.Direct3D11 current stable version manually, or run:
```bash
dotnet package search Vortice.Direct3D11 --exact-match
```
Record the exact version resolved (do not float `*`). Pin `Vortice.Direct3D11`, `Vortice.DXGI`, `Vortice.Direct2D1`, `Vortice.DirectWrite`, `Vortice.DirectComposition` to the same matched version.

- [ ] **Step 2: Create the OverlayHost project**

```bash
dotnet new console -n IracingLiveCoach.OverlayHost -o src/IracingLiveCoach.OverlayHost --framework net8.0-windows
dotnet sln add src/IracingLiveCoach.OverlayHost/IracingLiveCoach.OverlayHost.csproj
```
Edit the `.csproj` to add `<UseWindowsForms>false</UseWindowsForms>`, `<OutputType>WinExe</OutputType>`, and the five pinned Vortice package references from Step 1.

- [ ] **Step 3: Win32 layered-but-GPU-composited window**

In `GpuOverlayWindow.cs`, create a borderless, topmost, `WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW` window (transparent flag toggled by `SetClickThrough`), but back it with a DirectComposition visual tree instead of `UpdateLayeredWindow` — this is the actual fix for the software-compositing bottleneck. Use `DCompositionCreateDevice3`/`IDCompositionDevice.CreateTargetForHwnd` bound to the HWND, one `IDCompositionVisual` per widget window, committed via `IDCompositionDevice.Commit()`.

- [ ] **Step 4: Direct2D draw target + one static text draw**

In `DeviceResources.cs`, create the `ID3D11Device` (`D3D11CreateDevice` with `D3D11_CREATE_DEVICE_BGRA_SUPPORT`), wrap it for D2D via `CreateDXGIDeviceContext`, create an `IDXGISwapChain1` in `DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL` composition mode, and get an `ID2D1DeviceContext`. Draw one static string ("V3 PROOF") with DirectWrite, grayscale AA, at a fixed position, and present via `IDCompositionDevice.Commit()`.

- [ ] **Step 5: Verify real per-pixel transparency**

Run the proof app, take a screenshot with something colorful behind it (not solid black — this is exactly the bug from the earlier DWM/Aero attempt). Confirm the window background is genuinely transparent and only the drawn text is opaque.

- [ ] **Step 6: Click-through toggle**

Wire `SetClickThrough(true)` to add `WS_EX_TRANSPARENT` (mouse events pass to iRacing) and `SetClickThrough(false)` to remove it (window is clickable, for the future edit mode). Verify manually: with click-through on, clicking where the text is drawn does not focus the overlay; with it off, it does.

- [ ] **Step 7: DPI + multi-monitor test**

Run the proof window on a monitor at 100%, then at 125%/150% (or use `resize_window`-equivalent Windows display scaling settings), and on a secondary monitor if available. Confirm text stays crisp (not blurry/bitmap-scaled) by recreating the D2D device context and swap chain on `WM_DPICHANGED`. Record actual DPI values tested — do not claim untested scales work.

- [ ] **Step 8: Radar/Start Helper pacing test**

Add a second proof window that redraws a moving dot (simulating a radar blip) at 60Hz and 120Hz via a `System.Threading.Timer` or a dedicated render thread with `DXGI_PRESENT_PARAMETERS` and measure actual presented frame times (log to a file, not console, per spec §13 — technical logs stay in diagnostics). Confirm no visible queuing/backlog of frames (the "growing queue" failure mode explicitly called out in spec §3).

- [ ] **Step 9: Document findings**

Write `docs/superpowers/specs/2026-09-17-v3-phase0-findings.md` recording: exact Vortice package versions used, transparency confirmed (yes/no + screenshot reference), click-through confirmed, DPI values actually tested and result, monitors tested, measured frame times (p50/p95/p99) for the pacing test, and any limitation discovered (e.g., ClearType artifacts, device-lost behavior not yet handled). This file is the gate — Phase 1 does not start if any of transparency, click-through, or text sharpness fails.

- [ ] **Step 10: Commit**

```bash
git add src/IracingLiveCoach.OverlayHost docs/superpowers/specs/2026-09-17-v3-phase0-findings.md IracingLiveCoach.sln
git commit -m "feat(v3): GPU overlay technical proof -- DirectComposition transparency, click-through, DPI, frame pacing

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

## Phase 1 — Core Extraction + Telemetry Adapter (scoped)

**Files:**
- Modify: `src/IracingLiveCoach.Core/Models.cs` — audit against spec §1 for gaps (per-car capability flags: has-P2P, is-multiclass, session type).
- Move/adapt: `src/IracingLiveCoach.App/TelemetryReader.cs` → `src/IracingLiveCoach.Core/Telemetry/TelemetryReader.cs`, keeping the current, user-validated P2P dual-path logic (`ReadP2P`, `ReadP2PCount`, `P2PMaxSeconds = 200`) byte-for-byte — do not "fix" it again without new evidence.
- Create: `src/IracingLiveCoach.Core/Telemetry/TelemetrySnapshot.cs` — immutable snapshot with timestamp, session id, per-field validity flags (spec §1: "documente capacidades reais por carro e sessão... inclusive dados ausentes, desatualizados e derivados").
- Create: `src/IracingLiveCoach.OverlayHost/Ipc/` — typed, versioned local IPC (named pipes or a local loopback socket) between Control Center and OverlayHost, with reconnect and schema versioning (spec §3).

**Acceptance:** Core project builds with zero WPF/UI references (verify via `dotnet build src/IracingLiveCoach.Core` referencing nothing from `System.Windows.*`); existing 38 unit tests still pass unmodified against the moved `TelemetryReader`.

## Phase 2 — Shared Declarative Layout Engine (scoped)

**Files:**
- Create: `src/IracingLiveCoach.OverlayHost/Layout/` — column definitions, text measurement via cached `IDWriteTextLayout`, row/header model, per-widget layout descriptor consumed identically by preview and live overlay (spec §3, §12: "Preview e overlay real devem compartilhar o mesmo motor de layout").
- Create: `src/IracingLiveCoach.OverlayHost/Theme/PaletteTokens.cs` — every HEX value from spec §16 as named constants, nothing hardcoded elsewhere.

**Acceptance:** A geometry test renders the three mandated presets (1 GTP+5 GT3, 5 SF23, Relative 7-row) and asserts physical width/height against the §17 limits (`floor(0.25 × width)`, `floor(0.35 × height)`) at 100/125/150% DPI.

## Phase 3 — Standings + Relative (scoped)

**Files:**
- Create: `src/IracingLiveCoach.OverlayHost/Widgets/StandingsWidget.cs`, `RelativeWidget.cs`.
- Reuse: `LiveCoachEngine.cs`, `ComputeLivePositions`/`UpdateStandings`/`UpdateRelative` from the extracted Core telemetry logic — no recalculation logic duplicated in the render layer.

**Acceptance:** iRating+Δ combined badge, lap-delta-vs-player, OT balance+bar (200s scale, states modeled separately), class color strip keyed by ClassId — each independently verifiable against spec §6/§7 acceptance bullets.

## Phase 4 — Weather, Fuel, Radar, Start Helper (scoped)

**Files:** `src/IracingLiveCoach.OverlayHost/Widgets/WeatherWidget.cs`, `FuelWidget.cs`, `RadarWidget.cs`, `StartHelperWidget.cs`.
Radar/Start Helper reuse the *data* from `RadarWidgetViewModel`/existing calibration logic; drawing is new (Direct2D), replacing `DynamicWidgetSurface`'s WPF `OnRender` with the Phase 2 layout engine.

**Acceptance:** Radar shows symbolic left/right/both indicators only if telemetry supports it (no fabricated XY positions per spec §10); Start Helper calibration/profiles preserved from current `RadarWidgetViewModel`/Start Helper logic.

## Phase 5 — Control Center V3 (scoped)

**Files:** New WPF project `src/IracingLiveCoach.ControlCenter/` (kept separate from the render-critical path per spec §3), tabs: Layout, Cabeçalhos, Colunas, Aparência, Cores, Regras, Perfis — talking to OverlayHost over the Phase 1 IPC channel for live, no-restart updates.

## Phase 6 — Profiles & Persistence (scoped)

**Files:** Extend `src/IracingLiveCoach.Core/WidgetLayoutStore.cs` (already exists) with versioned migrations, atomic writes, import/export, undo/redo stack (spec §3, §12).

## Phase 7 — Validation, Benchmarking, Packaging (scoped)

**Files:** `scripts/publish-v3.ps1` (parallel to existing `scripts/publish.ps1`, publishing to `%LOCALAPPDATA%\IracingLiveCoachV3\` so V2 and V3 can run side-by-side without collision). Benchmark harness logging frame latency/CPU/GPU/memory per spec §13, captured on the reference hardware.

---

## Execution Handoff

Phase 0 is the next actionable work and is fully detailed above. Two options:

1. **Subagent-Driven (recommended)** — dispatch a fresh implementer subagent for Phase 0's task, review the diff, then decide with you whether to write Phase 1's detailed plan.
2. **Inline Execution** — implement Phase 0 directly in this session with checkpoints.

Both stop at the Phase 0 findings doc for your sign-off before Phase 1 gets its own detailed plan — per the spec's own sequencing (§14, step 2 gates step 3).
