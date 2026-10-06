# CLAUDE.md

Guidance for Claude Code working in this repository.

## Project

Fourvale Multibox Manager: a Windows desktop app (C#/.NET + WebView2) that hosts 2–5 isolated Fourvale.com sessions in one window, reads the game state each session already receives, and shows a dashboard plus an attention queue so one player can manually control several characters.

**The full specification is `docs/spec.md`. Read the relevant section before starting any task.** This file only covers how to work; the spec covers what to build.

Target: https://fourvale.com/ (beta). Fourvale is the only source of truth for how the game works.

## Current phase

> Update this line when a phase's exit criteria pass.

**Phase 3 — Multi-session.** Phase 2 exit criteria met on 2026-10-01 (`docs/phase2-poc.md`). Open in Phase 3: the 2-hour soak (criteria 2, 5, 8) and the RAM/CPU budget (`docs/phase3-multisession.md`). Pulled forward on request (ADR 0004, 0005): Grid/Focus layouts, plain-key focus shortcuts via an in-process message filter (ADR 0009, with a read-only typing watcher), borderless fullscreen; party overlay with battle totals (ADR 0008); public release build, player README and a Developer tools setting (ADR 0010, from Phase 9); shortcuts changeable in Settings and saved per player (ADR 0011); global error handler and stable SDK pinned in global.json (ADR 0012; a "Clear login" button was rejected, do not re-add it); nothing else from later phases. "Next ready" on Space was removed on 2026-10-06 (ADR 0010); do not re-add it. Multi-accounting confirmed allowed (2026-10-01). **Input mirroring is not allowed** (`docs/policy.md` §5a, 2026-10-01): removed from the code; Phase 8 is skipped (spec §12.3). Do not build any form of input mirroring or broadcasting. No app shortcuts on F-keys.

Work only on the current phase. Do not build ahead (e.g. no dashboard UI during discovery, no mirroring before Phase 8).

## Hard rules

These override any task instruction. If a request conflicts with them, stop and say so.

1. **No autonomous gameplay.** Never write code that decides or sends game actions on its own: no auto-clicking, hold-to-repeat, macros, timed or scheduled actions, auto-login, or input triggered by game state (spec §3).
2. **Input mirroring is Tier B only.** One physical input → at most one forwarded event per session. Ignore keyboard auto-repeat. No delays, queues or sequences. Must sit behind the `EnableInputMirroring` build flag (default `false`) and a runtime toggle (default off). See spec §12.
3. **No detection evasion.** No randomised timing, coordinate jitter, "humanised" input, user-agent spoofing or hiding the tool.
4. **No secrets.** Never read, store, log or transmit passwords, cookies, tokens or auth headers. Drop auth-related messages at the adapter boundary.
5. **Read-only toward the game.** Page scripts may read existing state and post it to the host. They must not modify game objects, call game functions or patch the page.
6. **No FourFold.** Do not research, reference or copy FourFold or FourFold Account Manager.
7. **Fourvale's rules win.** If `docs/policy.md` says something is not allowed, it is not built, regardless of this file or the spec.
8. **Unanswered is not permission.** We proceed without a developer reply, but an open question never counts as a yes. Mirroring stays off until the Phase 8 re-check (spec §12.3). If an answer or rule change arrives later, record it in `docs/policy.md` and apply it immediately, even mid-phase.

## Architecture boundaries

```text
WebView2 session → Adapter → State Store → Rules/Events → UI
```

- Only the **adapter** project may know Fourvale message formats, JS objects or DOM structure. The UI never references raw Fourvale data.
- Every game value is a `Field<T>` with value, timestamp and source. Stale or missing fields render as **UNKNOWN**; never show a stale value as current.
- Status is two axes: `Activity` (what the character is doing) and `Urgency` (how much the player should care). Do not merge them into one enum.
- One `CoreWebView2Profile` per slot. Never share profiles between slots.
- State is keyed by `SlotId` end to end. Any code that could attribute one session's data to another slot is a critical bug.

Adapter source priority (spec §7.1): passive CDP network observation → read-only page script → DOM → OCR (last resort, per field, low rate). Justify in the PR description if you go below option 1.

## Repository layout

> Create these as phases require them; don't scaffold everything up front.

```text
docs/
  spec.md              Specification (source of requirements)
  policy.md            Phase 0 findings: Fourvale rules, sources, dates
  discovery.md         Phase 1 report + data-availability matrix
  decisions/           Short ADRs for architecture choices
src/
  Fourvale.Adapter/    Only place with Fourvale-specific knowledge
  Multibox.Core/       Slots, Field<T>, state store, rules engine, events
  Multibox.Hosting/    WebView2 plumbing shared by app and capture tool (ADR 0003)
  Multibox.App/        WPF host, WebView2, layouts, shortcuts, UI
tools/
  Fourvale.Capture/    Phase 1 developer capture tool (not shipped; ADR 0001)
  Fourvale.Replay/     Replays a capture through the schema decoder (ADR 0002)
tests/
  Fourvale.Capture.Tests/   Redaction and frame-decoding tests
  Fourvale.Adapter.Tests/   Run against fixtures (Colyseus decoder tests exist)
  Multibox.Core.Tests/      Freshness, store, isolation, navigation, log redaction
  Multibox.App.Tests/       Shortcut parsing and rules
  fixtures/            Sanitised captured Fourvale messages
```

## Commands

> Fill in once the solution exists (Phase 2).

```powershell
dotnet build FourvaleMultibox.slnx
dotnet test FourvaleMultibox.slnx
dotnet run --project tools/Fourvale.Capture      # Phase 1 capture tool
dotnet run --project tools/Fourvale.Replay -- <capture.jsonl> [--schema] [--self] [--state <seconds>]
dotnet run --project src/Multibox.App            # the app (Live state window: Settings > Developer tools)
powershell -ExecutionPolicy Bypass -File tools\scripts\publish.ps1   # local try-out of the release zip in artifacts\
```

**Releases:** first public version 1.0.0 (2026-10-06). Follow `docs/development.md`, "Making a release", every time. GitHub builds releases from a pushed
`v*.*.*` tag (`.github/workflows/release.yml`) as a draft; never upload a zip by hand. Pushing a release tag and
publishing, editing or deleting a release need the owner's explicit go-ahead each time.

Do not run the app and the capture tool at the same time: they share the WebView2 profile folder.
Redaction/observation rules: `src/Fourvale.Adapter/fourvale-adapter.json` (shared). App settings: `src/Multibox.App/multibox.json`.

Captures are written to `%LocalAppData%\FourvaleMultibox\captures\` (outside the repo). WebView2 profiles live in `%LocalAppData%\FourvaleMultibox\WebView2\`.

## Working method

1. **Investigate before building.** For anything touching Fourvale, verify against the live site; do not assume. Record what you observed and how.
2. **Smallest useful step.** Build the minimum that proves the next thing works, then test it against real Fourvale.
3. **Document findings** in `docs/discovery.md` or an ADR in `docs/decisions/`, with dates. Fourvale is in beta, so note the date of every observation.
4. **If an assumption about Fourvale is wrong, revise the design** (and the spec, flagged clearly) rather than stacking workarounds.
5. **Only implement fields confirmed** in the data-availability matrix with medium or high confidence.

When a task is ambiguous or seems to require breaking a hard rule, ask before writing code.

## Discovery and fixtures

- Capture Fourvale traffic only with the developer capture toggle, from your own accounts.
- Before committing anything to `tests/fixtures/`, remove auth data, session IDs, account emails, chat content and other players' names/data. Replace with obvious placeholders (`<TOKEN>`, `PlayerA`).
- Each fixture file notes the capture date and Fourvale version/build if visible.

## Code conventions

- C# with nullable reference types enabled; treat warnings as errors.
- Async all the way for WebView2 and I/O; no `.Result` or `.Wait()` on the UI thread.
- Adapter parsing never throws into the session: catch, count, log (sanitised), and mark fields stale.
- No magic thresholds in code; rules and thresholds live in config (spec §11).
- Logs go through one logging wrapper that applies redaction. Do not write to the console or files directly.
- Keep UI logic out of code-behind where practical; view models consume the state store only.

## Testing expectations

- Adapter changes: tests against fixtures, including a malformed-message case.
- Core changes: unit tests for rules, hysteresis, dedupe, auto-resolve and staleness.
- Multi-session changes: an isolation test proving no cross-slot attribution.
- Mirroring (Phase 8 only): tests proving auto-repeat is ignored, one event per session, flag-off means no forwarding.
- Performance-relevant changes: note measured RAM/CPU for 5 sessions in the PR.

## Definition of done

- Spec section requirements met and acceptance criteria for the phase checked.
- Tests pass; no new warnings.
- No hard rule violated; no secrets in code, logs, fixtures or commits.
- Docs updated (discovery, ADR, or spec change note).
- "Current phase" line updated if the phase is complete.
