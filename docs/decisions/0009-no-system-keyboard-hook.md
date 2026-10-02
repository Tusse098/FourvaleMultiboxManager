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

## Update 2026-10-01: shortcuts stuck after a fullscreen switch

In live play, every shortcut stopped working after toggling fullscreen. Two weak points were fixed:

- **Key repeat** is now read from Windows' previous-key-state flag (lParam bit 30) instead of "is this key still in the held set". A key release that never reached the app can no longer turn every later press into an ignored "repeat".
- **Typing detection** also re-checks `document.activeElement` twice a second and posts only on change. A focused text field removed from the page fires no `focusout`, which could leave the slot marked "typing" forever, so plain keys went to the game. The script stays read-only.

When a shortcut key is passed to the game because the slot reports typing, the log says so once (`shortcut key ... passed to the game: slot N reports typing`).

**Unstick keys button (2026-10-01).** The top bar has an "Unstick keys" button that clears every slot's typing state and the held-key state, then returns focus to the focused slot. Real focus changes in the page always report (only the half-second re-check skips unchanged values), so clicking into chat after a reset counts as typing again.

**Space after attacking (2026-10-01).** Pressing Space right after an attack sometimes stayed on the slot that had just acted for up to ~2 s. Likely cause, not yet confirmed live: the game data was fine (meter resets within ~0.1 s, discovery.md); the store was only refreshed by the 250 ms read tick at Background priority, which rendering and input can hold back. Space now reads every slot just before it decides, the read tick runs at Normal priority, and each press logs the timers it saw (`next to act -> slotN (...; last read N ms before)`).

**Shortcuts dead after opening a slot (2026-10-02).** Opening slot 4 while slots 1–3 ran stopped every shortcut; "Unstick keys" found no slot typing, and the log showed no key reaching the router at all. So the key messages failed the "is this the main window or a child of it" check. Most likely WebView2 left the game's keyboard window outside the main window when the new view was created (not yet confirmed). The check now also accepts a browser window (class `Chrome_*`) on the UI thread unless it sits in the Settings or Live state window, and logs each such window once. "Unstick keys" now logs where the keyboard focus is after it hands focus back to the game.

**Real cause of "shortcuts stopped" (2026-10-02).** `ShowNotice` unsubscribed the key filter: a stray line left by the edit that replaced the keyboard hook (commit 373983e) meant for `OnClosing`. Any notice ("Slot 4 is not open", "No character is in battle", even "Shortcuts reset") switched every shortcut off for the rest of the session. That explains all the reports: pressing 4 before slot 4 was open, Space while talking to an NPC (nobody in battle), and the earlier fullscreen case. Removed; the filter is now unsubscribed only on close. The earlier theories in this ADR (stuck typing state, missed key release, key window outside the main window) were not the cause. The repeat-flag and typing re-check changes stay as hardening; the "key window outside the main window" widening was reverted, because the focus log showed the key window was always a child of the main window.
