# ADR 0008 — Party overlay (dashboard elements pulled forward)

- **Date:** 2026-10-01
- **Status:** Accepted (player request)
- **Phase:** pulled forward from Phase 4 (Dashboard) into Phase 3, like ADR 0004

## Context

The player asked for "a nice overlay that is transparent, can be closed, can be minimized and moved around", showing every account's character "across their battles and stuff".

## Decision

`OverlayWindow`: borderless, transparent, always on top. It is non-activating (`WS_EX_NOACTIVATE`), so clicking or dragging it never takes keyboard focus from the game window; the keyboard shortcuts only work while the game window is in front.

- **Controls:** drag the header to move it; **–** minimises it to a header with a summary (who is READY, how many in battle, problems); **✕** closes it; **Overlay** in the top bar brings it back. Clicking a character row focuses that slot.
- **Per slot:** slot number, character, class · level, READY badge, HP and SP bars (value / max in battle; HP from `hpSync` without a bar outside battle), the action meter, then either "In battle · N enemies left · X% HP" or the location, and the session totals "W won · L lost · +XP · +silver".
- **New data (adapter):**
  - `EnemiesAlive` and `EnemyHpFraction` from the battle room's `combatants` with `isPlayer == false`, re-confirmed like other room-state fields.
  - `BattleStats` (won, lost, XP, silver) from `battleOver` (discovery C3). Kept across page reloads; started again when a different character appears in the slot.
  - Verified on capture 1: enemies count down 3→0 with HP 100%→0%, and the totals match the game's rewards (first battle +1,099 XP, +276 silver).
- **Rules kept:** reads the state store only; UNKNOWN when not current (spec §6.1); no game input.
- **Saved:** visible, minimised and position (in `app-settings.json`, no game data). Background opacity is `overlayOpacity` in `multibox.json`.

## Update 2026-10-01: three bars only

At the player's request each row shows only the slot number and character name plus three bars: **HP**, **SP** and **Timer**. The timer is the action meter as a bar with the seconds until the character can act, `(1 - actionMeter) x attackRateMs`, or READY. Outside battle the timer reads "not in battle". Class, level, battle text and session totals are no longer shown in the overlay; the data stays in the store, and the totals are still in the Live state window's data.

## Update 2026-10-01: frameless, header on hover

No outer frame and no side borders; each row has its own translucent background, and the gaps between rows are click-through. The header (Party, summary, minimise, close) has only a top edge line and is invisible and click-through until the mouse is over the overlay's area; it always shows while minimised (there are no rows to hover then). Hover is decided by comparing the cursor position with the window rectangle every 100 ms, because WPF's IsMouseOver is unreliable for a window that never activates. Row states without side borders: focused = lighter row, ready = the timer's READY, problem = red name.

## Update 2026-10-01: click-through and battle-only, set in Settings

The overlay caught clicks meant for the game. Now:

- **Click-through** (default on): the window gets `WS_EX_TRANSPARENT`, so every click passes to the game. Header and buttons cannot be used then, and hover never shows the header. Unticking it in Settings is **arrange mode**: the overlay is always shown with its header, can be dragged, and is expanded.
- **Show:** *Only in battle* (default; shown while at least one character has a current InBattle = true), *Always*, *Off*. Rule in `OverlayVisibility.ShouldShow`, unit-tested. The overlay's close button sets *Off*.
- **Only list characters that are in battle** (optional) hides the other rows.
- **Background opacity** slider (20–100%).
- All of it is saved in `app-settings.json` with the position; settings saved by the earlier version still load (tested). The top bar's **Overlay** button opens Settings.

## Tests

`FourvaleSessionReadTests`: enemies and their HP share from the battle room; battle results add up once per slot; totals survive a reload and restart for a different character.
