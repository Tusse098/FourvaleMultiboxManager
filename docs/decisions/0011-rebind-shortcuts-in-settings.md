# ADR 0011 — Change shortcuts in Settings

- **Date:** 2026-10-06
- **Status:** Accepted (player request)
- **Amends:** ADR 0005 (shortcuts were only configurable in multibox.json)
- **Phase:** 3. Part of spec Phase 5 (navigation) and Phase 7 (persistence), pulled forward for the first public release.

## Context

Players could only change shortcuts by editing `multibox.json` next to the exe, which an update overwrites. The player
asked for the slot keys (`1`–`5`) and `Alt` to be changeable in Settings, and kept per player.

## Decision

- **Settings → Keyboard** lists every shortcut: focus slot 1…N, next slot, previous slot, fullscreen. Click one, press
  the new key. Esc cancels, Backspace/Delete removes the key, **Reset to defaults** goes back to `multibox.json`.
- **Capture** uses the Settings window's own WPF key events (`ShortcutCapture`): no keyboard hook (ADR 0009). Tapping
  Alt alone is captured on release, as in `KeyRouter`.
- **Same rules as before** (`ShortcutMap.Parse`): no F-keys, the game's keys need Ctrl or Alt, only Alt may stand alone.
  A refused key leaves everything unchanged and says why.
- **A key already in use is swapped** (`ShortcutEditor.Assign`): the other shortcut gets this one's old key, and the
  message says so. Two shortcuts never share a key.
- **Per-slot keys:** `ShortcutConfig.FocusSlots` (one entry per slot) replaces the `{n}` pattern once a slot key is
  changed. A top-row digit still also works on the number pad, unless that keypad key has its own binding.
- **Persistence:** saved as `Shortcuts` in `%AppData%\FourvaleMultibox\app-settings.json`; absent = the defaults from
  `multibox.json`, so a new default in an update reaches players who never changed anything. Invalid saved shortcuts
  are ignored with a log warning, and the defaults are used.
- **Applied at once:** a change rebuilds the `ShortcutMap` and `KeyRouter`; no restart.

## Tests

`ShortcutEditorTests` and `ShortcutCaptureTests`: one slot changes without moving the others; swap; Alt tap moved to a
slot; refused keys (Q, F2, Shift+Tab); Ctrl+game key allowed; clearing; explicit keypad binding wins over the keypad
copy; key text round-trips through the settings file; capture of modifiers, Alt tap, Alt+key, Esc and Backspace; the
editor view model applies, refuses and resets.

Smoke test 2026-10-06 (spare slot 8): saved custom shortcuts load at start (`shortcuts: custom; Z (slot 1), …`) and are
written back on exit. Using the Settings editor itself is checked by the player (no synthetic input; antivirus).
