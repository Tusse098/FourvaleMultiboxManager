# ADR 0004 — Pull focus switching and "next ready" forward into Phase 3

- **Date:** 2026-10-01
- **Status:** Accepted (player request). Shortcut keys and window chrome amended by ADR 0005.
- **Phase:** 3 — Multi-session (soak test outstanding)

## Context

Phase 3 criteria 3/7 and the slot picker work, but the 2-hour soak (criteria 2, 5, 8) is not done. The player asked to make playing 2–3 accounts pleasant first, so the soak test is worth running in real play. CLAUDE.md says not to build ahead, so this ADR records a deliberate, limited reorder. From the offered options the player chose two: **focus + switching** (Phase 5) and **jump to next ready** (spec §3.2 Tier A, Phase 6 core). Not chosen for now: focused-slot-only audio and panel status badges.

## Decision

Pulled forward:

1. **Layouts Grid and Focus** (spec §8.1). In Focus, the focused slot is large and the others are small live tiles in a column; every slot stays visible (discovery H1). Layout and focused slot are remembered with the open slots.
2. **Focus switching:** `Ctrl+1…N` focuses a slot, `Ctrl+Tab` / `Ctrl+Shift+Tab` cycle, a header click focuses, and clicking into a game focuses that slot (in Focus layout it becomes the large one after the click completes). The focused panel has a thick blue border; ready is gold, a problem red.
3. **Next ready:** `Ctrl+Space` focuses the next slot after the current one whose action meter is fresh and full (`SlotNavigator.NextReady`). It shows "No character is ready" when none is.

Constraints kept:

- **Tier A only:** these features move keyboard focus between slots. They never send input to the game, and they act only on a key press by the player. There is **no auto-focus**, so the spec §18 auto-focus risk does not apply.
- Shortcuts come from `multibox.json` and are validated by `ShortcutMap`: Ctrl or Alt is required (plain game keys are never taken), F-keys are rejected (discovery R3), and duplicates are rejected. A matched key is swallowed; every other key passes to the game untouched. Key repeat is ignored (one press, one action).
- Not pulled forward: mirroring (Phase 8, hard rule 2), attention queue and notifications (Phase 6), the Dashboard and Compact layouts and audio routing (Phase 5), full persistence (Phase 7).

## Update 2026-10-01: Space goes to whoever acts next

At the player's request, Space (ADR 0005) now uses `SlotNavigator.NextToAct`. A READY slot comes first, cycling after the current one. If nobody is ready, it picks the slot with the lowest action timer, `(1 - meter) x attackRateMs` from `SecondsUntilReady` (ties go to the first after the current slot). With nobody in battle it shows "No character is in battle". The overlay's timer uses the same calculation. Still Tier A: it moves focus only, on the player's key press.

## Consequences

- Phase 3 stays the current phase until the soak test passes.
- Spec §15 criterion 3 (< 100 ms focus switch) can be measured early; formally it remains a Phase 5 criterion.
- Tests: `SlotNavigatorTests` (Core), `ShortcutMapTests` (`tests/Multibox.App.Tests`).
