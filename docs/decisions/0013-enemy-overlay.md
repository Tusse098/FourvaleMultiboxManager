# ADR 0013 — Enemy overlay and overlay size

- **Date:** 2026-10-13
- **Status:** Accepted (player request)
- **Phase:** 3. Dashboard element pulled forward from Phase 4, like ADR 0008.

## Context

The player asked for an overlay of the enemies of the focused window, built like the party overlay, showing only
each enemy's HP "at the corresponding positions" (front row above back row, top before bottom), toggleable, and
for the size of both overlays to be changeable.

## Decision

**Enemy overlay.** `EnemyOverlayWindow`: a second window with the party overlay's structure (frameless, one
translucent row per enemy, header only while arranging). Each row is one HP bar with `hp / max`; no name, SP or timer.

- **Whose enemies:** the focused slot's battle. Changing focus refreshes the list at once, so one slot's enemies
  never stay under another slot's focus.
- **Data (adapter):** `CharacterState.Enemies`, a list of `EnemyState(Hp, MaxHp, Alive)` from the battle room's
  `combatants` with `isPlayer == false` (discovery E1, E2). Passive network observation, the same source as
  `EnemiesAlive`. An enemy without usable HP is left out, never shown with a made-up value.
- **Order:** `row == "front"` first, every other row after it, and map order (`enemy_0`, `enemy_1`, …) within a
  row. Only `"front"` has been observed (E3), so the back row is not named in code. That map order is top to
  bottom on screen is an assumption until the player has checked it (E4).
- **Defeated enemies** keep their row, dimmed at `0 / max`, so the other rows do not move.
- **Shown** while the focused slot has a current enemy list; always while arranging; never when turned off.
  Rule in `OverlayVisibility.ShouldShowEnemies`. Not current means no rows (spec §6.1), never a stale value.
- **Rules kept:** reads the state store only; no game input; nothing is drawn into or read from the page.

**Size.** Each overlay has a *Size* slider in Settings (50–200 %, steps of 10) that scales the whole overlay, text
and bars together (`LayoutTransform`). It works while click-through is on. While arranging (click-through off)
each overlay also shows a corner grip: dragging it resizes that overlay in whole percent within the same range,
and the slider follows. The size is saved when the mouse is released.

**Settings** (saved in `app-settings.json`, no game data):

| | Party overlay | Enemy overlay |
|---|---|---|
| On/off | *Show*: only in battle, always, off | *Show enemy HP* checkbox (default on); the overlay's ✕ unticks it |
| Size, position | own | own |
| Click-through, background opacity | shared | shared |

Settings saved by 1.0.0 load with size 100 % and the enemy overlay on (tested).

**Code.** The window behaviour both overlays need (non-activating, click-through, hover, drag, hide instead of
close) moved from `OverlayWindow` into `OverlayWindowBase`; the shared view-model part is `OverlayViewModelBase`.

## Update 2026-10-13: after the first live try

The player reported UNKNOWN rows and a dead timer in battle. Two recorded battles (capture 1 alone, capture 3 as
a two-character party) replayed in real time into spare slots showed HP, timer and enemy rows correctly, with no
UI-thread lag, so the overlay change was not the cause. The cause was a Fourvale client update the same day: the
battle seat now arrives as `startBattle.reservation` instead of a matchmake response (discovery S1–S4). The
adapter now reads the seat from the reservation, confirmed in live battles the same evening; 1.0.0 has the same
fault and needs a release with this fix. Found and changed on the way:

- The enemy list is dropped as soon as a town or dungeon room is current again, instead of lingering for the
  freshness limit after the battle.
- The enemy overlay's first position is to the right of the party overlay at its current size (it overlapped).
- Each slot logs a page navigation and a press of its reload button.
- Each slot logs where its fields come from when that changes, e.g. `slot1: reading battle room, own entry found,
  2 open` (`FourvaleSession.DescribeSource`; room kinds only). A row that shows UNKNOWN in battle can then be
  explained from the log: no room state, or the own entry missing.

## Not done

- No toggle shortcut; the checkbox is the toggle.
- HP labels drawn over the enemy sprites: the game data has no screen positions.

## Tests

`FourvaleSessionReadTests`: front row first then map order, players and enemies without HP left out, a defeated
enemy kept; two slots in different battles each list only their own enemies. `StateStoreTests`: the list goes
UNKNOWN when stale and is forgotten on reset. `OverlayVisibilityTests`: when the enemy overlay shows; old settings
load; sizes round-trip and are clamped; the size for a dragged width; both overlay windows build from their XAML.
`FourvaleSessionReadTests` also covers the seat from `startBattle.reservation` and a malformed reservation.

## Open

Player check in a live battle: rows match the game top to bottom, and a battle with a back row sorts it below
the front row (E3, E4). Dragging the corner grip has not been tried by hand yet. 5-slot RAM/CPU with the extra
window has not been measured.
