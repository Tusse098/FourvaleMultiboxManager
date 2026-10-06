# Fourvale Policy — Phase 0 Published Rules Check

> **⚠ AI-generated, probably not accurate.** This document was written by an AI assistant during development, from a
> quick automated look at public pages on 2026-10-01. It is **not** Fourvale's rules, it has not been checked by
> Fourvale, and much of it is likely incomplete, out of date or wrong. Do not rely on it. Fourvale's own rules and
> its developers (for example on the Fourvale Discord) are the only source of truth, and they always win.

Spec reference: §3 (Play Policy), §3.3 (Phase 0), §16 (Phases).

- **Check performed:** 2026-10-01
- **Fourvale version observed:** client constants `0.98` / `Beta` (from `/assets/main-DZfOvN-x.js`, 2026-10-01)
- **Decision:** **GO** for Tier A work (organising information and moving keyboard focus between the player's own sessions).
- **Developer message:** drafted below. **Not yet sent.** Date sent: `____` (to be filled in by the player).

> Hard rule 8: an unanswered question is not permission. Any answer or rule change is recorded here and applied immediately, even mid-phase.

---

## 1. Sources checked

| # | Source | How checked | Date | Result |
|---|---|---|---|---|
| S1 | `https://fourvale.com/` | HTTP GET of HTML shell | 2026-10-01 | SPA shell titled "Fourvale - Battle Slice". No rules, terms or links in the HTML. |
| S2 | `https://fourvale.com/{terms,tos,rules,faq,privacy,terms-of-service,robots.txt}` | HTTP GET | 2026-10-01 | All return the same 3,931-byte SPA shell (catch-all routing). **No dedicated rules/terms pages exist.** |
| S3 | Public client bundle `main-DZfOvN-x.js` (~2.9 MB) and `guildlog-Czyrlfw7.js` | Downloaded and text-searched (read-only) for terms, rules, multiple accounts, bots, macros, cheating, ban text, links | 2026-10-01 | No terms of service, rules, code of conduct, or registration agreement text. Only outbound links: Discord invite and `/rankings`. Findings F2–F4 below. |
| S4 | `https://sacredseasons.online/` | WebFetch | 2026-10-01 | Describes Fourvale as "a faithful, community-run revival … a fan project made by people who loved the original" (Sacred Seasons). Links only to `fourvale.com` and the Discord. No rules, terms or FAQ. |
| S5 | Discord invite `discord.gg/cEd2MX4tp4` | Public invite metadata API (no login) | 2026-10-01 | Server "Fourvale", Community server, onboarding enabled, ~78 members. **Rules channel / onboarding not readable without joining.** See gap G1. |
| S6 | Web search (general and extended) for Fourvale rules, multi-accounts, multiboxing | WebSearch | 2026-10-01 | No Fourvale-specific rules, developer statements or community discussions found. Only unrelated games. |
| S7 | `sacredseasons.fandom.com` (wiki for the original game) | WebFetch | 2026-10-01 | Blocked (HTTP 402). Not a Fourvale source anyway. |

## 2. Findings

| ID | Topic | Finding | Source |
|---|---|---|---|
| F1 | Published rules / ToS | **None found.** Fourvale publishes no terms of service, rules page or FAQ on its site or companion site. | S1–S4, S6 |
| F2 | Enforcement exists | The login/register/trial API can return `banned` with a reason and expiry; the client shows "You've been banned … This ban is permanent / In effect until …". Bans are a real mechanism, but the rules they enforce are not published. | S3 |
| F3 | Player reports | In-game report form placeholder: "What's the problem? (harassment, spam, cheating, an inappropriate name…)". "Cheating" is reportable but not defined. | S3 |
| F4 | **Single-tab lock** | The client uses `BroadcastChannel("fourvale-active-tab")`; when a second tab opens, older tabs are paused with: *"Game open in another tab — Sacred Seasons can only run in one tab at a time. This tab was paused so the other can play."* This is a **technical** restriction within one browser profile, not a published rule about accounts. It does show the developers did not design for several game instances in one browser. Our design (one `CoreWebView2Profile` per slot) means separate storage partitions, the same as separate browsers. **This is not an evasion technique and must not be treated as one:** we do not hide, spoof or patch anything. The question is included in the developer message (Q3). | S3 |
| F5 | Trial accounts | A trial flow exists that needs only an email address. No per-person account limit is stated. | S3 |
| F6 | Nature of project | Community-run fan revival of Sacred Seasons, in beta. Small community (~78 Discord members on 2026-10-01). Rules may exist only informally in Discord. | S4, S5 |

### Topics required by spec §3.3 step 1

| Topic | Published rule found? |
|---|---|
| Multiple accounts | No |
| Accounts per person / household | No |
| Third-party tools | No |

## 3. Gaps (unknown, not permission)

- **G1 — CLOSED 2026-10-01 for multi-accounting:** the player confirmed that multi-accounting is allowed (see §5a). Third-party tools are still unconfirmed.
- ~~G1 — Discord rules channel/onboarding not read.~~ Joining the server is needed. **The player must read the Discord rules (and any pinned or announcement posts about alts, multi-logging, tools or bots) and record the result in §5.** If they prohibit multi-accounting, the project stops (spec §3.3 stop condition). If they prohibit tools, the affected features are removed.
- **G2 — Unwritten intent of the single-tab lock (F4).** Unknown whether it exists for technical reasons only (shared local storage) or to discourage multiple sessions. Asked in Q3.
- **G3 — "Cheating" is undefined (F3).** Unknown whether a status dashboard would count.

## 4. Decision

**GO (Tier A).** No published Fourvale rule prohibits multi-accounting or third-party tools (F1). Under spec §3.3 the project proceeds under the working policy in §3.1–3.2.

Conditions:

1. ~~G1 must be closed by the player before any live multi-account use~~ Closed 2026-10-01: multi-accounting is allowed (§5a). A Discord rule prohibiting multi-accounting is a stop condition.
2. Tier C is never built.
3. Separate WebView2 profiles are used for isolation, not to get around F4. If the developers say the single-tab lock is meant to limit concurrent sessions per person, treat that as a multi-accounting restriction and stop or shrink the project.

**Accepted risk:** the player proceeds without explicit confirmation from Fourvale, while bans exist (F2) and rules are unpublished. Use accounts that can be actioned during the beta without great loss.

## 5. Developer message

**Status:** drafted 2026-10-01, **not sent**. Suggested channel: the Fourvale Discord (a support/questions channel or a DM to a developer/moderator, whichever the server rules ask for).

> Hi! I'd like to check a few things before I build a small personal tool for Fourvale:
>
> 1. Is it allowed for one person to play several accounts at the same time? Is there a limit?
> 2. Is it OK to use a tool that opens several Fourvale sessions in one window (each with its own separate browser profile) and shows their status (HP, activity, etc.) in a dashboard? It only reads what the game already shows; it never plays for me.
> 3. I noticed the game pauses when opened in a second tab. Is that only a technical limit, or are you trying to limit how many sessions one person runs?
>
> I'll follow whatever you say. Thanks!

| Field | Value |
|---|---|
| Date sent | `____` |
| Channel / recipient | `____` |
| Reply date | `____` |
| Reply summary | `____` |

## 5a. Later answers

| Date recorded | Topic | Answer | Source | Effect |
|---|---|---|---|---|
| 2026-10-01 | Multiple accounts | **Allowed** | The player reported it in the project session; the exact source (Discord rule or developer reply) was not stated. Add a link or quote when available. | Stop condition cleared. Tier A proceeds. |

## 6. Change log

| Date | Change |
|---|---|
| 2026-10-01 | Initial Phase 0 check. Decision GO (Tier A). Developer message drafted, not sent. Discord rules (G1) pending player check. |
| 2026-10-01 | Player confirmed multi-accounting is allowed; G1 closed for multi-accounting. Third-party tools are still unconfirmed. |
| 2026-10-06 | Marked as AI-generated and probably inaccurate. Trimmed to what the app actually does; features the Fourvale creator has ruled out are not discussed here (the project's rules are in `CLAUDE.md`). |
