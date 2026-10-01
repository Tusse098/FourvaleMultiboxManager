# ADR 0009 — No system-wide keyboard hook

- **Date:** 2026-10-01
- **Status:** Accepted
- **Amends:** ADR 0005 (how plain-key shortcuts are seen)

## Context

ADR 0005 caught plain-key shortcuts (`1`–`5`, `Space`, `Alt`) with a low-level keyboard hook (`SetWindowsHookEx(WH_KEYBOARD_LL)`). When the repository was cloned for GitHub, the player's antivirus (RAV Endpoint Protection) blocked newly written copies of `KeyboardHook.cs` as "a virus or potentially unwanted software". The checkout failed, and the compiler could not open the file. A system-wide keyboard hook is the classic keylogger technique, so this would happen to anyone cloning the project.

## Decision

The hook was removed. Keys are read from the app's own message loop instead:

- Since ADR 0007 the game views are `WebView2CompositionControl`s. Each one's keyboard focus is a WebView2 input window (`Chrome_WidgetWin_0`) that is a **child window of our own process**, so its key messages are dispatched by our UI thread.
- `ComponentDispatcher.ThreadPreprocessMessage` sees each keyboard message (`WM_KEYDOWN`/`UP`, `WM_SYSKEYDOWN`/`UP`) before it is dispatched. The handler accepts only messages for the main window or its child windows (`IsChild`), passes them to the unchanged, unit-tested `KeyRouter`, and marks app shortcuts handled so the game never receives them.
- In-process only: no system-wide hook, and no view of other programs' keys.

## Consequences

- Same behaviour as ADR 0005: one press is one action, plain keys pass through while typing, a lone Alt tap moves to the next slot, and game keys pass untouched.
- Shortcuts only work while the Multibox window has the keyboard, as before.
- **Not verified with real key presses yet.** Synthetic test input was also flagged by the antivirus, so testing stopped. The player verifies with real key presses.
- The capture tool never used a hook. No project code calls `SetWindowsHookEx` any more.
