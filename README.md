# Fourvale Multibox Manager

A Windows desktop app for playing several [Fourvale](https://fourvale.com/) accounts at once from one window. It hosts each account in its own isolated browser profile, reads the game state the browser already receives, and shows it so one player can manually control several characters comfortably.

**The player makes every decision.** The app never plays the game: no automation, no macros, no input mirroring, no detection evasion. See [Play policy](#play-policy).

## Features

- **2–5 isolated sessions** side by side, each with its own login (one WebView2 profile per slot).
- **Layouts:** *Grid* (equal 16:9 tiles in the shape that makes the games largest) and *Focus* (one large game plus small live tiles). Switching is instant and flicker-free, and every slot keeps running.
- **Keyboard:** `1`–`5` focus a slot, `Alt` cycles to the next slot, `Space` jumps to the character that acts next (READY first, then anyone whose battle just ended, otherwise the lowest timer), and `Alt+Enter` toggles borderless fullscreen. Shortcuts stay off while you type in chat or a login field.
- **Party overlay:** HP, SP and the action timer of every character. Transparent, click-through, shown only in battle (or always). Configured in *Settings*.
- **Live state window** and soak logging for diagnostics: connection, adapter health, decode errors, browser memory and CPU.

## Requirements

- Windows 10 1809 or later
- [.NET 10 SDK](https://dotnet.microsoft.com/)
- Microsoft Edge WebView2 Runtime (included with current Windows)

## Getting started

```powershell
git clone <this repository>
cd FourvaleMultiboxManager
tools\scripts\start.cmd          # builds the latest code, then starts the app
```

Or build and test manually:

```powershell
dotnet build FourvaleMultibox.slnx
dotnet test FourvaleMultibox.slnx
dotnet run --project src/Multibox.App
```

Click a slot number in the top bar to open a slot, then log in to Fourvale in it. Logins are kept per slot.

## Configuration

`src/Multibox.App/multibox.json`: slots, shortcuts, freshness limits, layout and overlay defaults.
`src/Fourvale.Adapter/fourvale-adapter.json`: what network traffic is observed and how it is redacted.

The app stores its own data under `%LocalAppData%\FourvaleMultibox\` (browser profiles, logs, soak CSVs) and `%AppData%\FourvaleMultibox\app-settings.json` (open slots, layout, overlay). Nothing in the repository contains account data.

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
  scripts/             start.cmd, fixture generator
tests/                 Unit tests and sanitised fixtures
docs/                  Specification, policy, discovery report, decision records (ADRs)
```

Data flows one way: **WebView2 session → adapter → state store → UI**. The UI never reads raw game data.

## How it reads the game

Passive observation of the network traffic each session already receives, through the Chrome DevTools Protocol in WebView2. Fourvale uses Colyseus; room state is decoded with a small schema v2 decoder (`src/Fourvale.Adapter/Colyseus`). Request headers and bodies are never read, and login traffic is dropped, so passwords and tokens never reach the app. A tiny read-only page script reports only whether a text field has focus. Shortcuts are read from the app's own message loop; there is no system-wide keyboard hook. See `docs/discovery.md` and `docs/decisions/`.

## Play policy

- Multi-accounting: allowed (`docs/policy.md`).
- Input mirroring or broadcasting: **not allowed**. It was removed from the code and must not be added back.
- Every feature only organises information or moves keyboard focus between your own sessions. One physical input is one action in one game client, made by you. See `docs/spec.md` §3 and the hard rules in `CLAUDE.md`.

## Status

Fourvale is in beta and this project tracks it. Current phase and open items: see the top of `CLAUDE.md`.

## Disclaimer

- **Unofficial.** This is a fan project. It is not affiliated with, endorsed by or supported by Fourvale or its developers.
- **No cheating.** This project does not condone cheating, botting, automation or any other way of gaining an unfair advantage. It is built to help one player manually control their own accounts, and it deliberately leaves out anything that plays for you or sends input on your behalf.
- **Follow Fourvale's rules.** You are responsible for following Fourvale's terms, rules and guidelines, and those of its community (for example its Discord). If Fourvale does not allow something this software does, or does not allow using several accounts, do not use it for that. Fourvale's rules always take precedence over this project.
- **Use at your own risk.** You alone are responsible for how you use this software and for what happens to your accounts. The authors are not liable for any account action, ban, loss of progress or other consequence of using it. See the warranty and liability disclaimer in [LICENSE](LICENSE).
- **If you modify it,** you are responsible for what your version does. Please keep it fair: do not add automation, input mirroring or anything that hides the tool from the game.

## License

[MIT](LICENSE): you may use, copy, modify and share this software freely, as long as the copyright notice is kept. It is provided "as is", without warranty of any kind.
