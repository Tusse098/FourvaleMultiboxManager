# ADR 0010 — Remove "next ready" and prepare a public release build

- **Date:** 2026-10-06
- **Status:** Accepted (player request)
- **Amends:** ADR 0004 and 0005 ("next ready" on Space removed)
- **Phase:** 3 — Multi-session. Release packaging and docs are pulled forward from Phase 9 (spec §16).

## Context

The player asked to remove the Space shortcut ("jump to the character that acts next"), to make sure no Ctrl+click
(click mirroring) code is left, and to turn the app into something the public can download and use.

## Decision

1. **Space / "next ready" removed.** Deleted: `ShortcutAction.NextReady`, the `nextReady` setting, the shortcut
   handler in `MainWindow`, `SlotNavigator.NextReady`, `NextToAct`, `IsWaitingAfterBattle`, `IsReady`, and their tests.
   Space is not an app shortcut any more and always reaches the game (test: `Game_keys_and_unbound_chords_pass_through`).
   Kept: `SlotNavigator.Cycle` (Alt), `SecondsUntilReady` (overlay action timer), `Battles.LastBattleAt` (state field).
   An old `multibox.json` with `nextReady` still loads; the unknown key is ignored.
2. **Ctrl+click audit.** No mirroring code exists (removed 2026-10-01, ADR 0006). The remaining `Ctrl` handling is the
   generic shortcut parser for chords such as `Ctrl+Q`.
3. **Developer tools off by default.** Settings → Advanced → *Developer tools* (`DeveloperTools` in
   `app-settings.json`, default `false`) shows the Live state window and browser task manager buttons and writes the
   soak log. Off: the Live state window does not open on start and no soak CSV is written. The redacted app log is
   always written, for bug reports. Turn it on for the Phase 3 soak test.
4. **Self-contained release.** `tools/scripts/publish.ps1` runs the tests, publishes a self-contained win-x64 folder
   (not single-file, so `WebView2Loader.dll` stays a normal file and antivirus has less to object to), and zips it to
   `artifacts/FourvaleMultibox-<version>-win-x64.zip`. Version `0.1.0` in `Multibox.App.csproj`, shown in Settings.
   The exe is not code-signed; the README explains the SmartScreen prompt.
5. **Docs.** `README.md` is for players (download, first start, controls, data, policy, disclaimer); build and
   architecture notes moved to `docs/development.md`.

## Consequences

- Phase 3 exit criteria (2-hour soak, RAM/CPU budget) are still open; a public release does not close them.
- Publishing a GitHub Release is an outward-facing step and is done only on the player's go-ahead.
- Smoke test 2026-10-06: the published exe started from a clean copy on spare slot 8, opened the slot, wrote no soak
  file with developer tools off, and closed cleanly.
