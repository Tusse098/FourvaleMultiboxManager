# Fourvale Multibox Manager — Specification v2

## 1. Summary

A Windows desktop application that lets one player run and manually control about 2–5 Fourvale.com accounts from a single window.

It is a **multibox browser + game-state dashboard + attention manager**. It hosts isolated Fourvale sessions, reads the game state the browser already receives, turns that state into a central dashboard, and tells the player which character needs them next.

The player makes every gameplay decision. The application organises information and reduces window switching; it never plays the game.

Target: the current version of https://fourvale.com/ (currently in beta). Fourvale itself is the only source of truth for how the game works.

---

## 2. Non-goals

- **No FourFold.** Do not use FourFold, FourFold Account Manager, their code, APIs or behaviour as a reference. FourFold is conceptual inspiration only.
- **No autonomous gameplay.** The application never decides or executes combat, movement, quests, trades, purchases or any other game action on its own.
- **No detection evasion.** Nothing in the application may be designed to hide its existence or disguise its input from Fourvale (no randomised delays, no "humanised" input, no spoofing).
- **No credential handling.** The application never asks for, stores or logs passwords, cookies, tokens or auth headers.

---

## 3. Play Policy

### 3.1 Baseline

This project follows the conventions of established MMOs, applied slightly more leniently because Fourvale is in beta. **Fourvale's own rules always override this section.** If Fourvale publishes or communicates a rule that is stricter, the stricter rule wins and the affected feature is disabled.

The governing principle:

> **One physical input from the player produces at most one action per game client, and every action follows a decision the player made at that moment.**

### 3.2 Feature tiers

| Tier | Description | Examples | Status |
|---|---|---|---|
| **A — Allowed** | Does not send any input to the game | Dashboard, alerts, attention queue, focus switching, "jump to next ready character", layouts, muting, reloading a crashed panel | Core features, always available |
| **B — Allowed with safeguards (beta leniency)** | One physical input mirrored once to several clients | Click mirroring, key mirroring | Experimental, off by default, see §12 |
| **C — Not allowed** | Input the player did not individually produce, or decisions made by software | Hold-to-repeat, auto-clicking, timed/scheduled actions, macros/sequences, auto-login, reacting to game state by sending input, automatic healing/potions | Never implemented |

Large MMOs typically ban Tier B outright. Allowing it here is the "more lenient" part of the policy and is the first thing to remove if Fourvale objects.

### 3.3 Phase 0 — Published rules check

Phase 0 checks what Fourvale has **published**. It does not wait for a reply from the developers.

1. Look for Fourvale's terms of service, rules page, FAQ, Discord rules and developer statements on: multiple accounts, accounts per person/household, third-party tools, input broadcasting.
2. Send the developers a short message with these questions, but **do not block on the answer**:
   - Is one person playing several accounts at the same time allowed? Is there a limit?
   - Is a tool that hosts several Fourvale sessions in one window and shows their status acceptable?
   - May such a tool mirror one click or keypress to all of my sessions (one input → one action per session, no repetition)?
3. Record the findings, sources, dates and the date the message was sent in `docs/policy.md`.
4. Proceed under the working policy in §3.1–3.2 unless a published rule says otherwise.

**Stop conditions:** the project stops only if a published rule prohibits multi-accounting. If a published rule prohibits tools or broadcasting, the affected tier is removed.

**While the developers have not answered:**

- Tier A work (Phases 1–7, 9) proceeds normally.
- Tier B (input mirroring) is not enabled. It is re-checked at the start of Phase 8 (§12.3).
- Any later answer or rule change is recorded in `docs/policy.md` and applied immediately, even mid-phase.

**Accepted risk:** the player is proceeding without explicit confirmation from Fourvale. Use accounts the player can afford to have actioned during the beta.

---

## 4. Known Starting Facts

Unverified observations to be confirmed or corrected in Phase 1:

- The homepage HTML is a near-empty shell titled "Fourvale - Battle Slice" with a prompt asking mobile users to rotate to landscape. This suggests a JavaScript application that renders to **canvas or WebGL** rather than DOM elements.
- If true, game state is unlikely to be readable from the DOM. Network traffic (likely WebSocket) and in-memory JavaScript state are the probable sources.
- As a beta, Fourvale is expected to change frequently, including protocol and frontend changes and possibly data resets.

---

## 5. Architecture

### 5.1 Pipeline

```text
Fourvale session (WebView2)
        ↓  raw messages / state
Fourvale State Adapter          ← only component that knows Fourvale internals
        ↓  normalized snapshot + field freshness
Session State Store
        ↓  state changes
Event & Rules Engine
        ↓  events, attention items
Dashboard · Attention Queue · Notifications · Panel overlays
```

The UI never reads raw Fourvale messages, DOM structures or JavaScript objects.

### 5.2 Technology

- **Host:** C# / .NET with WPF or WinUI 3, embedding **WebView2**. Final choice made in Phase 2 based on how well each handles 5 WebView2 controls and global shortcuts.
- **Session isolation:** one **CoreWebView2Profile** per account slot inside a shared environment. Profiles isolate cookies, storage and auth while sharing one browser process tree, which is much lighter than separate user-data folders. Fall back to separate user-data folders only if profiles prove insufficient.

### 5.3 WebView2 requirements

| Concern | Requirement |
|---|---|
| **Background throttling** | *(Updated 2026-10-01, finding H1: a hidden slot pauses the game outright, so no layout may hide a running slot. Focus-style layouts show the other slots as small live views.)* Chromium throttles timers in hidden/occluded pages. Sessions must keep running normally when unfocused, small or covered. Test in Phase 2; mitigate with browser arguments and by keeping all panels technically visible. This is a project-level risk. |
| **Audio** | Only the focused session plays sound by default. Per-slot override. |
| **Crashes** | Handle `ProcessFailed`; reload only the affected panel and mark it LOADING. Other sessions are unaffected. |
| **Keyboard** | Global shortcuts handled via `AcceleratorKeyPressed` and host-level hooks so they work while a game panel has focus. Everything else passes to the game untouched. |
| **Zoom** | Per-panel zoom factor so the game stays readable in small tiles. |
| **Isolation check** | Automated test that state from session N is never attributed to slot M. |

---

## 6. Data Model

### 6.1 Accounts vs characters

A **slot** belongs to an account. The character currently played on that account is *detected state*, not configuration, because one account may have several characters.

```text
Slot
├── SlotId
├── DisplayName
├── ProfileName (WebView2 profile)
├── ShortcutBinding
├── NotificationOverrides
└── Session
    ├── BrowserState      (Loading | Ready | Crashed | Navigating)
    ├── ConnectionState   (Connected | Reconnecting | Disconnected | Unknown)
    ├── CharacterName     Field<string>
    ├── GameState
    │   ├── Class         Field<string>   (can change mid-session)
    │   ├── Level         Field<int>      (per class)
    │   ├── Hp / MaxHp    Field<int>
    │   ├── Resource / MaxResource   Field<int>
    │   ├── Xp            Field<long>
    │   ├── Location      Field<string>
    │   └── ...           (only fields confirmed in Phase 1)
    ├── Activity          (see 6.2)
    ├── Urgency           (see 6.2)
    └── LastSuccessfulUpdate
```

> **Spec change 2026-10-01 (Phase 1 findings K1–K2, player request):** the player can switch class during a session. The app must detect a class change and show it. Level, HP and SP are per class, so on a class change those fields switch to the new class's values and their history and thresholds restart. A class change is a dashboard event.

Every game field is a `Field<T>` carrying its value, the time it was last confirmed, and its source. A field older than its staleness limit is displayed as **UNKNOWN**, never as a current value.

### 6.2 Two-axis status

Status is split into two independent dimensions.

**Activity — what the character is doing** (derived from game state):
`Loading · Idle · Busy/Acting · InCombat · Disconnected · Unknown`

**Urgency — how much the player needs to care** (produced by rules):
`Normal · Ready · Attention · Warning · Critical`

`Ready` means "this character can usefully receive input now" (action available, combat ended, cooldown finished). The exact Activity values depend on what Phase 1 finds.

---

## 7. Fourvale State Adapter

### 7.1 Source priority

Use the first source that works reliably:

1. **Passive network observation** via the Chrome DevTools Protocol in WebView2 (e.g. WebSocket frame and HTTP response events). Read-only, no injection into the page.
2. **Read-only page script** that reads existing in-memory state and posts it to the host. Must not modify game objects or call game functions.
3. **DOM reading** for any HUD elements that are real DOM.
4. **Screen capture / OCR** only if nothing structured exists, only for the specific field, and at a low rate.

### 7.2 Adapter rules

- The adapter is versioned and isolated in its own module.
- Phase 1 captures sanitised sample messages; these become **test fixtures**. Adapter tests run against them.
- Messages that fail to parse are counted and logged (sanitised), never crash the session.
- **Adapter health indicator:** per session, shows OK / Degraded / Broken based on whether expected fields keep arriving. After a Fourvale update this is the first thing the user sees, instead of a dashboard that quietly fills with UNKNOWN.
- Auth-related messages and fields are dropped at the adapter boundary and never reach the store or logs.

---

## 8. User Interface

### 8.1 Layout modes

| Mode | Description |
|---|---|
| **Grid** | All sessions visible in equal tiles (2 → side by side, 4 → 2×2, 5 → configurable, e.g. 3+2 or 1 large + 4 small) |
| **Focus** | One session large, others as small live tiles |
| **Dashboard** | Dashboard dominant, sessions minimal (sessions must keep running, see §5.3) |
| **Compact** | Small tiles plus a condensed status strip |

All layouts are resizable and saved per configuration.

### 8.2 Dashboard

One row/card per slot showing only confirmed fields, e.g.:

```text
C1  Aria      Lv 42  HP 100%  Res 82%   InCombat   ● Normal     2s ago
C2  Bram      Lv 39  HP  91%  Res 61%   Idle       ◆ Ready      1s ago
C3  Cyra      Lv 41  HP  47%  Res 28%   InCombat   ⚠ Attention  1s ago
C4  Dorn      Lv 37  HP   —   Res  —    Unknown    ? Unknown   48s ago
C5  Eli       Lv 39  HP  18%  Res 22%   InCombat   ✖ Critical   0s ago
```

Plus: adapter health per slot, event log, attention queue.

### 8.3 Panel overlays

Each game panel shows a small, non-interactive badge (slot number, urgency colour, HP bar if available) and a highlighted border when focused, so the player can read status without looking at the dashboard.

---

## 9. Navigation and Shortcuts

All shortcuts are configurable. Defaults avoid F1 (help) and F5 (browser refresh); final defaults are checked against Fourvale's own key bindings in Phase 1.

| Action | Default |
|---|---|
| Focus slot 1–5 | `Ctrl+1` … `Ctrl+5` |
| Next / previous slot | `Ctrl+Tab` / `Ctrl+Shift+Tab` |
| ~~Focus next Ready / attention item~~ | ~~`Ctrl+Space`~~ removed 2026-10-06 (ADR 0010) |
| Acknowledge current attention item | `Ctrl+Enter` |
| Toggle dashboard | `Ctrl+D` |
| Cycle layout mode | `Ctrl+M` |
| Toggle mirroring (if enabled, §12) | `Ctrl+B` |

Focus changes are instant (target < 100 ms) and also route audio to the focused session.

**Auto-focus** (jumping to a critical character without a keypress) is opt-in, and never fires within 1 second of the player's last input, to prevent clicks landing in the wrong session.

---

## 10. Attention Engine

### 10.1 Events

State changes become timestamped events:

```text
13:42:11  C3  HP 71% → 42%  (below 50%)
13:42:16  C1  Activity InCombat → Idle
13:42:22  C4  Connection lost
13:42:29  C2  Level 38 → 39
```

### 10.2 Attention queue behaviour

- **One item per condition per slot** (deduplication).
- **Ordered by urgency, then age.**
- **Auto-resolve** when the condition clears.
- **Hysteresis** on thresholds: e.g. enter Critical below 25% HP, leave only above 30%.
- **Minimum duration** option: a condition must hold for N seconds before alerting (filters flicker).
- **Acknowledge** removes the alert but keeps the item visible until resolved.
- **Snooze** hides an item for a chosen time.
- Clicking an item focuses that session.

### 10.3 Ready detection

"Who needs me next" is the primary use case. Ready events (action available, combat ended, idle after activity) should be built as carefully as danger alerts.

> **Spec change 2026-10-06 (ADR 0010):** the `Ctrl+Space` / Space "focus next Ready" round-robin was removed at the player's request. Ready events may still feed the attention queue and notifications.

---

## 11. Rules and Notifications

### 11.1 Rules

Rules map state to urgency and notifications. They may only reference fields confirmed in Phase 1.

```text
WHEN Hp% < 50  FOR 2s   THEN Attention   CLEAR WHEN Hp% > 55
WHEN Hp% < 25           THEN Critical    CLEAR WHEN Hp% > 30
WHEN Connection = Disconnected FOR 5s   THEN Critical
WHEN Activity changes InCombat → Idle   THEN Ready
WHEN AdapterHealth = Broken             THEN Warning
```

The rules engine is generic over field names so new Fourvale fields can be added without code changes to the engine. Rules start as a config file; a rule editor UI comes later.

### 11.2 Notifications

Per rule, any combination of:
dashboard highlight · panel badge · attention queue · taskbar flash · sound · Windows toast.

Defaults: Critical → all; Warning → dashboard, badge, queue, sound; Attention/Ready → dashboard, badge, queue.

Quiet mode suppresses sound and toasts.

---

## 12. Input Assistance

### 12.1 Always available (Tier A)

- ~~Focus next Ready/attention character (`Ctrl+Space`).~~ Removed 2026-10-06 (ADR 0010).
- One-key focus per slot.
- Audio follows focus.

Together these give most of the speed of broadcasting while keeping one input = one action in one client.

### 12.2 Input mirroring (Tier B, experimental)

Mirrors a single physical click or keypress to several sessions.

**Hard constraints:**

- One physical input event → exactly one forwarded event per participating session.
- **Keyboard auto-repeat events are not forwarded.** Holding a key sends only the initial press.
- No hold-to-repeat, no queues, no delays, no sequences, no timing tricks.
- No randomisation or jitter of coordinates or timing.
- Only forwards while mirroring is explicitly toggled on; a clear on-screen indicator shows it is active.
- Per-slot opt-in: the player chooses which slots participate.
- Never triggered by game state; only by the player's physical input.

**Technical notes:**

- Click coordinates are normalised to the game canvas bounds of the source panel and scaled to each target panel. Panels should use the same zoom and aspect ratio while mirroring; mirroring refuses to run if they don't match.
- Implementation via CDP input dispatch to each WebView2.

### 12.3 Kill switch

- *Settled 2026-10-01: input mirroring is not allowed (the Fourvale creator); Phase 8 is skipped and the rest of this section is history.*
- Build flag `EnableInputMirroring` (default **false**). Decided at the start of Phase 8 using the policy known at that time:
  - **Explicitly permitted** (published rule or developer answer) → may be set to true.
  - **Prohibited** → stays false; Phase 8 is skipped.
  - **Still unknown** → the player decides whether to accept the risk; the decision and date are recorded. The runtime toggle stays off by default either way.
- Runtime setting, off by default even when the build flag is on.
- If Fourvale's rules change to prohibit it, ship an update with the flag off. Do not leave a hidden way to re-enable it.

---

## 13. Persistence

Saved in `%AppData%\FourvaleMultibox\` as JSON:

- slots (display name, profile name, shortcut, notification overrides)
- layouts and window arrangement
- shortcuts
- rules and notification settings
- dashboard preferences

Never saved: passwords, cookies, tokens, auth headers, captured game messages (except sanitised fixtures in the dev repo).

WebView2 profile data (which includes the game's own login cookies) lives in the profile folder managed by WebView2, not in the app's config. ~~Users can sign out or delete a slot's profile from the app.~~ *Spec change 2026-10-06 (ADR 0012): not built; players log out inside the game or delete the slot's profile folder.*

---

## 14. Diagnostics and Privacy

Diagnostics view shows per session: browser state, connection state, adapter health, last update per field, parse error counts, recent events, browser console errors.

Logging rules:

- Never log credentials, cookies, tokens, auth headers or raw auth messages.
- Do not log chat content or other players' data by default.
- Raw message capture is a developer-only toggle, writes to a local file, applies the same redaction, and is off on every start.

---

## 15. Acceptance Criteria (measurable)

| # | Criterion | Target |
|---|---|---|
| 1 | Independent logins | 5 slots logged into 5 different accounts simultaneously |
| 2 | Isolation | 0 cross-slot state attributions over a 2-hour soak test |
| 3 | Focus switch | < 100 ms from shortcut to input reaching new session |
| 4 | State latency | Dashboard reflects a game-state change within 1 s |
| 5 | Background behaviour | Unfocused / small sessions show no throttling-induced lag or disconnects over 1 hour |
| 6 | Staleness | Missing or stale fields show UNKNOWN within their staleness limit |
| 7 | Resilience | Reloading or crashing one session does not affect the others or lose app state |
| 8 | Resources | 5 sessions within a RAM/CPU budget set from Phase 2 measurements |
| 9 | Manual control | Every session remains fully playable by mouse and keyboard |
| 10 | Policy | No Tier C behaviour exists; Tier B obeys every constraint in §12.2 |

---

## 16. Phases

Each phase: investigate → build the smallest useful thing → test against real Fourvale → document → continue. If a Fourvale assumption proves wrong, revise the architecture instead of layering workarounds.

| Phase | Goal | Exit criteria |
|---|---|---|
| **0 — Rules** | Establish what Fourvale has published | `docs/policy.md` written from published sources; developer message sent (reply not required); go unless a published rule prohibits multi-accounting |
| **1 — Discovery** | Understand Fourvale's tech and data | Discovery report (§17) with data-availability matrix and sample fixtures |
| **2 — Proof of concept** | One session → adapter → external display | One confirmed field updates live outside the game view; throttling tested with the panel hidden |
| **3 — Multi-session** | 2–5 isolated sessions | Criteria 1, 2, 5, 7, 8 pass |
| **4 — Dashboard** | Cards, fields, freshness, event log | Only confirmed fields shown; UNKNOWN works |
| **5 — Navigation** | Shortcuts, layouts, focus, audio routing | Criterion 3 passes; no conflicts with Fourvale keys |
| **6 — Attention** | Rules, queue, Ready detection, notifications | Hysteresis, dedupe, ack, snooze, auto-resolve work |
| **7 — Persistence** | Save/restore everything in §13 | Restart restores layout and slots, still logged in via profiles |
| **8 — Mirroring** *(see §12.3)* | Re-check policy, then Tier B feature | Policy re-checked and flag decision recorded; all §12.2 constraints verified by tests |
| **9 — Polish** | Diagnostics, reconnect handling, installer, docs | Criteria 1–10 pass |

---

## 17. Phase 1 Discovery Report

The report must answer:

1. What frontend technology does Fourvale use (framework, canvas/WebGL/DOM)?
2. How does the browser talk to the backend (WebSocket, HTTP, SSE, polling)? Message format?
3. How is authentication maintained (cookies, storage, tokens)? Does it survive app restarts in a WebView2 profile?
4. Where is game state observable (network, JS memory, DOM)?
5. Which fields are available? (matrix below)
6. How often does each field change, and is it pushed or polled?
7. Is there a reliable event-driven source?
8. Can 5 isolated WebView2 profiles run Fourvale simultaneously?
9. How does Fourvale behave when its page is hidden, small or throttled?
10. Which Fourvale key bindings must global shortcuts avoid?
11. What can be extracted without screenshots/OCR?
12. What cannot be extracted reliably?
13. What are the technical risks?
14. What exactly should Phase 2 implement?

### Data-availability matrix template

| Field | Available? | Source | Update mechanism | Frequency | Confidence | Notes |
|---|---|---|---|---|---|---|
| Character name | | | | | | |
| Account identifier | | | | | | |
| Level | | | | | | |
| Class | | | | | | |
| HP / Max HP | | | | | | |
| Resource / Max | | | | | | |
| XP | | | | | | |
| Location | | | | | | |
| Combat state | | | | | | |
| Target | | | | | | |
| Action availability | | | | | | |
| Cooldowns | | | | | | |
| Inventory | | | | | | |
| Currency | | | | | | |
| Quest state | | | | | | |
| Connection state | | | | | | |

Only fields marked available with medium or high confidence are implemented.

---

## 18. Risks

| Risk | Impact | Mitigation |
|---|---|---|
| Fourvale prohibits multi-accounting or tools | Project stops or shrinks | Phase 0 published-rules check; apply any later answer immediately |
| Proceeding without developer confirmation | Account action during beta | Tier A only by default; mirroring not allowed (settled 2026-10-01) |
| Fourvale prohibits mirroring | Tier B removed | Build flag, off by default |
| Background throttling breaks unfocused sessions | Core concept fails | Test in Phase 2; browser arguments; keep panels visible |
| Beta updates change protocol | Adapter breaks | Isolated adapter, fixtures, health indicator |
| Canvas-only rendering, no structured data | Little to show | Network/JS sources first; OCR only per field as last resort |
| Memory use of 5 sessions | Poor performance | Shared-process profiles; measure in Phase 2–3 |
| Shortcut conflicts with the game | Broken controls | Configurable bindings; Phase 1 key survey |
| Wrong-session input from auto-focus | Misplays | Auto-focus opt-in with input cooldown |
| Beta data resets | Stale slot names/history | Character is detected state, not config |
