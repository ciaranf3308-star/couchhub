# CouchHub v0.1 — Windows Living-Room Launcher

A lightweight, native Windows entertainment dashboard for a MINISFORUM
UM790 Pro mini PC connected to a TV. Power on, sign in, CouchHub appears
fullscreen with four large tiles — Gaming, TV, YouTube, Windows — and you
drive it with an Xbox controller, keyboard, or mouse. Windows stays
underneath; nothing about the shell is replaced.

> **Build status — read this first:** this project was authored on Linux,
> where `dotnet` is not installed and WPF is Windows-only, so **compilation
> has NOT been verified here**. The code targets .NET 10 WPF APIs only and
> uses no third-party packages, but you must build it on Windows with the
> .NET 10 SDK before calling it verified. See "Build" below and
> `CODEX_HANDOFF.md` for what Codex must still test on the real hardware.

## What it does

| Tile | Behaviour |
| --- | --- |
| **Gaming** | Launches ES-DE (configurable .exe path). Empty path → tile shows "Setup required". |
| **TV** | Launches HotPlayer (Microsoft Store app) via its `shell:AppsFolder\…!App` target — see Configuration below. It has no .exe path. |
| **YouTube** | Launches FreeTube (configurable .exe path). |
| **Windows** | Hides CouchHub to expose the normal desktop. Return with Ctrl+Alt+Home, the tray icon, or View+Menu held 2 s. |

HotPlayer notes: it is already installed on the UM790 Pro and already
plays the user's IPTV subscription — CouchHub only launches it, it never
touches the subscription, and no IPTV credentials go in CouchHub.
HotPlayer currently needs a mouse to navigate inside the app;
controller-to-mouse mapping is **not** in v0.1 (deferred polish, do not
add it here). Return home from HotPlayer is via Ctrl+Alt+Home, View+Menu
held 2 s, or the tray icon — Codex must test all three from inside
HotPlayer on the hardware. CouchHub cannot force-maximise a Store app
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
View+Menu controller shortcut is watched by CouchHub's background poll
and **may not fire while a fullscreen game owns the controller exclusively**
— that is untested on hardware and is flagged for Codex in
`CODEX_HANDOFF.md`. The tray icon and the keyboard shortcut are the
reliable fallbacks, and CouchHub never force-kills an external app.

## Configuration

Stored at `%LOCALAPPDATA%\CouchHub\config.json`, created automatically
on first run. No application paths are hardcoded in the source — the
defaults are empty and Codex fills them in on the PC (see
`config.example.json`). Gaming and YouTube take plain .exe paths;
TV takes HotPlayer's Store target
(`shell:AppsFolder\<PackageFamilyName>!App`, found on the PC with
`Get-AppxPackage *HotPlayer*`) or a .lnk shortcut to HotPlayer —
both are accepted by the launcher and by Settings validation.

Settings (in-app, controller-navigable) cover: Gaming / TV / YouTube
paths and arguments, fullscreen dashboard, display scaling
(System / 125% / 150% / 200%), startup at sign-in, return-home shortcut,
controller status, and Exit.

- A malformed `config.json` is backed up (`config.json.corrupt-*.bak`)
  and defaults are used instead — CouchHub does not crash.
- **Never store IPTV passwords or other credentials in CouchHub.**
  Launch arguments are never written to the log.

Logs: `%LOCALAPPDATA%\CouchHub\logs\couchhub-YYYY-MM-DD.log`
(old logs pruned after 14 days).

## Project structure

```
CouchHub.sln
src/CouchHub/CouchHub.csproj      # net10.0-windows, UseWPF + UseWindowsForms (tray icon only)
src/CouchHub/App.xaml(.cs)        # lifecycle, single-instance Mutex, unhandled-exception logging
src/CouchHub/MainWindow.xaml(.cs) # dashboard, tiles, clock, hotkey, tray, hide/show
src/CouchHub/SettingsWindow.xaml(.cs)
src/CouchHub/Theme.xaml           # centralised colours / styles — future themes go here
src/CouchHub/Config/             # AppConfig, ConfigService (JSON, safe recovery)
src/CouchHub/Input/              # XInputService (dynamic XInput1_4 → 1_3 → 9_1_0), InputService
src/CouchHub/Launcher/           # ProcessLauncher (validate, focus-existing, launch, maximise)
src/CouchHub/Services/           # Logger, WindowHelper (Win32), StartupService (Startup-folder .lnk)
```

Design choices worth knowing:

- **No process supervisor.** A short-lived launcher (Steam, a `.lnk`)
  exiting is not treated as the app exiting. CouchHub restores only on
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
- **Single instance** via a named mutex (`Global\CouchHub.SingleInstance.v1`).

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
automatic updates, shell replacement, kiosk lockdown, and
controller-to-mouse mapping (HotPlayer needs a mouse for now — deferred
polish). CouchHub launches other apps; it does not become them.

## Deployment

See [`CODEX_HANDOFF.md`](CODEX_HANDOFF.md) — paths to configure for
ES-DE / HotPlayer / FreeTube, autostart, and the full untested-on-hardware
list (controller, return-home, HDMI, performance).
