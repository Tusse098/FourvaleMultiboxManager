# ADR 0001 — Phase 1 capture tool

- **Date:** 2026-10-01
- **Status:** Accepted
- **Phase:** 1 — Discovery

## Context

Phase 1 needs real Fourvale traffic from the player's own accounts to build the data-availability matrix and the test fixtures (spec §17). The player chose a small WebView2 capture tool over watching a normal browser, and asked that it be easy to use.

## Decision

Build `tools/Fourvale.Capture`: a WPF app (.NET 10, WebView2 1.0.4258.31) that hosts one Fourvale session and records sanitised network traffic to a local JSON Lines file.

- **Source:** passive DevTools protocol network observation (spec §7.1 option 1). It enables the `Network` domain and reads WebSocket frames and API responses. It injects no page script, sends no game input and patches nothing.
- **Isolation:** one `CoreWebView2Profile` per slot (`slot1`…`slot5`), with a shared user-data folder (spec §5.2). Switching slots is locked while recording.
- **Capture is off on every start** (spec §14). Nothing is written until the player presses Start.
- **Redaction happens before anything reaches disk:**
  - Request headers and request bodies are never read. The bearer token lives there, including in the `/matchmake` body.
  - Response bodies of auth paths (`/api/login`, `/register`, `/trial`, `/2fa`, `/password`, `/logout`, plus a pattern) are never requested.
  - JSON keys matching a secret pattern (`*token`, `password`, `email`, `cookie`, `authorization`, …) are replaced, at any depth.
  - E-mail addresses and JWT- or bearer-shaped strings inside free text are replaced.
  - URL query values are replaced; parameter names are kept.
  - The reconnection token at the start of the Colyseus `JOIN_ROOM` frame is skipped without being decoded.
  - Chat-type messages (`chat`, `chatMsg`, …) lose `text`, `from`, `to` and similar fields.
  - Frames that fail to decode are stored by size only.
- **Rules live in `capture-rules.json`**, not in code. The tool refuses to start if the redaction patterns are missing.
- **Output** goes to `%LocalAppData%\FourvaleMultibox\captures\`, outside the repo. `*.jsonl` is git-ignored except under `tests/fixtures/`.
- **Markers:** preset and free-text markers let the player timestamp in-game events so that messages can be matched to what happened.

## Update 2026-10-01: several slots at once

The first version showed one slot at a time and disposed the previous game view when switching, which disconnected it. The player asked to run slots side by side to test multi-accounting (spec §17 Q8). Changes:

- Each open slot has its own `SlotPanel` (WebView2, profile `slotN`), `NetworkObserver` and `SlotCapture`. The observer feeds only its own slot's capture, so one account's data cannot reach another account's file. `SlotIsolationTests` covers this.
- Layouts: **Focus**, **Side by side** and **Grid**. The first Focus version hid the other slots, which paused their games (finding H1). Focus now shows the active slot large and the others as a live strip below it; no layout hides a running slot. All slots start with `browserArguments` from `capture-rules.json` (Chromium background-throttling switches).
- Start/Stop records every open slot, one file per slot. Markers go to the active slot. Opening and closing slots is locked while recording.
- Open slots, active slot, layout and the reload option are remembered in `%AppData%\FourvaleMultibox\capture-tool.json`. No game or account data is stored there.

This is still a discovery aid. It has no dashboard, no shortcuts and no state parsing. The Phase 3 app is built separately in `src/`.

## Update 2026-10-01: live decoded state

The sidebar has a **Decoded state** view for the active slot. It shows room, class, level, HP, SP, action meter (READY at 1), whether the character is in battle, the decode error count and the raw room-state JSON (own sessionId shown as `@me`). It is fed by `LiveState`, which uses the same redacted records as the capture file plus `RoomTracker` from `Fourvale.Adapter` (ADR 0002), so it never sees raw frames or tokens. It runs whether or not the slot is recording. Values that are not available show UNKNOWN; HP outside battle is labelled "sent on room join". This is a debug view for discovery, not the Phase 4 dashboard.

## Update 2026-10-01: shared code (Phase 2)

The tool's network observation, redaction and decoding now live in `Fourvale.Adapter` and `Multibox.Hosting` (ADR 0003); the tool references them instead of keeping its own copies. Redaction rules moved from `capture-rules.json` to `fourvale-adapter.json`. Browser shortcuts are disabled on its game panels too.

## Placement

The tool lives under `tools/`, not `src/`, because it is a developer tool and not part of the app pipeline. It knows the generic Colyseus framing, which is a library protocol, but it knows nothing about Fourvale game semantics. When Phase 2 builds `Fourvale.Adapter`, the frame decoder and redactor move there (or are referenced from there). The tool must not grow game-specific parsing.

## Consequences

- Room state (`ROOM_STATE`, `ROOM_STATE_PATCH`) is Colyseus schema binary and is stored as base64. Decoding it needs the schema handshake from `JOIN_ROOM`, which is why "Reload game on start" is on by default.
- Captures are redacted but still contain other players' names and session ids. They must be sanitised again before they become fixtures (CLAUDE.md, "Discovery and fixtures").
- Tests: `tests/Fourvale.Capture.Tests` (39 tests) cover secret-key redaction, chat redaction, URL sanitising, auth-path detection, `JOIN_ROOM` token removal, malformed frames, and a 5,000-frame random input test that must never throw.
