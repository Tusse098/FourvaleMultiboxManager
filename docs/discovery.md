# Phase 1 Discovery Report

Status: **complete (2026-10-01).** All §17 questions answered; player checks R1–R4 done (reported by the player, not recorded in captures). Answers are filled in as observations are made. Every observation has a date and a method. Fourvale is in beta, so re-verify anything older than a few weeks.

## Observation log

### 2026-10-01 — Static review of the public client (no login)

Method: HTTP GET of `https://fourvale.com/` and read-only text search of the public JS bundle `/assets/main-DZfOvN-x.js` (~2.9 MB). Client constants report version `0.98`, `Beta`. Nothing was executed against the game and no account was used.

| # | Finding | Confidence |
|---|---|---|
| O1 | Rendering: **Phaser** (Scale.FIT into `#game`), so the game draws to canvas. Login, dialogs and some panels are DOM overlays (`fw-*` classes). | High |
| O2 | Networking: **Colyseus** client (`@colyseus/schema`) over `wss://api.fourvale.com`. Rooms seen: `town`, `dungeon`, `battle`. Frames are binary: the first byte is the protocol code (10 JOIN_ROOM, 13 ROOM_DATA, 14 ROOM_STATE, 15 ROOM_STATE_PATCH, …). | High |
| O3 | REST API on `https://api.fourvale.com/api/*`: login, register, trial, 2fa, password, profile, item/use, shop/buy, quest/complete, guild/*, online, rankings. The login page polls `/api/online` every ~20 s. | High |
| O4 | Auth: bearer token stored in `localStorage` (`ss_token_v1`), sent as an `Authorization: Bearer` header and as `token` in Colyseus join options. Each room join returns a `reconnectionToken`. These are handled per ADR 0001 and never captured. | High |
| O5 | The client saves the player profile (level, xp, silver, gold, inventory, equipped, quests) in `localStorage` and POSTs it to `/api/profile` (debounced to 3 s). This may be a strong source for character fields. To be verified with captures. | Medium |
| O6 | Chat: room message `chat` (send) and `chatMsg` (receive), payload `{scope, text, from, to, toSession, verified}`. | High |
| O7 | Single-tab lock: `BroadcastChannel("fourvale-active-tab")` pauses older tabs in the same browser profile. Separate WebView2 profiles have separate storage partitions, so slots should not lock each other. Must be verified (Q8). | Medium |

### 2026-10-01 — Capture 1 (slot 1, ~7 min, logged in)

Method: `tools/Fourvale.Capture`, file `2026-10-01_14-15-23_slot1.jsonl` (local only, 6,436 records, 35 markers). Activities: town, several dungeon battles, zone changes, a class/look change, level-up, chat, logout. The redaction audit passed: all 27 reconnection tokens were redacted; no e-mail, JWT or bearer strings appeared in text or binary blobs; chat text and names were redacted; login and logout bodies were dropped.

Traffic overview: 27 room joins over the session, because each zone change and each battle is a new Colyseus room. Over the whole session, `ROOM_STATE_PATCH` frames averaged ~13/s and made up 89% of frames.

| # | Finding | Confidence |
|---|---|---|
| C1 | **Rooms:** `town` (one per map, e.g. `shikaakwa_b3`), `dungeon`, `battle`. The matchmake response (`/matchmake/joinOrCreate/<room>`) gives `room.name`, `mapId`, `roomId` and **our own `sessionId`** for that room, which is how "self" can be identified in room state and events. | High |
| C2 | **Schema state (binary, needs a decoder).** The handshake declares field names. Battle `combatants` map: `id, name, isPlayer, classId, level, hp, maxHp, sp, maxSp, att, mag, def, res, spd, skl, lck, actionMeter, attackRateMs, alive, statuses, boss, row` plus `phase`, `log`. Town/dungeon `players` map: `id, name, classId, level, x, y, dir, moving, mapId, guild, inBattle, dungeonId` plus `mapId`, `cleared`, `joinLocked`. | High (names); values not yet decoded |
| C3 | **Room messages (MessagePack, decoded):** `hpSync {hp, sp}` on joining a room; `startBattle {nodeId, enemies[{name,hp,n,type}], xp, silver}`; `damage {sourceId, targetId, amount, kind (damage/heal), crit, killed, element}`; `battleOver {win, defeated, expEach, silverEach}`; `dungeonComplete`; `teleport {toMap, toX, toY}`; `consumables {name: count}`; `log {text, kind}`; `gldEvt` (guild roster incl. own level/mapId/online); `onlineCount`; `chatMsg`/`chatTyping` (redacted); `frEvt` (friends). | High |
| C4 | **Client → server (observed, never sent by us):** `moveTo {x,y}`, `action {type: attack/skill, targetId/skillId}`, `act`, `battleReady`, `nodeCleared`, `dungeonResume`, `updateLook`, `chat`. Useful to recognise player activity. | High |
| C5 | **REST:** `/api/me` at login returns the full profile: `level, xp, hp, sp, gold, silver, classId, classXp, hpByClass, spByClass, inventory, equipped, lastMap, lastX/Y, questsAccepted/Completed, weekly, …`. The profile is POSTed to `/api/profile` regularly (36 times); the response is `{ok:true}`. We do not read request bodies by design. | High |
| C6 | **Level-up** produced no dedicated message. The new level must come from schema state (`players[].level`, `combatants[].level`) or a later `/api/me`/`gldEvt`. | Medium |
| C7 | **Logout:** `LEAVE_ROOM` → socket close → `/api/logout`. A disconnect without logout has not been observed yet. | Medium |
| C8 | MessagePack uses msgpackr; `undefined` arrives as fixext type 0. The capture tool now decodes this as `null`. | High |

### 2026-10-01 — Capture 2 (slots 1–3 at the same time, ~2 min)

Method: `tools/Fourvale.Capture` multi-slot build, files `2026-10-01_14-40-27_slot{1,2,3}.jsonl` (local only). Three different accounts in separate WebView2 profiles, playing together as a party (town → dungeon → 3 battles → town). The redaction audit passed for all three files.

| # | Finding | Confidence |
|---|---|---|
| M1 | **Three accounts ran at the same time without disconnects.** All 28 socket closes across the three slots were client-initiated (`LEAVE_ROOM` on zone or battle change). There were no server closes, socket errors or decode errors. Separate profiles avoid the single-tab lock (O7). | High (3 slots, 2 min); 5 slots and long sessions still to test |
| M2 | **Party members share rooms.** All three joined the same battle `roomId`s, each with its own `sessionId`, and every slot received the same `damage` and `log` events for all party members. **Attribution must use each slot's own sessionId from its own matchmake response**; "a damage event arrived in this slot" does not mean it concerns this slot's character. | High |
| M3 | The capture tool kept each slot's data separate: each file contains only its own matchmake sessionIds and markers. | High |
| M4 | State-patch volume was similar in all three slots (same rooms, same patches). No throttling difference was seen, but the layout during capture was not recorded. Q9 still needs a deliberate Focus-layout (hidden) test. | Low |
| M5 | Level-up (slot 1, level 2 character) again produced no dedicated message (confirms C6). | Medium |
| M6 | `/api/me` `username` and `profile.name` were equal for all three accounts. A battle log line named one account with a different class than its login profile, so class can change during a session: take class from room state, not only from login. | Medium |

### 2026-10-01 — Class changes and hidden slots (re-analysis of capture 1, plus player test)

| # | Finding | Confidence |
|---|---|---|
| K1 | **A class change has no WebSocket message of its own.** Sequence in capture 1 at 265 s: client POSTs `/api/profile` → the response `profile` has the new `classId` (`evergreen_soldier` → `arctic_soldier`) → `LEAVE_ROOM` and a new town join → the new room's `hpSync` (80/40 instead of 19967/1113) and the own `gldEvt` roster entry show the new class. Later battles log `"<name> (Arctic Soldier) joined the battle!"`. | High |
| K2 | **Level, HP and SP are per class.** Level went 268 → 1 on the switch (`classXp`, `hpByClass` and `spByClass` in the profile). The dashboard must show level and HP for the *current* class and reset its history on a class change. | High |
| K3 | Sources for current class, in order of preference: own entry in room state `players[].classId` (needs schema decoder; available on every join); `/api/profile` response `profile.classId` (only 4 of 36 responses carried a profile); own `gldEvt` roster entry (**only for players in a guild**); `/api/me` (login only, so it goes stale after a switch); battle log text (last resort). | Medium |
| H1 | **Hidden slots pause.** In the Focus layout the other slots were hidden (WebView2 not visible, so the page reports itself hidden) and the game paused (player test, 2026-10-01). Grid and Side by side, where all slots are visible, ran normally. The Focus layout now keeps every slot visible as a small live strip, and slots start with Chromium's background-throttling switches. Whether minimising the whole window pauses everything is not yet tested. | High (hidden pauses) |

### 2026-10-01 — Room state decoded (ADR 0002)

Method: `src/Fourvale.Adapter/Colyseus` (schema v2 decoder) run offline on all four captures with `tools/Fourvale.Replay`. **8,587 patches and 55 full states decoded with 0 errors and 0 definition mismatches.** The decoded values match the markers, `hpSync`, the `damage` events and the battle log.

| # | Finding | Confidence |
|---|---|---|
| D1 | **One schema for all rooms.** Each handshake declares the same 6 types; the room picks the root: town = type 3 `{mapId, players: map<Player>}`, dungeon = type 5 `{dungeonId, joinLocked, cleared, players: map<DungeonPlayer>}`, battle = type 1 `{combatants: map<Combatant>, phase, log}`. Map keys are Colyseus sessionIds, so **own entry = key equal to the sessionId from this slot's matchmake response** (C1, M2). | High |
| D2 | **Class and level:** `players[me].classId` / `.level` (town/dungeon) and `combatants[me].classId` / `.level` (battle) are set on every room join. The class change in capture 1 (265.6 s: `evergreen_soldier` 268 → `arctic_soldier` 1) and the slot 3 character's in capture 2 (39 s: `medicine_man` 152 → `arctic_soldier` 1) both appear directly. Level-up shows on the next room join (capture 1: level 2 at 427 s). Neither changed mid-room in the captures. | High |
| D3 | **Battle HP/SP:** `combatants[me].hp/maxHp/sp/maxSp/alive/statuses` change live per hit (e.g. hp 19967 → 19966 at 173.9 s, followed by the "Took damage" marker; sp −50 with the Hip Flask heal). | High |
| D4 | **Readiness:** `combatants[me].actionMeter` climbs 0 → 1 over `attackRateMs` (3500 ms) in ~50 ms steps, **stays at 1 until the player acts**, then drops to 0. `actionMeter == 1` is the "Ready" signal for spec §10.3. It is the most frequent field (≈80% of battle patches). | High |
| D5 | **Combat state:** dungeon `players[me].inBattle` flips true/false around each battle; battle room join/leave brackets the fight. **Position:** `players[me].x/y/mapId`, ~10 updates/s while moving. | High |
| D6 | No HP outside battle in room state; `hpSync` (on join) remains the source in town/dungeon. | High |

### 2026-10-01 — Capture 3 (slots 1–3, ~2.5 min, with live decoded view)

Method: capture tool with the live **Decoded state** view (player reports it "working well"). Files `2026-10-01_15-07-51_slot{1,2,3}.jsonl` (local only). The redaction audit passed. **2,606 patches and 29 full states decoded with 0 errors.**

| # | Finding | Confidence |
|---|---|---|
| L1 | **Level-up shows at the next room join.** The slot 3 character was level 2 when the battle at 20.4 s started and level 3 when the next one started at 41.9 s, and stayed 3 in town. Level never changed inside an open room in any capture. (The player's "Level up" marker at 37 s was pressed by mistake and is ignored.) | High |
| L2 | **`statuses` is a comma-separated list of effect names**, e.g. `"freeze,slow"` on an enemy after Permafrost (capture 1). The client knows `sleep, freeze, silence, charm, confuse, blind, poison, slow, haste, ward` (bundle label map). Own statuses were always empty so far. | High (format) |
| L3 | **Battle `phase`** changes to `"won"` at the end of a battle (21 times). Other phase values were not seen in patches. | Medium |
| L4 | All socket closes in capture 3 were again client-initiated room changes. | High |

### 2026-10-01 — Action timing (re-analysis of capture `2026-10-01_15-07-51`, slots 1–3)

- After a slot sends `action` (`{type: 'attack', targetId}`), its own `actionMeter` drops to 0 in the next patch, **50–110 ms** later (all 49 attacks in the 14:15 and 14:40 captures).
- In a party battle every member's meter then **stops for about 2 s** (attack animation): no meter patches, values unchanged, then they resume. The overlay timers pause with them.
- So a "Space doesn't switch yet" delay after attacking is not in the game data. Most likely cause: the app's read tick lagging behind (fix: Space reads all slots before deciding, ADR 0009 update). *2026-10-06: the Space feature was removed (ADR 0010).*

## Spec §17 questions

| # | Question | Answer (2026-10-01) | Confidence |
|---|---|---|---|
| 1 | Frontend technology | Phaser canvas; login, dialogs, chat and some panels are DOM overlays (O1) | High |
| 2 | Client ↔ backend | Colyseus over `wss://api.fourvale.com`: binary schema v2 state (full + patches) and MessagePack room messages; REST JSON on `/api/*` and `/matchmake/*` (O2, O3, D1) | High |
| 3 | Auth and persistence | Bearer token in the profile's `localStorage` (`ss_token_v1`), sent as a header and in join options (O4). Each WebView2 profile keeps its own, and **slots stay logged in across an app restart** (R1) | High |
| 4 | Where state is observable | Network only: room state, room messages, REST. Passive CDP network observation (spec §7.1 option 1) is enough; no page script, DOM or OCR needed | High |
| 5 | Which fields | See matrix: class, level, HP/SP, readiness, status effects, location, combat state, inventory, currency, quests. Missing: cooldowns, running XP | High |
| 6 | Change frequency, push or poll | All pushed. Action meter ~20/s in battle, position ~10/s while moving, HP/SP per hit, class/level per room join (D2, L1), `hpSync` per join, `/api/online` polled every ~20 s | High |
| 7 | Reliable event-driven source | Yes: room state patches plus `startBattle`/`damage`/`battleOver`/`teleport` messages and socket open/close | High |
| 8 | 5 isolated profiles at once | Yes. 3 accounts captured at once without disconnects (M1); 5 slots in Grid ran ~5 min without disconnects or lag (R4). Separate profiles avoid the single-tab lock (O7). RAM not yet measured | High (3), Medium (5, player-reported) |
| 9 | Hidden / small / throttled | A hidden slot pauses the game (H1). Small but visible slots run normally. A **minimised or fully covered window kept fights running** (R2, with the background-throttling switches from ADR 0001) | High (hidden), Medium (minimised/covered, player-reported) |
| 10 | Game key bindings to avoid | From the client code: `Q` quests, `I`/`C` character, `M` map, `Enter` chat, `Esc` close, `Tab` chat tabs, `←`/`→` paging, `Space`/`Enter` on focused buttons. `1`–`9`, `WASD` and `Space` do nothing in play (R3). **`F3` opens WebView2's browser Find bar**, so F-keys are claimed by the browser layer. Global shortcuts: modifier combinations (e.g. Ctrl+1…5), **no F-keys**. Phase 2 should set `AreBrowserAcceleratorKeysEnabled = false` on game panels so browser shortcuts (F3, F5, Ctrl+F…) cannot fire | High |
| 11 | Extractable without screenshots | Everything in the matrix rated High; no OCR needed | High |
| 12 | Not reliably extractable | Cooldowns (not in room state); running XP (only login value + per-battle rewards); HP outside battle between room joins (only `hpSync` on join); own status effects (format known, never seen on own character) | Medium |
| 13 | Technical risks | See *Technical risks* below | — |
| 14 | What Phase 2 should implement | See *Proposed Phase 2 scope* below | — |

## Technical risks

| Risk | Evidence | Mitigation |
|---|---|---|
| Colyseus schema v3 upgrade changes the binary format completely | Client bundles schema v2 today (ADR 0002) | Handshake decode fails immediately and visibly; the adapter health indicator goes Broken; the version markers to check are in ADR 0002 |
| Fourvale field or message renames (beta) | Field names come from each room's handshake | Adapter maps by name, not index; unknown fields are ignored; fixture tests catch drift |
| Hidden slots pause | H1 | No layout may hide a running slot (spec §5.3 updated) |
| Minimised window pauses everything | Not observed: fights kept running minimised and covered (R2) | Keep the background-throttling switches; re-check after WebView2 runtime updates |
| Party members' events arrive in every slot | M2 | Attribute by own sessionId per room, never by "arrived in this slot" |
| Class/level only refresh on room join | D2, L1 | Show the value with its timestamp; UNKNOWN after staleness limit |
| Server-side multi-session limits | None seen with 3 accounts (M1) or 5 slots (R4) | Record any kick in `policy.md` immediately |
| Browser shortcuts inside game panels | `F3` opened the WebView2 Find bar (R3) | Disable browser accelerator keys on game panels; avoid F-keys for app shortcuts |
| Memory for 5 sessions | 5 slots ran without lag (R4); RAM not measured | Measure in Phase 2/3 (acceptance criterion 8) |

## Proposed Phase 2 scope

Phase 2 exit criteria (spec §16): *one confirmed field updates live outside the game view; throttling tested with the panel hidden.*

1. **Host:** WPF. The capture tool shows WPF + WebView2 handles 3 profiles side by side; no reason found to switch to WinUI 3. Record the choice in an ADR.
2. **Adapter (`src/Fourvale.Adapter`):** move `NetworkObserver`, `Redactor`, `ColyseusFrameDecoder` and `MsgPackReader` in from the capture tool; keep `Colyseus/` (`StateDecoder`, `RoomTracker`). Add a `CharacterReader` that turns the own entry into adapter output: Class, Level, HP/MaxHP, SP/MaxSP, ActionMeter, InBattle, MapId, each with source and timestamp. Auth paths are dropped at this boundary as today.
3. **Core (`src/Multibox.Core`):** `SlotId`, `Field<T>` (value, confirmed-at, source) with staleness → UNKNOWN, a per-slot state store. Fields come only from the matrix rows rated High.
4. **App (`src/Multibox.App`):** one slot (one profile) plus a small separate panel showing the fields live, outside the game view. This is not the dashboard; that comes in Phase 4.
5. **Tests:** adapter tests against `tests/fixtures` (handshake and message fixtures exist; add a malformed-message case), `Field<T>` staleness tests.
6. **Throttling:** confirm hidden = paused (expected, H1), visible-small = fine, minimised/covered = running (R2), and measure RAM/CPU.
7. **Keys:** game panels with browser accelerator keys disabled (R3).

## Player checks (2026-10-01, reported by the player)

| # | Check | Result |
|---|---|---|
| R1 | Restart the tool with slots logged in | All slots still logged in |
| R2 | Minimise, then fully cover the window during a fight | Fights kept running |
| R3 | Press `1`–`9`, `WASD`, `Space`, `F1`–`F4` in game | No game function. `F3` opened the browser Find bar, so F-keys are excluded from shortcuts |
| R4 | 5 slots in Grid for ~5 minutes | No disconnects or lag; RAM not noted |

## Capture sessions

Use `tools/Fourvale.Capture` (ADR 0001). Record each session here: date, slot, what was done, file name (local only), notable findings.

| Date | Slot | Activity | File | Notes |
|---|---|---|---|---|
| 2026-10-01 | 1 | Town, dungeon battles, zone changes, class change, level-up, chat, logout | `2026-10-01_14-15-23_slot1.jsonl` | C1–C8 |
| 2026-10-01 | 1, 2, 3 | Three accounts at once in a party: town, dungeon, 3 battles | `2026-10-01_14-40-27_slot{1,2,3}.jsonl` | M1–M6 |
| 2026-10-01 | 1, 2, 3 | Party play with the live decoded view; level-up in battle | `2026-10-01_15-07-51_slot{1,2,3}.jsonl` | L1–L4 |

## Data-availability matrix

| Field | Available? | Source | Update mechanism | Frequency | Confidence | Notes |
|---|---|---|---|---|---|---|
| Character name | Yes | `players[me].name` / `combatants[me].name`; `/api/me` | Schema on join; login | Per join | High | Own entry = matchmake sessionId (D1) |
| Account identifier | Yes | `/api/me` `username` | Login response | Once | High | Treat as personal; do not log |
| Level | Yes | `players[me].level`, `combatants[me].level` | Schema on every join | Per join (level-up shows on next join) | High | Per class (K2) |
| Class | Yes | `players[me].classId`, `combatants[me].classId` | Schema on every join | On change (D2) | High | Class change = dashboard event (spec §6.1) |
| HP / Max HP | Yes | Battle: `combatants[me].hp/maxHp`; town/dungeon: `hpSync.hp` | Schema patch per hit; push on join | Live in battle | High | No maxHp outside battle (D6) |
| Resource / Max (SP) | Yes | Battle: `combatants[me].sp/maxSp`; town: `hpSync.sp` | As HP | As HP | High | |
| XP | Partial | `/api/me` `xp`; `battleOver.expEach` | Login; push after battle | Per battle | Medium | Running XP must be derived; verify |
| Location | Yes | `players[me].mapId/x/y`; matchmake `mapId`; `teleport` | Schema; room join; push | ~10/s while moving | High | |
| Combat state | Yes | Battle room joined; `players[me].inBattle`; `startBattle`/`battleOver` | Schema; push | Per battle | High | |
| Target | Partial | Own `action.targetId` (outgoing) | Client message | Per action | Low | Player's click, not server state |
| Action availability | Yes | `combatants[me].actionMeter` (1 = ready), `attackRateMs` | Schema patch | ~20/s in battle | High | Ready signal (D4) |
| Status effects | Yes | `combatants[me].statuses` (comma list) | Schema patch | On change | High (format), Low (own effects not yet seen) | L2 |
| Cooldowns | Unknown | not in room state; maybe skill messages | | | Low | No cooldown field in the schema |
| Inventory | Yes | `/api/me` `inventory`, `equipped`; `consumables` | Login; push on battle | Login / battle | High | |
| Currency | Yes | `/api/me` gold/silver; `battleOver.silverEach` | Login; push | Per battle | High (login), Medium (live) | |
| Quest state | Yes | `/api/me` quests*; `/api/quest/complete` | Login; REST | Rare | Medium | |
| Connection state | Yes | WebSocket open/close, `LEAVE_ROOM`, `/api/logout` | Network events | Event | High | Unexpected disconnect not yet observed |

The schema decoder is done (ADR 0002). Remaining gaps: cooldowns, running XP, and an unexpected disconnect.
