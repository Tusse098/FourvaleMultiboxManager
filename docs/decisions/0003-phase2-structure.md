# ADR 0003 — Phase 2 structure: WPF host, adapter boundary, field freshness

- **Date:** 2026-10-01
- **Status:** Accepted
- **Phase:** 2 — Proof of concept

## Context

Phase 1 is complete (`docs/discovery.md`). Phase 2 must show one confirmed field updating live outside the game view, with throttling tested while the panel is hidden (spec §16). Spec §5.2 leaves WPF vs WinUI 3 open until Phase 2.

## Decisions

### 1. Host: WPF

The capture tool ran three WebView2 profiles side by side in WPF and five in Grid without lag (discovery M1, R4). `AcceleratorKeyPressed` is available on the WPF WebView2 control for global shortcuts later. WinUI 3 offers nothing we need and adds packaging work. **WPF (.NET 10).**

### 2. Projects

| Project | Target | Role |
|---|---|---|
| `src/Fourvale.Adapter` | net10.0 | Everything Fourvale-specific: `Network/` (DevTools event handling, redaction, frame decoding), `Colyseus/` (schema v2), `FourvaleSession` (turns observed traffic into Core field updates). Platform-neutral: it talks to the browser through `IDevToolsChannel`. |
| `src/Multibox.Core` | net10.0 | `SlotId`, `Field<T>`, `CharacterState`, `StateStore`, freshness rules, logging wrapper. No Fourvale knowledge. |
| `src/Multibox.Hosting` | net10.0-windows | WebView2 plumbing shared by the app and the capture tool: `WebView2DevToolsChannel`, profile creation (one `CoreWebView2Profile` per slot, browser arguments, browser accelerator keys off). |
| `src/Multibox.App` | net10.0-windows (WPF) | The application. |
| `tools/Fourvale.Capture` | net10.0-windows | Discovery tool; now uses the adapter's network code instead of its own copy. |

Dependencies: `Adapter → Core`; `Hosting → Adapter`; `App → Hosting, Adapter, Core`. The UI never reads raw Fourvale data; it reads `StateStore`.

The redaction and observation rules move from the capture tool's `capture-rules.json` to the adapter's `fourvale-adapter.json`, so the app and the tool cannot drift apart. `capture-rules.json` keeps only tool settings (markers, slots, layout).

### 3. Field freshness by source

A field's `ConfirmedAt` is the last time the adapter **verified** the value from a live source, not the last time it changed:

- **Room state** (class, level, battle HP/SP, action meter, location, in-battle): re-confirmed on every adapter read while the room's socket is open and its state decodes. If the socket closes, decoding fails or the page reloads, confirmation stops and the field goes UNKNOWN after the `room-state` limit.
- **`hpSync`** (HP/SP outside battle): confirmed when the message arrives on room join and never re-confirmed in between, so it has its own, longer limit.

The limits live in `multibox.json` (`freshness`), not in code (CLAUDE.md "no magic thresholds").

### 4. Phase 2 display

A separate always-available **Live state** window shows each field with value, age, source and UNKNOWN when stale, plus adapter health, updates per second and browser memory/CPU. A **Hide game panel** switch runs the throttling test. This is a proof-of-concept display, not the Phase 4 dashboard.

## Consequences

- One shared, tested network path for the app and the capture tool.
- The adapter can be tested without a browser by feeding DevTools event JSON through a fake `IDevToolsChannel`.
- Phase 2 runs one slot. Its WebView2 data folder and `slot1` profile are the same as the capture tool's, so an existing login is reused. Do not run the app and the capture tool at the same time.
