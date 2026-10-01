# ADR 0005 — Plain-key shortcuts: keyboard hook and typing watcher

- **Date:** 2026-10-01
- **Status:** Accepted (player request)
- **Amends:** ADR 0004 (the shortcut keys)
- **Amended by:** ADR 0009. The system-wide keyboard hook was replaced by an in-process message filter, because antivirus software blocks the hook as a keylogger.

## Context

After trying the Ctrl-based shortcuts from ADR 0004, the player asked for plain keys: `1…5` to focus a slot, `Space` for "next ready", `Alt` (alone) for the next slot, plus borderless fullscreen and less chrome.

Two technical facts follow:

1. WebView2 passes only accelerator keys (Ctrl/Alt combinations) up to the WPF host. Plain keys pressed inside a game view go straight to the game, so the host never sees `1` or `Space`.
2. `1` and `Space` are also typed text: chat (opened with Enter), the login form and the password field. Taking them unconditionally would break typing.

## Decision

1. **Low-level keyboard hook** (`KeyboardHook`, spec §9 "host-level hooks"). It is active only while the Multibox main window is in the foreground, and it ignores synthetic (injected) events. `KeyRouter`, which is unit-tested, decides per event:
   - App shortcut: both the press and the release are swallowed, so the game never sees the key. Auto-repeat while held is swallowed without acting (one physical press, one action).
   - Anything else passes to the game untouched.
   - A lone `Alt` tap acts on release and is never swallowed, so Alt+Tab, Alt+F4 and Alt+Enter work. The window ignores the keyboard system-menu activation a lone Alt would trigger.
2. **Typing watcher** (`Multibox.Hosting/TypingWatcher`): a read-only page script (spec §7.1 option 2). It listens for `focusin`/`focusout` and posts one boolean: is an `INPUT`, `TEXTAREA` or contenteditable element focused? The host accepts the message only from the game's own host and only in that exact shape. While the focused slot reports typing, plain-key shortcuts pass through to the game.
   - **Why below option 1:** this is UI focus state, not game state. The network carries nothing about which element has focus, and no other source exists.
   - **Hard rule 5:** the script reads only `document.activeElement`. It does not read or modify game objects, call game functions, change the DOM or alter how the game handles input.
3. **Shortcut rules** (`ShortcutMap`): plain keys are allowed, except the game's own keys (discovery Q10: Q, I, C, M, Enter, Esc, Tab, ←, →), which need Ctrl or Alt. F-keys stay forbidden (discovery R3). Duplicates are rejected. An empty value disables a shortcut. Defaults: `focusSlot "{n}"`, `nextSlot "Alt"`, `previousSlot ""`, `nextReady "Space"`, `toggleFullscreen "Alt+Enter"`.
4. **Fullscreen:** borderless and covering the taskbar (Alt+Enter or the top-bar button). The top bar hides and reappears when the mouse touches the top edge.
5. **Chrome:** no panel margins or grey borders and a slimmer header. A constant 2 px edge is invisible normally and coloured only for focused (blue), ready (gold) or problem (red). Its thickness never changes, so status changes never resize the game canvas.

## Tier and safety check

- Only moves keyboard focus between slots and changes the window. No input is ever generated for the game; the hook only observes the player's own presses (spec §3.2 Tier A).
- No auto-focus: focus changes only on a key press or click by the player.
- `Space` is not used by the game (discovery R3) except to press a focused DOM button. A focused button is not a text field, so `Space` acts as "next ready" there. Accepted.

## Tests

`KeyRouterTests`: digit focus with press and release swallowed, repeat acts once, pass-through while typing, modified shortcuts while typing, game keys pass, Alt tap, Alt+Tab is not a tap, Alt+Enter. `ShortcutMapTests`: new defaults and rules.
