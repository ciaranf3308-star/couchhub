# RedSilk LingLing Edition v0.7.0 — Deployment Handoff for Codex

**For:** Codex (installation agent on the real Windows mini PC)
**Hardware:** MINISFORUM UM790 Pro, Ryzen 9 7940HS, Radeon 780M, 32 GB,
Windows 11 x64, Xbox-compatible wireless controller, HDMI TV.
**Build status:** source complete, **compilation NOT verified** — the
authoring environment is Linux with no `dotnet` and no WPF. Build it on
Windows first (see below) and treat the first on-device run as the real
verification pass.

## 1. Build on Windows

Prerequisite: .NET 10 SDK. `dotnet build` restores one NuGet package
(Velopack, for the installer + auto-updates) — the build machine needs
network access to nuget.org for the first restore.

```powershell
cd <repo root>
dotnet build .\CouchHub.sln -c Release
# or publish directly:
powershell -ExecutionPolicy Bypass -File .\publish.ps1
```

Publish output: `.\publish\CouchHub\` — a self-contained win-x64 folder.
No trimming, no AOT, not single-file. If the build fails, fix the error
there and report it back; do not silently swap frameworks or add packages.

## 2. Install via Setup.exe (not the portable folder)

v0.4 adds auto-updates, and **only the installed copy updates itself**.
CI publishes `CouchHub-win-Setup.exe` to the public update feed on every
push to `main`:

1. Download `CouchHub-win-Setup.exe` from
   `https://ciaranf3308-star.github.io/couchhub-updates/couchhub/CouchHub-win-Setup.exe`
   (that URL also proves the feed is live). If it 404s, the feed publish
   step in CI failed — report that back instead of installing the
   portable folder.
2. Run it. Per-user install to `%LOCALAPPDATA%\CouchHub\`, no admin.
   It creates Start Menu / Desktop shortcuts; use those from now on.
3. Run RedSilk once. Confirm the dashboard appears fullscreen with
   four tiles and the clock is correct.
4. First run creates `%AppData%\CouchHub\config.json` with empty
   launch targets. Tiles will show "Setup required" until step 3.

The portable `publish\CouchHub\` folder is still built by CI as an
artifact, but it is **not** the install path any more: a portable copy
never checks for updates, so installing it would silently opt the TV
out of auto-updates. Only use it as a fallback if Setup.exe itself
fails, and say so in the report.

Updates after this install are automatic: RedSilk checks the feed on
startup, stages a newer version in the background, and applies it on
the next launch. Settings also has a manual "Check for updates" button.
A staged update means the next launch briefly restarts into the new
version — that is expected, not a crash. Config and logs live outside
the versioned app folder (`%AppData%\CouchHub\config.json` and
`%LOCALAPPDATA%\CouchHub\logs\`), so updates do not wipe settings.

**v0.6.0 arrives the same way:** if v0.5.1 is already installed, the
next launch stages v0.6.0 and the launch after that runs it — no
reinstall needed. **Do NOT run Setup.exe over an existing install** —
let it auto-update. Verify the version in Settings ("Current version
0.6.0"). v0.6.0 is the shell build: the Windows tile now starts
Explorer on demand, return home ends the desktop session, and closing
the dashboard hides to the tray instead of exiting (see §8 for the
shell test plan — the policy flip itself is your job on the PC, not
part of this build). If you are installing fresh, use the
`CouchHub-win-Setup.exe` from the feed as above (it is the v0.6.0
installer once CI has published it). On first run v0.5.1 migrates the
config from `%LOCALAPPDATA%\CouchHub\config.json` to
`%AppData%\CouchHub\config.json` automatically (old file left in
place) — confirm the new location exists after the first launch.

**v0.7.0 arrives the same way:** if v0.6.0 is already installed, the
next launch stages v0.7.0 and the launch after that runs it — no
reinstall needed. Verify the version in Settings ("Current version
0.7.0"). v0.7.0 is the followed-series notifications build: the TV
tile shows an unread badge when RedSilk TV reports new episodes
(see §9 for the test plan — it needs a hand-written
`notifications.json` until the TV app writes the real one, and the
TV tile must point at the RedSilk TV exe, not HotPlayer, for the
deep-link step).

## 3. Configure the three launch targets

Do this in RedSilk Settings (Menu button / Settings button), or by
editing `%AppData%\CouchHub\config.json` while RedSilk is closed.
Use the real paths on that PC — **no paths are hardcoded in the source**,
so if a path below is wrong for the actual install, use the real one.

| Tile | Expected target | Notes |
| --- | --- | --- |
| Gaming | ES-DE executable, e.g. `C:\ES-DE\ES-DE.exe` | Confirm the real install path on the PC. Leave arguments empty unless ES-DE needs them. |
| TV | **HotPlayer (Microsoft Store app)** — already installed on this PC and already playing the user's IPTV subscription | HotPlayer has **no .exe path**. Do not guess one. Find its real Store target on the PC (see §3a below) and paste that into the TV field in Settings, or point the TV field at a `.lnk` shortcut to HotPlayer. The subscription lives **inside HotPlayer**, not in RedSilk. RedSilk stores no HotPlayer credentials — never put one in `config.json` or in launch arguments. The TV tile also shows a live now / next guide (§3b) — free, no setup needed. |
| YouTube | FreeTube, e.g. `C:\FreeTube\FreeTube.exe` | Confirm the real install path on the PC (installer default is under `%LOCALAPPDATA%\Programs\FreeTube\` — paste the full absolute path). VPN / ad-blocking belongs to FreeTube or the network, not CouchHub. |

Also confirm in Settings: fullscreen dashboard on, scaling = System
(change only if text is too small/large at 3 m), return-home shortcut
= `Ctrl+Alt+Home`.

## 3a. HotPlayer — finding its Store target (do this on the PC)

HotPlayer is a UWP / Microsoft Store app, so there is no executable
file to browse to, and RedSilk must launch it by Store activation.
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
RedSilk's TV field at that `.lnk` file instead. RedSilk launches
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
  returns no Process handle, so there is no window for RedSilk to
  maximise. HotPlayer owns its own window state. Test whether it
  opens / restores maximised, and maximise it inside HotPlayer if
  needed. Record what it does; do not claim RedSilk maximises it.
- Return home from inside HotPlayer must be tested explicitly (see
  §5): Ctrl+Alt+Home, View+Menu held 2 s, and the tray icon.
  RedSilk keeps polling View+Menu while an external app is in
  front, but whether the controller reaches RedSilk while HotPlayer
  has focus is untested — record the real result per method.

## 3b. Live TV guide (v0.5 — no setup needed by default)

The TV tile now shows the real now / next programmes for a channel,
with a progress bar for the current programme. The default source is a
**free public XMLTV guide** (open-epg, Ireland file, RTÉ One) — no
account, no credentials, nothing to configure. RedSilk downloads the
guide on launch, caches it, and refreshes every 30 minutes. It needs
network access to `www.open-epg.com` from the mini PC; if that is
blocked, the tile degrades honestly (LIVE badge stays, programme rows
hide, "Live guide unavailable right now") — that is expected behaviour,
not a bug, so do not work around it by inventing a schedule.

- In Settings → "Live TV guide" you can switch the guide (Ireland /
  United Kingdom) and the channel (picked from the guide file's own
  channel list), and press **"Test guide"** to see what the guide
  actually returns for the current settings.
- The optional "Provider guide" fields (Xtream Codes server / username
  / password / channel name) are **only** for using the user's own IPTV
  provider's guide instead of the free one. Leave them blank unless the
  user asks for the provider guide. If they are filled in, the password
  lives in the local `config.json` on the PC only — never paste real
  provider credentials anywhere else, never put them in the example
  config, and check the log contains no password after testing.

## 4. Autostart (only after the app is validated)

Leave autostart **off** until steps 2–3 and the checks in §5 pass.
Then enable it in Settings ("Start RedSilk when Windows signs in").
That creates a shortcut in the current user's Startup folder —
reversible from Settings, no admin, no shell change. Do not enable it
by editing the registry.

**Shell mode replaces autostart, it does not add to it.** Once the
Custom User Interface policy is set (§8), Winlogon launches RedSilk
as the shell at sign-in — the Startup-folder shortcut is then
redundant (a second launch would just exit via single-instance) and
should be left off. Do not enable both.

## 5. Verify on the hardware (checklist — report results back)

- [ ] Dashboard launches fullscreen at 1080p and at 4K (if the TV does 4K); text readable from ~3 m.
- [ ] Controller: D-pad and left stick navigate, A launches, B goes to desktop (starts it if it is not running, in shell mode), Menu opens Settings. Unplug/replug mid-session works.
- [ ] Keyboard: arrows / Enter / Esc work; mouse clicks work; no double-launch from one press.
- [ ] Each tile launches its app; already-running app is focused, not duplicated; missing app shows a friendly error (test by temporarily pointing a tile at a non-existent path, then restore it).
- [ ] **TV / HotPlayer specifically:** TV tile launches HotPlayer from the `shell:AppsFolder\…!App` target (or the `.lnk` fallback) — record which method was used and the real target string. Launching it a second time while it is running brings the existing window forward, not a second copy. HotPlayer still plays the existing subscription; mouse navigation inside HotPlayer works as before. Record whether it opens maximised, and if not, what you did in HotPlayer to maximise it.
- [ ] **Live TV guide (v0.5):** the TV tile shows a real now programme (title + channel + time range + progress bar) and Next / Later rows that match the actual broadcast at the time you look — compare against the real TV guide, do not just confirm that text is present. In Settings → "Live TV guide", press "Test guide" and record what it returns. Switch the channel to BBC One London and confirm the guide switches to the UK file and shows BBC One programmes. If the guide shows nothing, check network access to `www.open-epg.com` first and record the log lines — do not fill in a schedule by hand.
- [ ] **Hero look (v0.5):** the four tiles are photographic (hunter / talk-show stage / mountain lake / glass bloom), the focused tile has a glowing border + soft glow + slight lift, and the footer shows the controller status and the A / B / Menu / Home hints. Judge it at 3 m and record what looks off. **Red Silk (v0.5.1) is the only theme:** the dashboard sits over the burgundy silk photograph, the header reads RedSilk / LINGLING EDITION, and there is no theme picker in Settings — do not look for one.
- [ ] Return home: Ctrl+Alt+Home from the desktop and from inside a launched app; tray icon double-click; View+Menu held 2 s. **Test all three from inside HotPlayer specifically**, as well as from ES-DE and FreeTube. **Note the honest limitation:** the View+Menu shortcut is polled by RedSilk and may not fire while a fullscreen game owns the controller exclusively. Record what actually happens per app/game — do not claim it works everywhere.
- [ ] Windows tile: if the desktop is already running, the tile hides RedSilk to it; if not (shell mode), the tile starts Explorer and the taskbar appears. Return home (hotkey / tray / View+Menu held 2 s) restores RedSilk and ends the desktop session — the taskbar goes away. Pressing the Windows tile again starts Explorer fresh.
- [ ] **Followed-series notifications (v0.7.0):** see §9 — badge
  appears with a hand-written `notifications.json`, click opens the
  New episodes view, a row deep-links into the TV app, and a
  missing/malformed file means no badge and no crash.
- [ ] **Close-to-tray (v0.6.0):** closing the dashboard window (X button / Alt+F4) hides RedSilk to the tray instead of exiting — the tray icon stays visible and clicking it restores the dashboard. Only tray → Exit RedSilk or Settings → Exit quits for real. This matters most in shell mode, where an accidental close must never black-screen the PC.
- [ ] **Shell (only after the policy is set — §8):** tile-start, return-home-end, and close-to-tray all verified per §8, recovery rehearsed first.
- [ ] HDMI: unplug/replug the TV, confirm the dashboard reappears fullscreen on the right display.
- [ ] Settings persist across a restart; a deliberately malformed `config.json` is backed up and RedSilk starts with defaults (then restore the good config).
- [ ] Single instance: launching `CouchHub.exe` twice leaves one dashboard — and the second launch must **not** wipe the configured launch targets or crash on exit (v0.5.0 bug, fixed in v0.5.1: the second instance touches neither config nor the mutex).
- [ ] **Config location (v0.5.1):** after the first launch, `config.json` lives at `%AppData%\CouchHub\config.json` (Roaming). If an older install had it at `%LOCALAPPDATA%\CouchHub\config.json`, v0.5.1 copies it once on first run and leaves the old file in place.
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
- Themes: v0.5.1 is Red Silk only — one theme, no picker, no Hotel.
  The dashboard background is the burgundy silk photograph
  (`Assets/redsilk-lingling.jpg`, compiled into the app). The theme
  has **not** been seen rendered — judge header/footer legibility over
  the photo on the TV at 3 m; the image is dark burgundy and the text
  is ivory, so it should read cleanly with no extra scrim.
- Hero look: v0.5 replaces the flat icon tiles with full-bleed
  photographic tiles (original generated art, compiled into the app).
  The focused tile glows; the TV tile shows the live guide. None of
  this has been seen rendered — judge it on the TV at 3 m.
- Live guide: v0.5 shows real now / next / later from a free XMLTV
  guide (open-epg Ireland, RTÉ One by default), refreshed every
  30 minutes, cached locally. If it cannot download, the tile hides
  the programme rows — that is by design, never a reason to invent
  a schedule. The optional Xtream provider override is off by default;
  its password lives in the local config.json only and must never
  appear in a log — verify that after any provider test.
- Updates: v0.4 adds Velopack auto-update (feed:
  `https://ciaranf3308-star.github.io/couchhub-updates/couchhub/releases.win.json`).
  Install via Setup.exe (§2) or the TV will not update itself. After
  any future push to `main`, the next RedSilk launch should pick the
  new version up — verify the version in Settings ("Current version …")
  after an update lands, and report it. The Velopack pack id and exe
  stay `CouchHub` / `CouchHub.exe` — only the display name is RedSilk.
- **Shell mode (v0.6.0):** the Windows tile starts Explorer on
  demand and return home ends the desktop session RedSilk started —
  but only for the Explorer RedSilk itself started. If RedSilk
  restarts (crash, update, or manual exit), it has lost track of the
  desktop session and leaves Explorer alone rather than guessing —
  see §8. Closing the dashboard hides to the tray; only a deliberate
  exit quits. None of this is hardware-tested yet (see §8).
- **HotPlayer with no Explorer running is unverified** — the TV tile
  launches a Store app while Explorer is not the shell; §8 step 5
  covers it.
- **Automatic (PIN-free) sign-in is a separate, unverified check** —
  not covered by this build, do not attempt it as part of the shell
  work (see §8 step 6).
- The tray icon uses `System.Drawing.SystemIcons.Application` as its
  icon in v0.1; a branded RedSilk icon can replace it later.

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

## 8. Windows shell test plan (v0.6.0 — Codex does this on the PC)

v0.6.0 is the RedSilk side of running RedSilk as the Windows shell.
Flipping the policy and proving it on the hardware is this section —
do not skip steps, and do not do step 2 before step 1.

**Step 0 — spare account first.** Do all of this on a spare local
account before touching the main account. The Custom User Interface
policy is per-user, so a mistake on the spare account cannot lock
out the main one.

**Step 1 — rehearse recovery FIRST (before the policy is set).**
With RedSilk running normally, prove the escape hatch works:
Ctrl+Alt+Del → Task Manager → File → Run new task → type
`explorer.exe` → OK, and the desktop/taskbar come back. Also prove
a second account with a normal shell still signs in to a normal
desktop — that account is the fallback if anything goes wrong on
the shell account. Do not proceed until both are proven.

**Step 2 — set the policy (spare account).** `gpedit.msc` →
User Configuration → Administrative Templates → System →
Custom User Interface → Enabled, and set the shell to the installed
RedSilk exe path — normally
`%LocalAppData%\CouchHub\current\CouchHub.exe` (confirm the exact
path from the installed Start-menu shortcut first; Velopack keeps
the current version under `current\`). The equivalent registry
value is the per-user Winlogon `Shell` value; either route is the
same policy. Sign out and back in: RedSilk should appear fullscreen
with no taskbar and no desktop behind it. If RedSilk fails to start,
use the step-1 recovery (Ctrl+Alt+Del → Task Manager → Run
`explorer.exe`) to get a desktop back, then fix the path.

**Step 3 — Windows tile starts the desktop.** On the shell account,
select the Windows tile: Explorer should launch and the taskbar +
desktop appear. B / Esc from the dashboard should do the same.
Record how long the taskbar takes to appear. If the taskbar never
appears, RedSilk should restore itself after ~10 s with a plain
error — record that too.

**Step 4 — return home ends the desktop session.** From the desktop,
press Ctrl+Alt+Home, and separately try the tray icon and View+Menu
held 2 s: each should restore RedSilk to the foreground AND make
the taskbar/desktop go away. Pressing the Windows tile again after
returning home must launch Explorer fresh (it is gone by then).
Check Task Manager: the `explorer.exe` RedSilk started should be
gone after return home, and no other Explorer should have been
touched.

**Step 5 — HotPlayer with no Explorer running.** From the shell
account, before ever selecting the Windows tile, select the TV tile:
HotPlayer must still launch and play the existing subscription.
This is the one genuinely unverified interaction in shell mode —
a Store app launching with no Explorer as shell. Record exactly
what happens (launches / plays / maximised or not).

**Step 6 — NOT covered by this build: automatic (PIN-free)
sign-in.** Do not attempt it as part of the shell work. It is a
separate, unverified check — the user still signs in normally for
now, and RedSilk appears after sign-in.

**Known shell-mode edges (do not "fix" these on the PC):**
- If RedSilk restarts (crash, auto-update, or a deliberate exit)
  while the desktop is up, it has lost track of the desktop session
  and deliberately leaves Explorer alone rather than guessing —
  the desktop stays up and the Windows tile will just hide RedSilk
  to it. That is the safe failure mode, by design.
- Deliberately exiting RedSilk (tray → Exit RedSilk, Settings →
  Exit) while the desktop is up leaves the desktop up, so an exit
  can never strand the user on a black screen.
- A staged Velopack update still applies on the next launch; the
  app restarting itself for an update while it is the shell is
  expected.
- The Startup-folder autostart (§4) is redundant once the policy
  is set — leave it off.

## 9. Followed-series notifications test plan (v0.7.0 — Codex does this on the PC)

v0.7.0 is the launcher side of the followed-series contract with
RedSilk TV. **The TV app does not write `notifications.json` yet** —
until it ships, test with a hand-written file in exactly the shape
the TV app will produce. Nothing below needs the TV app to be
installed except step 4, which needs the RedSilk TV exe.

The file is `%LOCALAPPDATA%\RedSilk\integration\notifications.json`:

```json
{
  "schemaVersion": 1,
  "lastCheck": "2026-11-10T10:00:00Z",
  "unreadCount": 2,
  "notifications": [
    {
      "seriesId": "series-123",
      "seriesTitle": "Test Show",
      "season": 2,
      "episode": 4,
      "episodeTitle": "The Test Episode",
      "detectedAt": "2026-11-10T09:30:00Z",
      "posterUrl": ""
    },
    {
      "seriesId": "series-456",
      "seriesTitle": "Second Show",
      "season": 1,
      "episode": 1,
      "episodeTitle": "",
      "detectedAt": "2026-11-10T09:00:00Z"
    }
  ]
}
```

**Step 1 — badge appears.** With RedSilk running, create the
integration folder and write the file above. Within about a second
the TV tile must show a small wine "2 new" badge (top right of the
tile, next to the LIVE badge) and a "✦ New episodes available" chip
under the guide rows, and the footer must gain a "Y — New episodes"
hint. No restart, no polling wait — the watcher picks the change up
live. Record whether it appears, and how quickly.

**Step 2 — view opens from the badge, the chip, and Y.** Click the
badge: the New episodes view opens listing both shows with
"Series 2 · Episode 4 — The Test Episode" and "Series 1 ·
Episode 1", each with its detected date, and the second row shows
no episode title (it was empty — it must not invent one). Close it
(Esc / Close button), then open it again from the chip, then again
with Y on the controller and Y on the keyboard. In the view, D-pad
up/down must move between rows, A must pick the focused row, B must
close. Clicking the badge or chip must NOT also launch the TV app
(the tile underneath must not fire).

**Step 3 — file absent / malformed = no badge, no crash.** Delete
the file: the badge, chip and footer hint must disappear within
about a second and RedSilk must keep running. Then write garbage
into the file (e.g. `{not json`), then a wrong schema version
(`"schemaVersion": 99`), then an empty object (`{}`): in every case
there must be no badge and no crash, and the log
(`%LOCALAPPDATA%\CouchHub\logs\`) should have one short warning
line about the file — no file content in the log. Restore the good
file from step 1 afterwards and confirm the badge comes back.

**Step 4 — deep link opens the TV app at the right series.** This
step needs the TV tile pointed at the **RedSilk TV exe** (a real
`.exe` path in Settings → TV), not the HotPlayer Store target —
a Store target cannot take command-line arguments, and the
deep-link step must show a plain error in that case instead of
launching (verify that too, before repointing the tile, if the
tile still points at HotPlayer). With the tile pointed at the TV
exe, open the New episodes view and pick "Test Show": the TV app
must launch with `--open-series series-123 --episode 2:4` and open
at that series/episode. Pick "Second Show": it must launch with
`--open-series series-456 --episode 1:1`. If a notification has no
season/episode, the launch must be `--open-series <id>` with no
`--episode` flag. Confirm the deep link actually lands on the
right series in the TV app — do not just confirm the app opened.
Note: the TV app's own single-instance forwarding is the TV app's
job; if it is already running, the link should still land on the
right series.

**Step 5 — TV app not configured.** Point the TV tile at nothing
(empty path) and pick a row in the view: a plain error ("TV is
not set up yet…") and no crash, and the view stays open. Restore
the real TV path afterwards.

**Step 6 — badge survives return home.** With unread episodes
showing, go to the Windows desktop and return home (Ctrl+Alt+Home):
the badge must still be there. Quit RedSilk properly (tray →
Exit RedSilk) and relaunch: the badge must be there on startup too
(the file is read once at startup).
