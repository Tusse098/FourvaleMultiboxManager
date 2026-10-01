# Phase 3 — Multi-session: verification

Spec §16 exit criteria: **§15 criteria 1, 2, 5, 7 and 8 pass.** Structure: ADR 0003 (unchanged).

## What exists (2026-10-01)

- **Slot picker** in the top bar (slots 1–`slotCount`, default 5): a grey number opens that slot, ✕ on a panel closes it. The open slots are remembered in `%AppData%\FourvaleMultibox\app-settings.json` (slot numbers only); on the first start `defaultSlots` from `multibox.json` is used. One `SlotSession` per open slot, each with its own WebView2 profile `slotN`, `NetworkObserver` and `FourvaleSession`. All slots share one browser process tree (spec §5.2). Slots start one after another.
- **Grid:** all panels always visible (2 → side by side, 3–4 → 2×2, 5 → 3+2). A hidden slot pauses the game (discovery H1), so nothing is ever hidden. Each panel has a header with slot, character, connection, adapter health and status, plus ⟳ to reload only that slot. The border turns gold when the slot is ready to act and red on a problem.
- **Live state window:** one card per slot (class, level, HP, SP, action, in battle, location; UNKNOWN when stale; field age and source on hover) and a diagnostics strip (uptime, isolation violations, browser memory/CPU, soak file).
- **Core:** `BrowserState` (Loading/Ready/Crashed/Navigating) and `ConnectionState` (Unknown/Connected/Reconnecting/Disconnected) per spec §6.1. `UnexpectedDisconnects` survives page reloads.
- **Adapter:** a room socket that closes without the game sending `LEAVE_ROOM` first counts as an unexpected disconnect. Sockets from before a reload are ignored.
- **Isolation monitor:** flags two slots reporting the same current character, or a slot's character changing without a reload. Each incident is logged once as `ISOLATION VIOLATION`.
- **Crash recovery:** a renderer exit or hang reloads only that slot; a browser-process exit rebuilds that slot's view. Recoveries are counted per slot.
- **Soak recorder:** every `soakIntervalSeconds` (60) one CSV row per slot in `%LocalAppData%\FourvaleMultibox\soak\soak-<start>.csv` with browser, connection, health, disconnects, recoveries, decode errors, updates/s, class/level freshness, isolation violations and browser processes, working-set MB, **private MB** and CPU. Budget against private memory: summing working sets counts shared pages once per process. No names or game data.
- Tests: 13 core (including isolation monitor), 82 adapter (including disconnect/reconnect rules), 3 capture tool.
- Smoke test by Claude (2026-10-01): three unused profiles (slots 7–9) opened side by side in the grid, 0 isolation violations. The test window appeared on the player's screen and the player logged in on slot 7 (confirmed by the player).

## Player tests

Close Fourvale Capture first. Open slots with the numbers in the top bar.

| # | §15 | Test | Pass when | Result |
|---|---|---|---|---|
| T1 | 1 | Start with 5 slots, each logged into a different account | All 5 show their own character | Needed in-app slot choice (player, 2026-10-01): added the slot picker. **Re-test with 5 slots.** |
| T2 | 2, 5, 8 | **Soak:** play normally (and idle) with all slots for **2 hours**; keep the window un-minimised | Isolation violations 0; no unexpected disconnects caused by the app; no lag in small/unfocused slots; send me the soak CSV | First run: ~2 min with 3 slots, all Connected/Ok, 0 disconnects, 0 decode errors, 0 isolation violations. **2-hour run still needed.** |
| T3 | 7 | Press ⟳ on one slot during play | Only that slot reloads; the others keep playing; Live state keeps the others' values | **Pass** (player, 2026-10-01) |
| T4 | 7 | **Browser task manager** → select one slot's tab process → **End process** | Only that slot shows "crashed, reloading" and comes back; others unaffected | **Pass** (player, 2026-10-01). Log: only `slot3: process failed (RenderProcessExited, Terminated)`; slot 3 restarted, the others continued |
| T2b | 2, 5, 8 | Soak evidence (player sessions, 2026-10-01) | | 36 min with 4 slots (soak CSV `soak-20261001-181258.csv`): 0 isolation violations, 0 unexpected disconnects, 0 decode errors, all Connected/Ok; then 50 min of play \"without any further issues\" (player). Old window-hosted build: private memory ~9.9 GB average (max 12 GB), CPU ~18% average. **Re-measure with visual hosting (ADR 0007).** |
| T5 | 8 | From the soak CSV | RAM/CPU for 5 slots recorded; the budget is set from it (spec §15 criterion 8) | First data (3 slots, working set): ~5.3 GB, CPU ~10%. Working set overstates shared memory; private memory is now recorded. **Needs the 5-slot soak data.** |
