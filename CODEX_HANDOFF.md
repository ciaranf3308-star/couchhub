# CouchHub v0.2 — Deployment Handoff for Codex

**For:** Codex (installation agent on the real Windows mini PC)
**Hardware:** MINISFORUM UM790 Pro, Ryzen 9 7940HS, Radeon 780M, 32 GB,
Windows 11 x64, Xbox-compatible wireless controller, HDMI TV.
**Build status:** source complete, **compilation NOT verified** — the
authoring environment is Linux with no `dotnet` and no WPF. Build it on
Windows first (see below) and treat the first on-device run as the real
verification pass.

## 1. Build on Windows

Prerequisite: .NET 10 SDK.

```powershell
cd <repo root>
dotnet build .\CouchHub.sln -c Release
# or publish directly:
powershell -ExecutionPolicy Bypass -File .\publish.ps1
```

Publish output: `.\publish\CouchHub\` — a self-contained win-x64 folder.
No trimming, no AOT, not single-file. If the build fails, fix the error
there and report it back; do not silently swap frameworks or add packages.

## 2. Install (portable, no admin)

1. Copy the whole `publish\CouchHub\` folder to the mini PC, e.g.
   `C:\Apps\CouchHub\`. No installer, no admin rights needed.
2. Run `CouchHub.exe` once. Confirm the dashboard appears fullscreen
   with four tiles and the clock is correct.
3. First run creates `%LOCALAPPDATA%\CouchHub\config.json` with empty
   launch targets. Tiles will show "Setup required" until step 3.

## 3. Configure the three launch targets

Do this in CouchHub Settings (Menu button / Settings button), or by
editing `%LOCALAPPDATA%\CouchHub\config.json` while CouchHub is closed.
Use the real paths on that PC — **no paths are hardcoded in the source**,
so if a path below is wrong for the actual install, use the real one.

| Tile | Expected target | Notes |
| --- | --- | --- |
| Gaming | ES-DE executable, e.g. `C:\ES-DE\ES-DE.exe` | Confirm the real install path on the PC. Leave arguments empty unless ES-DE needs them. |
| TV | **HotPlayer (Microsoft Store app)** — already installed on this PC and already playing the user's IPTV subscription | HotPlayer has **no .exe path**. Do not guess one. Find its real Store target on the PC (see §3a below) and paste that into the TV field in Settings, or point the TV field at a `.lnk` shortcut to HotPlayer. The subscription lives **inside HotPlayer**, not in CouchHub. CouchHub stores no IPTV passwords or credentials — never put one in `config.json` or in launch arguments. |
| YouTube | FreeTube, e.g. `C:\FreeTube\FreeTube.exe` | Confirm the real install path on the PC (installer default is under `%LOCALAPPDATA%\Programs\FreeTube\` — paste the full absolute path). VPN / ad-blocking belongs to FreeTube or the network, not CouchHub. |

Also confirm in Settings: fullscreen dashboard on, scaling = System
(change only if text is too small/large at 3 m), return-home shortcut
= `Ctrl+Alt+Home`.

## 3a. HotPlayer — finding its Store target (do this on the PC)

HotPlayer is a UWP / Microsoft Store app, so there is no executable
file to browse to, and CouchHub must launch it by Store activation.
**Do not invent or guess the PackageFamilyName** — read it off the
installed app:

```powershell
Get-AppxPackage *HotPlayer* | Select-Object Name, PackageFamilyName
Get-StartApps | Where-Object Name -like "*HotPlayer*"
```

The TV target is then:

```
shell:AppsFolder\<PackageFamilyName from the command above>!App
```

Test it first by hand before putting it in CouchHub, e.g. paste it
into Run (Win+R) or run `explorer.exe shell:AppsFolder\<…>!App` —
HotPlayer should open (and if it is already running, that same
activation should bring its existing window forward, not start a
second copy). If the App id after `!` is not `App` on this install,
use the value `Get-StartApps` / the app's actual AppUserModelId shows.

**Fallback if Store activation misbehaves:** in Windows, right-click
HotPlayer in Start → create a desktop shortcut (`.lnk`), and point
CouchHub's TV field at that `.lnk` file instead. CouchHub launches
`.lnk` targets with `UseShellExecute` and that path is already
supported. Report which method you used.

**HotPlayer integration notes (confirmed, not assumptions):**

- Already installed on the UM790 Pro; the user's IPTV subscription
  is already configured in HotPlayer and plays. Do not reinstall it,
  do not re-enter the subscription, and do not change its playlist.
- HotPlayer currently requires **mouse navigation** inside the app.
  Do **not** add controller-to-mouse mapping to CouchHub — that is
  deferred polish, out of scope for v0.1.
- CouchHub cannot force-maximise a Store app: shell activation
  returns no Process handle, so there is no window for CouchHub to
  maximise. HotPlayer owns its own window state. Test whether it
  opens / restores maximised, and maximise it inside HotPlayer if
  needed. Record what it does; do not claim CouchHub maximises it.
- Return home from inside HotPlayer must be tested explicitly (see
  §5): Ctrl+Alt+Home, View+Menu held 2 s, and the tray icon.
  CouchHub keeps polling View+Menu while an external app is in
  front, but whether the controller reaches CouchHub while HotPlayer
  has focus is untested — record the real result per method.

## 4. Autostart (only after the app is validated)

Leave autostart **off** until steps 2–3 and the checks in §5 pass.
Then enable it in Settings ("Start CouchHub when Windows signs in").
That creates a shortcut in the current user's Startup folder —
reversible from Settings, no admin, no shell change. Do not enable it
by editing the registry or replacing the shell.

## 5. Verify on the hardware (checklist — report results back)

- [ ] Dashboard launches fullscreen at 1080p and at 4K (if the TV does 4K); text readable from ~3 m.
- [ ] Controller: D-pad and left stick navigate, A launches, B goes to desktop, Menu opens Settings. Unplug/replug mid-session works.
- [ ] Keyboard: arrows / Enter / Esc work; mouse clicks work; no double-launch from one press.
- [ ] Each tile launches its app; already-running app is focused, not duplicated; missing app shows a friendly error (test by temporarily pointing a tile at a non-existent path, then restore it).
- [ ] **TV / HotPlayer specifically:** TV tile launches HotPlayer from the `shell:AppsFolder\…!App` target (or the `.lnk` fallback) — record which method was used and the real target string. Launching it a second time while it is running brings the existing window forward, not a second copy. HotPlayer still plays the existing subscription; mouse navigation inside HotPlayer works as before. Record whether it opens maximised, and if not, what you did in HotPlayer to maximise it.
- [ ] Return home: Ctrl+Alt+Home from the desktop and from inside a launched app; tray icon double-click; View+Menu held 2 s. **Test all three from inside HotPlayer specifically**, as well as from ES-DE and FreeTube. **Note the honest limitation:** the View+Menu shortcut is polled by CouchHub and may not fire while a fullscreen game owns the controller exclusively. Record what actually happens per app/game — do not claim it works everywhere.
- [ ] Windows tile: desktop is normal Explorer, apps work, CouchHub returns via hotkey/tray.
- [ ] HDMI: unplug/replug the TV, confirm the dashboard reappears fullscreen on the right display.
- [ ] Settings persist across a restart; a deliberately malformed `config.json` is backed up and CouchHub starts with defaults (then restore the good config).
- [ ] Single instance: launching `CouchHub.exe` twice leaves one dashboard.
- [ ] Performance: record idle CPU % and private working set (Task Manager) on the UM790 Pro, and warm startup time. Targets are ~0% idle / <150 MB / <2 s — record the real numbers, do not assume them.
- [ ] Logs exist at `%LOCALAPPDATA%\CouchHub\logs\` and contain no passwords or launch arguments.

## 6. Known limitations / untested (do not claim otherwise)

- Compilation, controller behaviour, return-home shortcuts, HDMI
  reconnect, and performance were **not tested** on Windows hardware
  during this build (Linux authoring environment).
- XInput is loaded dynamically (`xinput1_4.dll` → `xinput1_3.dll` →
  `xinput9_1_0.dll`); if none loads, CouchHub runs keyboard/mouse-only
  and says so in Settings.
- Fullscreen for launched apps is best-effort (maximise the main window
  after launch); apps that manage their own fullscreen own that behaviour.
  For HotPlayer specifically there is no Process handle to maximise at
  all (Store activation), so CouchHub cannot maximise it — see §3a.
- HotPlayer's PackageFamilyName / App id were **not** looked up during
  this build (no access to the mini PC, and no reliable public value
  was found) — Codex must read them off the installed app (§3a).
  Nothing in the source or example config contains a real HotPlayer
  target; the example is a clearly-marked placeholder.
- Return home is explicit, not automatic on process exit — by design,
  so Steam-style short-lived launchers are not mistaken for the app exiting.
- The tray icon uses `System.Drawing.SystemIcons.Application` as its
  icon in v0.1; a branded CouchHub icon can replace it later.

## 7. If something is wrong

- Check `%LOCALAPPDATA%\CouchHub\logs\couchhub-YYYY-MM-DD.log` first.
- Config problems: rename `config.json`, relaunch (defaults regenerate),
  then re-enter the three paths in Settings.
- Hotkey not working: another app may own Ctrl+Alt+Home — change the
  shortcut in Settings, or use the tray icon.
- Do not install, bundle, or modify ES-DE, FreeTube, HotPlayer, or
  Steam as part of CouchHub itself; configure their paths / targets only.
  Do not add controller-to-mouse mapping for HotPlayer — deferred polish.
- HotPlayer will not launch / Settings will not save the TV target:
  check the exact `shell:AppsFolder\…!App` string first (a wrong family
  name or App id is the most likely cause), test it in Run, and fall
  back to a HotPlayer `.lnk` shortcut if Store activation fails.
