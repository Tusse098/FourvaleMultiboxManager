# ADR 0012 — Release hardening: error handler, pinned SDK

- **Date:** 2026-10-06
- **Status:** Accepted (player request)
- **Phase:** 3. From Phase 9 (polish), pulled forward for the first public release (ADR 0010).

## Decisions

1. **Global error handler** (`App.xaml.cs`). Before this, an error nothing caught (e.g. a broken `multibox.json`) made
   the app disappear without a word. Now:
   - Dispatcher, AppDomain and unobserved-task errors are written to `%LocalAppData%\FourvaleMultibox\logs\crash-yyyyMMdd.log`
     through the `Log` wrapper with redaction. Only the exception type and stack trace are written, never the message
     (it can echo game data), except for our own settings errors (`InvalidDataException`, `JsonException`).
   - WPF's `XamlParseException` and `TargetInvocationException` wrappers are unwrapped, so the player sees the real
     cause (verified: "multibox.json: intervals must be positive.").
   - During start the app shows the message and closes; later errors show a message and keep the games running. At
     most one message every 10 seconds.
   - Release builds ship without `.pdb` files, so crash logs contain no source paths.
2. **`global.json` pins a stable SDK** (10.0.401, `latestFeature`, no prereleases). The development PC had only a
   preview SDK (`10.0.400-preview`), and releases were compiled with it. Developers need `winget install Microsoft.DotNet.SDK.10`.

## Rejected

- **A per-slot "Clear login" button** (spec §13: "users can sign out or delete a slot's profile from the app") was built
  and removed the same day at the player's request: logging out is Fourvale's own job inside each slot, and the app
  should not build on top of the game's login. Players log out in the game, or delete a slot's profile folder (README).
  Spec §13's sentence is superseded by this.

## Not done

- 4–5 slots are not tested extensively (Phase 3 soak and RAM/CPU budget still open); the README says so.
