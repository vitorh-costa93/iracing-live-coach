# V3 Phase 0 — GPU Technical Proof: Findings

Ran against the plan's [Phase 0 steps](../plans/2026-09-17-v3-overlay-rearchitecture-plan.md#phase-0--gpu-technical-proof) on 2026-09-17. Project: `src/IracingLiveCoach.OverlayHost` (net9.0-windows).

## Package versions used

- .NET 9 SDK 9.0.318 (installed via `winget install --id Microsoft.DotNet.SDK.9`, user-confirmed).
- `Vortice.Win32.Graphics.Direct3D11` 2.5.0
- `Vortice.Win32.Graphics.Dxgi` 2.5.0
- `Vortice.Win32.Graphics.Direct2D` 2.5.0
- `Vortice.Win32.Graphics.DirectWrite` 2.5.0
- `Vortice.Win32.Graphics.DirectComposition` 2.5.0

These are raw, unsafe, Win32-metadata-generated bindings (COM vtables via function pointers, `ComPtr<T>`, `__uuidof<T>()`) — not the older managed-friendly Vortice API, which (confirmed in Step 1) has no DirectWrite bindings at all. There is no working DirectComposition/window sample in the upstream repo (`amerkoleci/Vortice.Win32`, only a `01-ClearScreen` D3D11/D3D12/WIC sample exists) — all DirectComposition/D2D/DirectWrite call sequences here were built by reading the generated interface source directly (exact method signatures fetched from GitHub, not guessed) and verified by compiling and running, not copied from a working reference.

## Step 5 — Real per-pixel transparency: **CONFIRMED**

Screenshot evidence (`phase0-proof-screenshot.png`, kept locally in the session scratchpad, not attached here because it also captured unrelated windows in the background): the overlay's white "V3 PROOF" text and a cyan dot render directly over other running windows, with the desktop/other windows fully visible through every transparent pixel around the text — no solid black box, which is exactly the failure mode the earlier `DwmExtendFrameIntoClientArea` (Aero Glass) attempt hit. This is genuine DirectComposition compositing, not `UpdateLayeredWindow` software blending.

Key implementation choices that made this work:
- `WS_EX_NOREDIRECTIONBITMAP` on the window (avoids allocating the legacy GDI redirection surface WPF's layered windows rely on).
- Composition swap chain (`IDXGIFactory2.CreateSwapChainForComposition`) with `Format.B8G8R8A8Unorm` + `AlphaMode.Premultiplied` + `SwapEffect.FlipSequential` + `Scaling.Stretch`.
- `IDCompositionVisual.SetContent` binding the swap chain directly into the DirectComposition visual tree, committed via `IDCompositionDesktopDevice.Commit()`.
- D2D bitmap bound with premultiplied alpha (`D2DPixelFormat(Format.B8G8R8A8Unorm, AlphaMode.Premultiplied)`) matching the swap chain, per spec §19's warning about not double-multiplying alpha.

## Step 6 — Click-through toggle: **CONFIRMED**

SPACE key handler flips `WS_EX_TRANSPARENT` via `GetWindowLongW`/`SetWindowLongW` on `GWL_EXSTYLE`. Verified by remotely focusing the window and sending SPACE (`SendKeys`), then screenshotting: the on-screen label changed from "click-through: ON" to "click-through: OFF" as expected. This confirms the code path executes; a full "does a real mouse click land on iRacing instead of the overlay" test still needs a human at the mouse — flagging this as not independently verified beyond the style-flag toggle itself.

## Step 7 — DPI + multi-monitor: **PARTIAL**

Only tested at the machine's current setting: **96 DPI / 100% scale, single monitor**, confirmed via `Graphics.DpiX/DpiY`. Text rendered sharp at this scale (see screenshot). **125%/150% and a second monitor were NOT tested** — changing the user's display scaling is a system setting I did not want to change unilaterally, and no second monitor was available in this session. This is an open gap: Phase 1 (or a dedicated re-test) needs the user to either temporarily change scaling or plug in a second display, then re-run this same proof app and confirm text stays crisp (no `WM_DPICHANGED` recreation logic exists yet in this proof build — it would currently need a restart at a new DPI, which is expected and fine for a Phase 0 spike, but must be built properly in Phase 2's layout engine).

## Step 8 — Radar/Start Helper pacing: **CONFIRMED, exceeds target**

A moving dot redrawn every frame (`Present(1, ...)`, i.e. vsync-locked) logged real frame-to-frame timings to `%AppData%\iracing-live-coach\v3-phase0-pacing.log`. Representative samples (600-frame windows, ~10s each):

```
p50=6.06ms  p95=6.11ms  p99=6.13ms  max=6.51-13.98ms (occasional single-frame hitches, one 35.5ms outlier)
```

6.06ms ≈ **165Hz**, matching the reference monitor spec (§13: "165 Hz") exactly — the swap chain is presenting in lockstep with the display's real refresh rate, with p99 barely above p50 (no backlog, no growing queue, no visible stutter in the moving dot). This directly answers spec §3's concern about a "growing queue" render failure mode: none observed over ~2.5 minutes of continuous run.

## Gate decision

Per the plan: Phase 1 does not start if transparency, click-through, or text sharpness fail. All three passed. **Proceeding to Phase 1 is unblocked.**

Carried-forward gaps for Phase 1+ to close (not blockers, but tracked so they aren't lost):
- DPI-change/monitor-change recovery (`WM_DPICHANGED` handling, target/swapchain recreation) — not built in this spike, required by Global Constraints and spec §3/§19.
- 125%/150% DPI and multi-monitor testing itself — needs the user's environment, not just code.
- Device-lost recovery — explicitly deferred to Phase 1 per the plan, not attempted here.
