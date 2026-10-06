# Fourvale Multibox Manager

A Windows app for playing several [Fourvale](https://fourvale.com/) accounts at once from one window. Each account runs in its own isolated browser profile, side by side, with a party overlay that shows every character's HP, SP and action timer.

**You make every decision.** The app never plays the game: no automation, no macros, no input mirroring, no detection evasion. See [Play policy](#play-policy).

## Features

- **2–5 accounts side by side**, each with its own login that is remembered between starts.
- **Layouts:** *Grid* (equal tiles, as large as the screen allows) and *Focus* (one large game plus small live tiles). Every game keeps running.
- **Keyboard:** `1`–`5` focus a slot, `Alt` moves to the next slot, `Alt+Enter` toggles borderless fullscreen. Shortcuts are off while you type in chat or a login field.
- **Party overlay:** HP, SP and the action timer of every character, on top of the game. Transparent and click-through; shown only in battle, always, or never.

## Download and start

1. Download `FourvaleMultibox-<version>-win-x64.zip` from the [Releases](https://github.com/Tusse098/FourvaleMultiboxManager/releases) page.
2. Unzip it anywhere (for example `Documents\FourvaleMultibox`).
3. Run `Multibox.App.exe`.

Windows may show *"Windows protected your PC"* because the app is not code-signed. Click **More info → Run anyway**. The full source code is in this repository.

Requires Windows 10 (1809) or later with the Microsoft Edge WebView2 Runtime, which current Windows already includes. No other installs are needed.

## First start

1. Click a slot number (`1`–`5`) in the top bar to open a slot.
2. Log in to Fourvale in that slot. Each slot has its own login and stays logged in after a restart (see [Accounts and logins](#accounts-and-logins)).
3. Open more slots the same way. Close a slot with **✕** on its panel.
4. Choose **Grid** or **Focus** in the top bar. Click a game, or press its number, to play in it.

## Controls

| Key / button | What it does |
|---|---|
| `1` … `5` | Focus that slot (the large tile in Focus layout) |
| `Alt` (tap) | Next slot |
| `Alt+Enter` | Borderless fullscreen; touch the top edge of the screen to show the top bar |
| **Unstick keys** | If shortcuts stop working, this resets them |
| **Overlay** / **Settings** | Party overlay options; untick *Click-through* to drag the overlay into place |

Shortcuts can be changed under `"shortcuts"` in `multibox.json` next to the exe. F-keys and the game's own keys (Q, I, C, M, Enter, Esc, Tab, arrows) cannot be used without Ctrl or Alt.

## Accounts and logins

**The app has no account list and never sees your password.** Each slot is a separate built-in browser (Microsoft Edge WebView2) with its own profile, like separate Edge profiles. You log in on Fourvale's own login page inside the slot, and staying logged in works exactly as it does in a normal browser:

- **What is saved:** the login session that Fourvale itself gives the browser after you log in (its cookies and site storage). It is saved in that slot's profile only, so slot 1 and slot 2 never share a login.
- **What is not saved:** your password. The built-in browser's password saving is off, and the app does not read login traffic, so it never reaches the app or its logs.
- **Which account is in which slot:** whichever account you logged in to in that slot. The app does not track or store account names; the slot simply stays logged in until Fourvale's session ends or you log out.
- **Where:** `%LocalAppData%\FourvaleMultibox\WebView2\EBWebView\WV2Profile_slot1` (and `…slot2`, `…slot3`, …). Paste `%LocalAppData%\FourvaleMultibox` into the File Explorer address bar to open it.

**To switch accounts in a slot,** log out in the game and log in with the other account. **To remove a slot's login completely,** close the app and delete that slot's `WV2Profile_slotN` folder; the slot starts fresh, logged out, the next time you open it.

Anyone who can use your Windows account can open the app and play the logged-in slots, the same as with a browser that remembers your logins. On a shared PC, delete the profile folders when you are done.

## Other data

- Settings (open slots, layout, overlay): `%AppData%\FourvaleMultibox\app-settings.json`
- Logs, with passwords and tokens removed: `%LocalAppData%\FourvaleMultibox\logs\`

To remove everything, delete the app folder, `%LocalAppData%\FourvaleMultibox` and `%AppData%\FourvaleMultibox`.

## Play policy

- Multi-accounting: allowed ([`docs/policy.md`](docs/policy.md)).
- Input mirroring or broadcasting: **not allowed**. It is not in the app and must not be added back.
- Every feature only organises information or moves keyboard focus between your own sessions. One physical input is one action in one game client, made by you.

## Disclaimer

- **Unofficial.** This is a fan project. It is not affiliated with, endorsed by or supported by Fourvale or its developers.
- **No cheating.** This project does not condone cheating, botting, automation or any other way of gaining an unfair advantage. It is built to help one player manually control their own accounts, and it deliberately leaves out anything that plays for you or sends input on your behalf.
- **Follow Fourvale's rules.** You are responsible for following Fourvale's terms, rules and guidelines, and those of its community (for example its Discord). If Fourvale does not allow something this software does, or does not allow using several accounts, do not use it for that. Fourvale's rules always take precedence over this project.
- **Use at your own risk.** You alone are responsible for how you use this software and for what happens to your accounts. The authors are not liable for any account action, ban, loss of progress or other consequence of using it. See the warranty and liability disclaimer in [LICENSE](LICENSE).
- **If you modify it,** you are responsible for what your version does. Please keep it fair: do not add automation, input mirroring or anything that hides the tool from the game.

## For developers

Building from source, the release script, configuration and project layout: [`docs/development.md`](docs/development.md).

## License

[MIT](LICENSE): you may use, copy, modify and share this software freely, as long as the copyright notice is kept. It is provided "as is", without warranty of any kind.
