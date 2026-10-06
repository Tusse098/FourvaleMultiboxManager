# Development

How to build, test and package Fourvale Multibox Manager. Players don't need this; see the [README](../README.md).

Working rules (hard rules, architecture boundaries, current phase) are in [`CLAUDE.md`](../CLAUDE.md). The specification is [`docs/spec.md`](spec.md).

## Requirements

- Windows 10 1809 or later
- [.NET 10 SDK](https://dotnet.microsoft.com/)
- Microsoft Edge WebView2 Runtime (included with current Windows)

## Build, test, run

```powershell
dotnet build FourvaleMultibox.slnx
dotnet test FourvaleMultibox.slnx
dotnet run --project src/Multibox.App      # or tools\scripts\start.cmd
```

## Release build

```powershell
powershell -ExecutionPolicy Bypass -File tools\scripts\publish.ps1
```

Runs the tests, then publishes a self-contained win-x64 folder to `artifacts\publish\FourvaleMultibox\` and zips it to
`artifacts\FourvaleMultibox-<version>-win-x64.zip`. Players unzip it and run `Multibox.App.exe`; no .NET install is
needed. The version comes from `<Version>` in `src/Multibox.App/Multibox.App.csproj` and is shown in Settings.
The exe is not code-signed, so Windows SmartScreen warns on first start.

## Configuration

`src/Multibox.App/multibox.json`: slots, shortcuts, freshness limits, layout and overlay defaults.
`src/Fourvale.Adapter/fourvale-adapter.json`: what network traffic is observed and how it is redacted.

The app stores its own data under `%LocalAppData%\FourvaleMultibox\` (browser profiles, logs, soak CSVs) and
`%AppData%\FourvaleMultibox\app-settings.json` (open slots, layout, overlay, developer tools). Nothing in the
repository contains account data.

**Developer tools** (Settings → Advanced, off by default): the Live state window, the browser task manager button and
the soak log (`%LocalAppData%\FourvaleMultibox\soak\`). Turn it on for the Phase 3 soak test.

## Project layout

```text
src/
  Fourvale.Adapter/    The only place that knows Fourvale's formats: network observation, redaction,
                       Colyseus schema v2 decoder, FourvaleSession (traffic -> state fields)
  Multibox.Core/       Slots, Field<T> with freshness (UNKNOWN when stale), state store, navigation rules
  Multibox.Hosting/    WebView2 plumbing: profiles, DevTools channel, typing watcher, metrics
  Multibox.App/        The WPF app
tools/
  Fourvale.Capture/    Discovery tool: records sanitised traffic per slot
  Fourvale.Replay/     Replays a capture through the decoder
  scripts/             start.cmd, publish.ps1, fixture generator
tests/                 Unit tests and sanitised fixtures
docs/                  Specification, policy, discovery report, decision records (ADRs)
```

Data flows one way: **WebView2 session → adapter → state store → UI**. The UI never reads raw game data.

## How it reads the game

Passive observation of the network traffic each session already receives, through the Chrome DevTools Protocol in
WebView2. Fourvale uses Colyseus; room state is decoded with a small schema v2 decoder (`src/Fourvale.Adapter/Colyseus`).
Request headers and bodies are never read, and login traffic is dropped, so passwords and tokens never reach the app.
A tiny read-only page script reports only whether a text field has focus. Shortcuts are read from the app's own message
loop; there is no system-wide keyboard hook. See `docs/discovery.md` and `docs/decisions/`.
