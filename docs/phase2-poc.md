# Phase 2 — Proof of concept: verification

Spec §16 exit criteria: **one confirmed field updates live outside the game view; throttling tested with the panel hidden.** Structure: ADR 0003.

## What exists (2026-10-01)

- `src/Multibox.App`: one slot (`slot1` profile, shared with the capture tool) and a separate **Live state** window. The window shows Character, Class, Level, HP, Max HP, SP, Max SP, Action (READY at 1), In battle and Location, each with its age and source. Missing or stale fields show UNKNOWN (freshness limits in `multibox.json`). It also shows adapter health, state updates/s, decode errors and browser memory/CPU.
- Pipeline: WebView2 → `NetworkObserver` (adapter) → `FourvaleSession` (adapter) → `StateStore` (core) → `LiveStateViewModel`. The UI reads only the store.
- Game panels have browser shortcuts (F3, F5, Ctrl+F…) disabled (discovery R3).
- Logs: `%LocalAppData%\FourvaleMultibox\logs\app-YYYYMMDD.log`, written through the redacting `Log` wrapper.
- Tests: 7 core (freshness, store, log redaction), 79 adapter (including `FourvaleSessionReadTests`: town and battle fields, UNKNOWN after the room closes, slot isolation), 3 capture tool.
- Smoke test by Claude (2026-10-01): the app started on an unused profile (`slot9`, not logged in). Both windows rendered and all fields showed UNKNOWN with health "Waiting". No account was used.

## Player tests

Close **Fourvale Capture** first: both apps use the same profile folder. Then run `dotnet run --project src/Multibox.App`.

| # | Test | Expected | Result |
|---|---|---|---|
| P1 | Log in (slot 1 is probably still logged in) and walk around town | Class, Level, Location fresh (age < 1 s, source `room-state`); HP/SP from `hpSync`; Max HP UNKNOWN | Works as intended (player, 2026-10-01). Log: adapter health Ok, 0 decode errors |
| P2 | Start a fight | HP/Max HP/SP live per hit; Action fills to READY and the gold "READY TO ACT" badge shows; In battle = Yes | Works as intended (player) |
| P3 | Switch class | Class and Level change after the room rejoin | Works as intended (player) |
| P4 | During a fight, **Hide game panel** for ~30 s, then show it | Record: did state updates/s continue? Did fields stay fresh? Did the fight continue? (Discovery H1 expects the game to pause when hidden.) | Works as intended (player). Log: panel hidden 3 times (~5 s, ~13 s, ~6.5 s); adapter stayed Ok with 0 decode errors. Hidden periods were shorter than the suggested 30 s |
| P5 | With the capture tool closed, note the **Browser** line after a few minutes | RAM/CPU for one slot (baseline for spec §15 criterion 8) | **Not recorded.** Carried into Phase 3 (5-session measurement, criterion 8) |

## Outcome

**Phase 2 exit criteria met on 2026-10-01** (player-reported, supported by the app log):

- *One confirmed field updates live outside the game view:* the Live state window showed the slot's fields from the state store (P1–P3).
- *Throttling tested with the panel hidden:* P4. The adapter stayed Ok with no decode errors during the hidden periods. What the game itself did while hidden (paused or not) was reported only as "as intended" and not measured in detail; the periods were short (≤13 s).

Carried forward: the RAM/CPU baseline (P5), now measured with 5 sessions in Phase 3.
