# ADR 0006 — Click mirroring (Tier B) pulled forward

- **Date:** 2026-10-01
- **Status:** **Superseded and removed 2026-10-01.** Mirroring is not allowed (the Fourvale creator, 2026-10-01). All mirroring code (`MouseMirror`, `MouseHook`, `CanvasWatcher`, `DispatchMirroredMouse`), the `EnableInputMirroring` build flag, the settings section, the indicators and `start-with-mirroring.cmd` were deleted; no hidden way to re-enable it remains (spec §12.3, hard rule 7). Kept as history. Re-verified 2026-10-06: no Ctrl+click or mirroring code in the repository (ADR 0010).
- **Phase:** pulled forward from Phase 8 into Phase 3 at the player's request.

## Context

The player asked: hold Ctrl and click, and the same position (relative to each game's size) is clicked in the other slots. That is input mirroring (spec §3.2 Tier B, §12.2). Hard rule 8 kept it off until the Phase 8 re-check. Fourvale's position is still unknown: there is no published rule (F1), no answer to developer question Q3, "cheating" is undefined (G3) and bans exist (F2). The player was given three choices: ask Fourvale first (recommended), accept the risk, or drop it. The player chose to **accept the risk**, with a settings window that switches it off completely. (That record was removed from `docs/policy.md` on 2026-10-06, after the question was settled.)

## Decision

Click mirroring is built with every spec §12.2 and §12.3 safeguard:

| Requirement (spec / hard rule 2) | Implementation |
|---|---|
| Build flag `EnableInputMirroring`, default **false** | MSBuild property in `Multibox.App.csproj` → `ENABLE_INPUT_MIRRORING`. Without it the build contains no input-dispatch code (verified: `Input.dispatchMouseEvent` is absent from the default `Multibox.App.dll`). Start with: `dotnet run --project src/Multibox.App -p:EnableInputMirroring=true` |
| Runtime switch, off by default | Settings window → Click mirroring ON/OFF. Off on every start and switched off on exit; OFF stops all forwarding immediately. |
| Clear on-screen indicator | Red **MIRRORING ON** badge in the top bar; red **MIRROR** badge on each participating slot. |
| Per-slot opt-in | Settings → "Slots that take part" (open slots only; nothing ticked by default). |
| One physical input → exactly one forwarded event per session | `MouseMirror`: one press → one `mousePressed` per other participating slot; its release → one `mouseReleased` to the same slots. Release is always forwarded, so no target is left with a held button. |
| No auto-repeat, hold-to-repeat, delays, queues, sequences | Mouse only (no keyboard mirroring). Events are dispatched as they happen; nothing is stored or replayed. |
| No randomisation or jitter | Positions are exact: normalised to the source canvas, scaled to each target canvas. |
| Never triggered by game state | Only the player's physical left button with Ctrl held. Synthetic (injected) events are ignored (`LLMHF_INJECTED`). |
| Coordinates relative to the game canvas; refuse on mismatch | The canvas rectangle is reported by `CanvasWatcher` (read-only page script). Refuses a slot whose canvas aspect differs by more than `mirroring.aspectTolerance` (0.02) and shows why. |
| Implementation via CDP input dispatch | `SlotSession.DispatchMirroredMouse` → `Input.dispatchMouseEvent` on that slot's own WebView2 (no cross-slot channel). |

Other details:

- The source slot receives the player's real click unchanged; the mouse hook observes and never swallows or creates OS input.
- In the Focus layout a Ctrl+click on a small tile does not enlarge it, so canvases do not move mid-click.
- Each mirrored click is logged (`mirrored click from slotN to …`) for the record.

## Kill switch obligations (spec §12.3)

- If Fourvale prohibits it, ship with the flag off and **remove** the code. Do not leave a hidden way to re-enable it.
- ~~Send developer question Q3 and check the Discord rules on tools and broadcasting.~~ Settled 2026-10-01: not allowed.

## Tests

`MouseMirrorTests` (10): off by default and cannot be enabled without the flag; the default build excludes it; relative mapping, one event per slot; release once to the same targets, even after Ctrl is released; no Ctrl, no mirroring; injected input ignored; opt-in only; clicks outside the canvas refused; shape mismatch refused; switching off drops the pending release.
