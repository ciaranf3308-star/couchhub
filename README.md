# RedSilk LingLing Edition v0.5.1 — Windows Living-Room Launcher

A lightweight, native Windows entertainment dashboard for a MINISFORUM
UM790 Pro mini PC connected to a TV. Power on, sign in, RedSilk appears
fullscreen with four large tiles — Gaming, TV, YouTube, Windows — and you
drive it with an Xbox controller, keyboard, or mouse. Windows stays
underneath; nothing about the shell is replaced.

> **Name note:** the product is RedSilk LingLing Edition. The exe and
> the Velopack pack id stay `CouchHub` / `CouchHub.exe` on purpose —
> renaming them would break auto-updates from earlier versions.

> **Build status — read this first:** this project was authored on Linux,
> where `dotnet` is not installed and WPF is Windows-only, so **compilation
> has NOT been verified here**. The code targets .NET 10 WPF APIs only and
> uses one third-party package — Velopack, for the installer and
> auto-updates (see "Updates" below) — but you must build it on Windows
> with the .NET 10 SDK before calling it verified. See "Build" below and
> `CODEX_HANDOFF.md` for what Codex must still test on the real hardware.

## What it does

| Tile | Behaviour |
| --- | --- |
| **Gaming** | Launches ES-DE (configurable .exe path). Empty path → tile shows "Setup required". |
| **TV** | Launches HotPlayer (Microsoft Store app) via its `shell:AppsFolder\…!App` target — see Configuration below. It has no .exe path. The tile also shows a live now / next guide for the configured channel (see "Live TV guide" below) — real data only, never a made-up schedule. |
| **YouTube** | Launches FreeTube (configurable .exe path). |
| **Windows** | Hides RedSilk to expose the normal desktop. Return with Ctrl+Alt+Home, the tray icon, or View+Menu held 2 s. |

HotPlayer notes: it is already installed on the UM790 Pro and already
plays the user's IPTV subscription — RedSilk only launches it, it never
touches the subscription, and HotPlayer's subscription credentials never
go in RedSilk. (The optional live-guide provider override in Settings
is separate — see "Live TV guide" below; it stores provider credentials
in the local `config.json` on the PC only, never logs them, and only
ever sends them to the user's own provider.)
HotPlayer currently needs a mouse to navigate inside the app;
controller-to-mouse mapping is **not** in v0.1 (deferred polish, do not
add it here). Return home from HotPlayer is via Ctrl+Alt+Home, View+Menu
held 2 s, or the tray icon — Codex must test all three from inside
HotPlayer on the hardware. RedSilk cannot force-maximise a Store app
(it gets no Process handle from shell activation), so HotPlayer's window
state is its own; test whether it restores maximised and maximise it in
the app if needed.

## Controls

- **D-pad / left stick** — move between tiles (stick has a dead zone and repeat timing)
- **A / Enter** — launch · **B / Esc** — back / go to desktop
- **Menu (Start)** — open Settings
- **View + Menu held 2 seconds** — return home from a game/app
- **Ctrl+Alt+Home** — return home from anywhere (configurable in Settings)
- **Mouse** — click tiles and settings normally

Return-home honesty note: the keyboard shortcut is a real global
`RegisterHotKey` and should work while another app is in front. The
View+Menu controller shortcut is watched by RedSilk's background poll
and **may not fire while a fullscreen game owns the controller exclusively**
— that is untested on hardware and is flagged for Codex in
`CODEX_HANDOFF.md`. The tray icon and the keyboard shortcut are the
reliable fallbacks, and RedSilk never force-kills an external app.

## Configuration

Stored at `%AppData%\CouchHub\config.json` (Roaming, so it survives
reinstall/uninstall), created automatically on first run. v0.5.1
migrates a config left over at `%LOCALAPPDATA%\CouchHub\config.json`
once on first launch and leaves the old file in place. No application
paths are hardcoded in the source — the defaults are empty and Codex
fills them in on the PC (see `config.example.json`). Gaming and YouTube
take plain .exe paths; TV takes HotPlayer's Store target
(`shell:AppsFolder\<PackageFamilyName>!App`, found on the PC with
`Get-AppxPackage *HotPlayer*`) or a .lnk shortcut to HotPlayer —
both are accepted by the launcher and by Settings validation.

Settings (in-app, controller-navigable) cover: Gaming / TV / YouTube
paths and arguments, live TV guide (feed, channel, optional provider
override, test button), fullscreen dashboard, display scaling
(System / 125% / 150% / 200%), startup at sign-in, return-home shortcut,
controller status, update check, and Exit.

## Live TV guide

v0.5 shows real now / next / later programmes on the TV tile, with a
progress bar for the current programme. The default source is a **free
public XMLTV guide** (open-epg, Ireland file by default, RTÉ One) — no
account, no credentials. RedSilk downloads the guide file on launch,
caches it locally, and refreshes every 30 minutes. Settings can switch
the guide (Ireland / United Kingdom) and the channel (picked from the
guide file's own channel list), and has a "Test guide" button that shows
what the guide actually returns.

An **optional Xtream Codes override** in Settings lets the user's own
IPTV provider supply the guide instead (server URL, username, password,
channel name). When it is configured, the provider is tried first and
the free guide is the fallback. The password is stored in the local
`config.json` on the PC only, is never written to the log, and is only
ever sent to the provider's own server — never put real credentials in
`config.example.json` or in the source.

If the guide cannot be downloaded or the channel has no data, the tile
degrades honestly: the LIVE badge and channel name stay, the programme
rows hide, and a small "Live guide unavailable right now" line shows.
RedSilk never invents a programme name or time.

## Look (v0.5.1)

The dashboard is photographic: each tile is full-bleed original
art (compiled into the app — `src/CouchHub/Assets/Tiles/*.jpg`) under a
theme scrim, with the focused tile getting a glowing border, soft glow
and slight lift. Gaming has platform chips, YouTube a play overlay and
a thumbnail strip, Windows an app-icon row, TV the live guide above.
The window background is the burgundy silk photograph
(`src/CouchHub/Assets/redsilk-lingling.jpg`, also compiled in) — the
header reads RedSilk / LINGLING EDITION over it in ivory.

## Updates

v0.4 adds silent auto-updates via Velopack. CI packs every push to
`main` into an installer and publishes it to a public update feed
([couchhub-updates](https://github.com/ciaranf3308-star/couchhub-updates),
served by GitHub Pages) — the feed has to be public because the app on
the TV downloads it without a GitHub login, while this source repo stays
private.

- **First install: run `CouchHub-win-Setup.exe`** (from the feed:
  `https://ciaranf3308-star.github.io/couchhub-updates/couchhub/CouchHub-win-Setup.exe`).
  Per-user install, no admin. That is the copy that updates itself.
- On startup RedSilk checks the feed and, if a newer version is there,
  downloads and stages it in the background. The staged update applies
  the next time RedSilk launches — nothing restarts mid-session.
- Settings has a manual "Check for updates" button with a status line
  ("You're up to date" / "Update vX downloaded — applies on next launch").
- The portable `publish\CouchHub\` folder does **not** auto-update — it is
  a plain copy with no installer behind it, so it never checks the feed.
- Updates only ship on a new version: CI refuses to publish if the feed
  already has that version with different files — bump `<Version>` in
  `src/CouchHub/CouchHub.csproj` before pushing a new release.

## Theme

One theme, final: **Red Silk** — deep wine burgundy with beige bright,
a serif display face, and the burgundy silk photograph behind the
dashboard. It lives as a single whole dictionary
(`ThemeRedSilk.xaml`) defining every key the windows use; the app
loads it at startup, before the dashboard is created. There is no
theme picker in Settings and no second dictionary — the old Hotel
theme was removed in v0.5.1.

- A malformed `config.json` is backed up (`config.json.corrupt-*.bak`)
  and defaults are used instead — RedSilk does not crash.
- **Credentials:** the only credentials RedSilk ever stores are the
  optional live-guide provider ones (Settings → Live TV guide), kept in
  the local `config.json` on the PC only, never in the example file or
  the source, never written to the log, and only ever sent to the user's
  own provider. Launch arguments are never written to the log either.

Logs: `%LOCALAPPDATA%\CouchHub\logs\couchhub-YYYY-MM-DD.log`
(old logs pruned after 14 days).

## Project structure

```
CouchHub.sln
src/CouchHub/CouchHub.csproj      # net10.0-windows, UseWPF + UseWindowsForms (tray icon only)
src/CouchHub/App.xaml(.cs)        # lifecycle, single-instance Mutex, unhandled-exception logging
src/CouchHub/MainWindow.xaml(.cs) # dashboard, tiles, clock, hotkey, tray, hide/show
src/CouchHub/SettingsWindow.xaml(.cs)
src/CouchHub/ThemeRedSilk.xaml   # centralised colours / styles — the one Red Silk theme
src/CouchHub/Config/             # AppConfig, ConfigService (JSON, safe recovery)
src/CouchHub/Input/              # XInputService (dynamic XInput1_4 → 1_3 → 9_1_0), InputService
src/CouchHub/Launcher/           # ProcessLauncher (validate, focus-existing, launch, maximise)
src/CouchHub/Services/           # Logger, WindowHelper (Win32), StartupService (Startup-folder .lnk), UpdateService (Velopack auto-update), EpgService (free XMLTV guide + optional Xtream override)
```

Design choices worth knowing:

- **No process supervisor.** A short-lived launcher (Steam, a `.lnk`)
  exiting is not treated as the app exiting. RedSilk restores only on
  an explicit return-home action. It also focuses an already-running app
  instead of launching a duplicate.
- **Idle cost is kept low on purpose.** The dashboard does no continuous
  rendering; the clock ticks once a second, the gamepad poll sleeps
  50 ms while active and 200 ms while another app is in front, and the
  XInput thread is below-normal priority. Idle CPU / RAM targets
  (~0% / <150 MB) are goals to measure on the UM790 Pro, not claims —
  they have not been measured yet.
- **Startup is a user-level Startup-folder shortcut**, reversible from
  Settings, no admin, no registry Run key, no shell change.
- **Single instance** via a session-local named mutex
  (`CouchHub.SingleInstance.v1` — `Local\` scope, so it never leaks
  across user sessions). A second launch exits immediately and touches
  neither the config file nor the mutex.

## Prerequisites (Windows)

- Windows 11 x64 (Windows 10 22H2 should also work; untested)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) — build machine only
- Visual Studio 2022 17.8+ (optional; `dotnet` CLI is enough)

The target mini PC needs **no .NET runtime**: the publish script makes a
self-contained build.

## Build

```powershell
# From the repo root, on Windows:
dotnet build .\CouchHub.sln -c Release
```

## Publish (portable release)

```powershell
powershell -ExecutionPolicy Bypass -File .\publish.ps1
# Output: .\publish\CouchHub\  — copy the whole folder to the mini PC.
```

That is a normal self-contained `win-x64` publish folder (not single-file,
no trimming, no AOT — WPF trimming is not verified for v0.1).

Run `CouchHub.exe` from that folder. First run creates the config file
with empty launch targets; open Settings and point each tile at its app.

## What is NOT in v0.1 (by design)

Game library / cover scraping, recently-played tracking, any IPTV
player or stream handling, YouTube downloading, VPN, remote apps,
shell replacement, kiosk lockdown, and
controller-to-mouse mapping (HotPlayer needs a mouse for now — deferred
polish). RedSilk launches other apps; it does not become them.
(Automatic updates were on this list until v0.4 added them — see
"Updates" above.)

## Deployment

See [`CODEX_HANDOFF.md`](CODEX_HANDOFF.md) — paths to configure for
ES-DE / HotPlayer / FreeTube, autostart, and the full untested-on-hardware
list (controller, return-home, HDMI, performance).
