# Development

How to build, test and package Fourvale Multibox Manager. Players don't need this; see the [README](../README.md).

Working rules (hard rules, architecture boundaries, current phase) are in [`CLAUDE.md`](../CLAUDE.md). The specification is [`docs/spec.md`](spec.md).

## Requirements

- Windows 10 1809 or later
- [.NET 10 SDK](https://dotnet.microsoft.com/), stable **10.0.401 or newer**. `global.json` pins it and refuses
  preview SDKs, so every build (yours, CI, releases) uses a released compiler: `winget install Microsoft.DotNet.SDK.10`
- Microsoft Edge WebView2 Runtime (included with current Windows)

## Build, test, run

```powershell
dotnet build FourvaleMultibox.slnx
dotnet test FourvaleMultibox.slnx
dotnet run --project src/Multibox.App      # or tools\scripts\start.cmd
```

## Checks on GitHub (CI)

Every push to `main` and every pull request is built and tested on a clean Windows machine
(`.github/workflows/ci.yml`, SDK from `global.json`). Results: the Actions tab, and ✅/❌ next to each commit.
Don't release from a commit whose check is red.

## Making a release

Releases are built by GitHub, never uploaded by hand. Pushing a version tag starts `.github/workflows/release.yml`,
which runs the tests, builds the zip with `tools/scripts/publish.ps1`, adds a SHA-256 checksum and creates a
**draft** release. Nothing is public until the draft is published.

**Version numbers** (`MAJOR.MINOR.PATCH`): new features → raise MINOR and set PATCH to 0 (0.1.0 → 0.2.0); only fixes →
raise PATCH (0.2.0 → 0.2.1). Stay below 1.0.0 while Fourvale and this app are in beta; 0.x releases are marked as
pre-releases automatically. Never reuse a version number that was already released.

**Steps:**

1. **Check `main`:** the CI check on the latest commit is ✅, and the changes were tried in the real app.
2. **Set the version:** change `<Version>` in `src/Multibox.App/Multibox.App.csproj`, commit
   (`Release 0.2.0`) and push.
3. **Tag and push the tag** (the tag must match `<Version>`, with a `v` in front; otherwise the build stops):
   ```powershell
   git tag v0.2.0
   git push origin v0.2.0
   ```
4. **Wait for the Release workflow** (about 5 minutes) on the Actions tab. If it fails, see *If a release build fails*.
5. **Review the draft** on the Releases page:
   - the zip and `.sha256` are attached and the title shows the right version;
   - the notes read well (download text from `.github/release-notes.md`, then the changes GitHub listed);
   - download the zip, unzip it, start `Multibox.App.exe` and check that Settings shows the new version.
6. **Publish** the draft. Only the project owner does this.

**If a release build fails:** fix the problem on `main`, then move the tag to the fixed commit, as long as the release
was never published:
```powershell
git tag -d v0.2.0
git push origin :refs/tags/v0.2.0     # delete the tag on GitHub (also delete a half-made draft there, if any)
git tag v0.2.0
git push origin v0.2.0
```
After a release is published, never move or reuse its tag: make a new PATCH version instead.

**For Claude (and anyone helping):** prepare steps 1–3 and the checks in step 5, but pushing a release tag, publishing a
draft, and deleting or editing a published release only happen when the project owner says so.

### Local release build

```powershell
powershell -ExecutionPolicy Bypass -File tools\scripts\publish.ps1 [-SkipTests]
```

The same build the workflow runs, for trying a build before tagging. It writes `artifacts\publish\FourvaleMultibox\`,
`artifacts\FourvaleMultibox-<version>-win-x64.zip` and the zip's `.sha256`. Players unzip it and run
`Multibox.App.exe`; no .NET install is needed. The exe is not code-signed, so Windows SmartScreen warns on first start.

## Configuration

`src/Multibox.App/multibox.json`: slots, default shortcuts, freshness limits, layout and overlay defaults. Players change
shortcuts in Settings; their choices are saved in `app-settings.json` (`Shortcuts`; absent = the defaults) and win over
`multibox.json`.
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
