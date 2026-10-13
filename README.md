# XivDesktop

<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="images/readme/hero-dark.png">
    <source media="(prefers-color-scheme: light)" srcset="images/readme/hero-light.png">
    <img src="images/readme/hero-dark.png" width="100%" alt="XivDesktop project banner">
  </picture>
</p>

<img src="images/icon.png" width="96" height="96" align="right" alt="XivDesktop icon">

**[Site](https://spacegho.st/mods/ffxiv/xivdesktop/) · [Install](https://spacegho.st/mods/ffxiv/plugins/) · [Vote on what's next](https://spacegho.st/mods/ffxiv/xivdesktop/vote/) · [Screenshots](https://spacegho.st/mods/ffxiv/term/gallery/?mod=xivdesktop) · [Changelog](CHANGELOG.md)**

**Your Linux applications, organized inside FINAL FANTASY XIV.**

XivDesktop is a Dalamud launcher for the Linux host's apps: search, favourites,
recents, a keyboard-first palette, window/workspace controls and optional Umbra
widgets. **It does not render or stream application windows.** A compatible
[Ghostty for Dalamud](https://github.com/Spaceghost/ghostty-dalamud) build and
its host agent supply the compositor and world panels.

> **Status: experimental, not verified in game.** The project targets Dalamud
> API 15 / .NET 10. Host tests exercise catalog/search, calculator, key chords,
> workspaces and IPC payloads; the scanner has been exercised on a Linux host.
> These do not verify Wine input, a running Ghostty backend, visible windows,
> NPC/session interfaces or either Umbra widget. In-game descriptions below
> are implemented intent and checks to perform, not observed results.

[Requirements](#requirements) · [Install](#install-from-the-plugin-repository) ·
[First success](#first-success) · [Commands](#in-game) ·
[Troubleshooting](#troubleshooting) · [Full guide](GUIDE.md)

## What you get

| Surface | Purpose and boundary |
| --- | --- |
| Launcher palette | Search apps and panels, run actions, or use a bounded calculator. Opens with `/desktop`; Super+D is a configurable shortcut. |
| App grid | Icons, favourites and recent apps. A populated catalog does not prove that the agent can launch a window. |
| Workspaces and panel controls | Nine workspaces, focus, close and placement through Ghostty IPC. Advanced operations require the corresponding methods in the installed Ghostty build. |
| Umbra Apps / Windows widgets | Optional launcher and taskbar clients of XivDesktop's IPC; neither replaces the required Ghostty backend. |
| NPC / Claude interfaces | Experimental local-model dialogue and Claude Code sessions. They add their own backend, permission and runtime requirements; neither is needed for the app catalog. |

## How it fits together

```text
FINAL FANTASY XIV under Wine
  /desktop / palette / optional Umbra widgets
        |
  XivDesktop plugin
        +-- catalog: host agent's app list; filesystem scan as fallback
        +-- search, favourites, recents, workspace bookkeeping
        `-- GhosttyDalamud.v1.Call / fallback Post
                  |
          compatible Ghostty plugin --> ghostty-agent on Linux
                                           `-- compositor --> host applications
                                               windows shown as game panels
```

Applications execute **on the Linux host**, not inside Wine. XivDesktop reads
host paths through Wine when it can, but Flatpak/sandbox visibility can limit
that fallback scan. A command accepted by IPC only means it was handed off;
it is not an acknowledgement that the application displayed a window.
See [architecture](docs/ARCHITECTURE.md) for the threading and backend design.

## Requirements

| Component | Required for |
| --- | --- |
| Linux host + XIVLauncher.Core/Wine + Dalamud API 15 | The desktop-app workflow described here. This is not a native Windows app launcher. |
| Compatible Ghostty plugin, loaded in game | Window/panel IPC. A basic terminal build is not necessarily a compositor-capable build. |
| Matching `ghostty-agent`, started with `--windows wayland` | Host compositor and app launching. Start it on the host, not in Wine. |
| SDK matching [`global.json`](global.json), plus Dalamud references | Building from source. An arbitrary older .NET 10 feature band may not satisfy the pin. |

**Check the Ghostty build, not just its name.** Some companion integrations
were developed while Ghostty's newer IPC was still in progress. Installing the
older default-branch terminal does not establish `window.*`, `panel.*`,
`terminal.new`, `agent.apps` or compositor support. Follow the documentation
for the compatible Ghostty revision you actually install; do not pass newer
agent flags to an older build and assume they worked.

Without Ghostty, the catalog can still be useful, but launching is disabled.
Even when Ghostty IPC is registered, XivDesktop cannot prove from that alone
that the host agent is running.

## Install (from the plugin repository)

The author's third-party feed is:

```text
https://spacegho.st/mods/ffxiv/plugins.json
```

1. `/xlsettings` → **Experimental** → **Custom Plugin Repositories**: add the
   URL, click **+**, then save and close.
2. `/xlplugins` → **All Plugins**: search **XivDesktop** and install it.
3. Confirm the compatible Ghostty plugin and its host agent are running, then
   open `/desktop`.

XivDesktop has a stable release, so it shows up for everyone. For test builds
ahead of a release, right-click its entry → **Receive plugin testing versions**.
See the
[feed instructions](https://spacegho.st/mods/ffxiv/plugins/) and
[repository releases](https://github.com/Spaceghost/xivdesktop-dalamud/releases)
for the available build. The feed does not install a host compositor for you,
and inclusion is not official Dalamud-list approval.

## Install (dev plugin)

With the pinned SDK and Dalamud reference assemblies available:

```sh
git clone https://github.com/Spaceghost/xivdesktop-dalamud.git
cd xivdesktop-dalamud
tools/install-dev.sh
```

[`tools/install-dev.sh`](tools/install-dev.sh) builds Release and stages the
plugin **beside the checkout** at `../xiv-desktop-build/devplugin/` by default.
It is not necessarily `~/xiv-desktop-build/`: use the path the script prints.

| Override | Purpose |
| --- | --- |
| `DOTNET` | SDK executable; otherwise the script tries `~/.dotnet/dotnet`, then `dotnet` on PATH. |
| `XIVDESKTOP_ARTIFACTS` | Build-output root; default `../xiv-desktop-build/artifacts`. |
| `XIVDESKTOP_STAGE` | Staged plugin folder; default `../xiv-desktop-build/devplugin`. |

1. `/xlsettings` → **Experimental** → **Dev Plugin Locations**: add the printed
   Wine path to `XivDesktop.dll`, then save and close.
2. `/xlplugins` → **Dev Tools** → **Installed Dev Plugins**: enable
   **XivDesktop** and **Start on boot**. Dev plugins begin disabled.
3. `/desktop` opens the launcher. After rebuilding/staging, reload XivDesktop
   through `/xlplugins` or its automatic-reloading option.

The installer does not edit Dalamud's configuration or copy files into
`~/.xlcore`. Keep the staged files together. Recheck build references after
Dalamud updates rather than assuming a previous build remains compatible.

## First success

Start with the catalog and calculator before debugging a compositor:

```text
/desktop status
/desktop apps
/desktop =2+2
```

Check that status reports an app count, the grid contains an app you recognise,
and the calculator offers **4**. This tests different layers: plugin loading,
catalog access and palette logic. It does not require launching an arbitrary
shell command.

Next select a known installed graphical app and launch it. Success means a
**visible, responsive window panel**, not merely `ok` in a status message.
`/desktop windows` helps distinguish a registered panel from a missing or
pending one. If the catalog works but launching does not, inspect the Ghostty
agent and its compositor before changing XivDesktop's search settings.

For a host-only catalog check, from the checkout with `dotnet` on PATH:

```sh
dotnet run --project tools/scan
dotnet run --project tools/scan -- --all --missing
```

That scans Linux paths directly. Passing it does not establish what a
sandboxed game can see through Wine.

## In game

| Command | Purpose |
| --- | --- |
| `/desktop`, `/desktop <text>` | Toggle the palette, or open it with a query. |
| `/desktop apps [search]` | Open the app grid. |
| `/desktop settings` | General, keybind, window and workspace settings. |
| `/desktop doctor` | Check the catalog, Ghostty IPC, agent and focus; optionally test an app launch. |
| `/desktop status`, `/desktop windows` | Catalog/backend summary, or panel listing in chat. |
| `/desktop launch <id or search>` | Launch by desktop-file ID, exact name or best match. |
| `/desktop ws <1-9>` | Switch workspace. |
| `/desktop veil [on|off|toggle]` | Control the privacy veil (Super+V by default). |
| `/desktop stash` | Put the focused panel in the scratchpad. |
| `/desktop scratchpad` | Summon the scratchpad here; one panel toggles, several cycle. |
| `/desktop reload` | Rescan application directories off the framework thread. |

App-grid favourites and recents are stored in `pluginConfigs/XivDesktop.json`.
Right-click an app for favourites, launch, the actions listed by its `.desktop` file (such as New Window), or copying its shell command. **Copy game macro** copies `/desktop launch <desktop-file id>` for a hotbar macro; the id stays stable across rescans. The palette offers the same app actions and **Copy macro** (Ctrl+Shift+C). Actions require access to the `.desktop` file through the directory scan; an app known only to the host agent has its normal launch action. A missing icon
can be a letter tile rather than a broken entry: the icon loader supports PNG,
not SVG.

Settings → Windows also lets you assign a workspace to each job. A real job change selects that workspace once; your manual switches remain until the next job change. An unmapped job keeps the current workspace.

The same tab offers optional stowing during combat, while inside a duty, and when the duty confirmation window appears. Select apps to keep visible, such as voice chat. Panels opened while stowed are hidden too; leaving the condition restores the previously visible panels after a short settle delay. Workspace assignments stay intact. These switches start off.

The scratchpad is separate from the nine numbered workspaces. **Super+Shift+Minus** stashes the focused panel; **Super+Minus** summons it on your current workspace. A single panel toggles; multiple panels cycle. Stashed panels stay hidden across workspace switches, even if normal workspace hiding is off. Assignments are saved, but summoned state is temporary, so the scratchpad starts hidden after reload.

**The privacy veil** (Super+V or `/desktop veil`) masks desktop panel titles in the palette, window list, taskbar IPC, and panel notifications. Settings → Windows lets you mark private apps and individual desktop panels; these are hidden while the veil is active. Group pose enables the veil by default, and leaving group pose preserves a veil you enabled manually. Private tags are saved, with panel tags rebound by title and app after reconnect. Duty-stowing exceptions do not override private-panel hiding. This covers XivDesktop’s UI and panels it manages; other plugins’ UI and existing notifications are outside the veil.

Use **Open file with…** on an app in the grid or **Ctrl+O** on its palette row to select up to 32 existing files. The app's `.desktop` file must be visible to the directory scan. Wine paths on `Z:` are mapped back to Linux; other Wine drives are refused. File and URL field codes are expanded as separate shell-quoted arguments. Apps accepting a single file are launched once per file; apps with `%F` or `%U` receive them together. Terminal apps retain the normal terminal launch route.

`/desktop doctor` opens the diagnostic window. Refreshing checks never launches an app. It reports catalog counts and scan time, the launch and window IPC gates, host-agent connectivity, and the latest focus response. **Copy redacted report** omits paths, host names, app commands, window titles and raw errors. To test a launch, explicitly select a harmless installed graphical app and press the launch-test button. A queued request passes only after its matching request resolves and its panel is live; you still need to confirm that the app renders and accepts input in game.

## Launcher palette (Super+D)

| Prefix | Search |
| --- | --- |
| `a:` | Applications. |
| `w:` | Panels, falling back to windows on an older backend. |
| `=` | Calculator. |
| `>` | Commands and actions. |

Up/Down or Ctrl+J/K selects, PageUp/PageDown jumps, Enter runs and Esc closes.
Panel rows expose actions for focus, pet/pin, HUD placement, minimize, close
and order. **Not every backend implements every action**; some panel/HUD
shapes remain integration assumptions. The full keyboard-action table and
fallback behavior are in [the detailed guide](GUIDE.md).

The calculator parses bounded arithmetic and listed math functions; it is not
a general code evaluator. Its input and nesting limits are part of the design.
The command provider is different: choosing a command can execute an actual
plugin/game command and should be treated accordingly.

## Key chords

Settings → Keybinds offers **Record** beside every chord. Press your combination while the game has focus; Escape cancels. Normal launcher chords are suspended while recording. After ten seconds a timeout says whether any modifiers reached the game, which helps diagnose Wine or desktop shortcut interception.

Common defaults are **Super+D** for the palette, **Super+Enter** for a terminal,
**Super+Q** to close the target window, **Super+1…9** to switch workspace, and
**Super+Shift+1…9** to move the target panel. The selected Ghostty backend must
support the requested terminal/window operation.

Configure chords in **Settings → Keybinds**; an empty chord unbinds it. Chords
match exact modifiers and fire once per press. Linux window managers may
consume Super before Wine receives it. Use `/desktop` to check that the plugin
works, then choose **Alt+Shift** or **Ctrl+Alt** using the modifier/reset controls
and check for game keybind conflicts.

**Read keys globally** changes how keyboard state is read while the game is
foreground. It is not proof that Wine will deliver a particular key; while a
panel owns input, the panel app may receive the chord too. The documented
escape route is **Esc twice** to return keyboard control to the game, but this
also needs target-environment testing. See [the guide](GUIDE.md) for exact
panel keys, input ownership and fallback limitations.

## Workspaces and optional Umbra widgets

XivDesktop tracks nine workspaces and asks Ghostty to show/hide or move panels.
Treat hiding as a presentation operation, not a promise that an application
stopped executing. Inspect the actual window state when changing workspaces.

The optional **Apps** widget provides search, favourites and recents; **Windows**
provides a taskbar and workspace controls. Their layout and Umbra integration
remain unverified, including a reflection-based popup workaround. They use
XivDesktop IPC rather than a separate app-launching implementation.
Installation and interaction details remain in [GUIDE.md](GUIDE.md).

## Classic games: the XivArcade mod

`/arcade`, a library of your own classic games with saves kept in step across machines, lives in its
own mod, [XivArcade](https://github.com/Spaceghost/xivarcade-dalamud). It does not need XivDesktop, and
XivDesktop does not need it. When both are installed, the launcher palette finds your games by name
(an `Arcade` badge) through XivArcade's `XivArcade.v1.Search` and `XivArcade.v1.Launch` IPC. Not
verified in game.

## Experimental NPC and Claude interfaces

| Interface | Role |
| --- | --- |
| `/npc <question>` | **In progress.** The Talk-style dialogue works; the speaker's body does not appear in the world yet ([what was tried](docs/NPC-SPAWN.md)). Client-side speaker and dialogue backed by Almanac's local gateway. `/npc bye` dismisses; `/npc who` opens the picker. |
| `/claude` / `/claude <prompt>` | Claude Code session UI with streaming text, tool cards and permission dialogs through Ghostty agent jobs. |

These are not dependencies of the desktop catalog and are **not verified in
game**. Claude Code sessions are not the same as Almanac's local-model chat;
their configured backend, authentication and data handling are separate.
Session persistence/re-attachment and NPC spawning are intended behavior, not
observations established by host tests.

Read [NPC dialogue: backend and risks](docs/ASK.md) and
[Claude sessions: setup, permissions and limitations](docs/CLAUDE_IN_GAME.md)
before enabling them. `/npc` avoids competing with Ghostty's `/ask`; optional
command claiming must not be mistaken for a universally available integration.

## IPC and companion boundaries

[`src/Shared/IpcContract.cs`](src/Shared/IpcContract.cs) defines the versioned
`XivDesktop.v1.*` gates. The [full guide](GUIDE.md) preserves the complete
signatures, JSON examples, threading rules and action payloads.

| Group | Gates |
| --- | --- |
| Catalog and launcher | `ListApps`, `Launch`, `Status`, `ToggleLauncher`, `ToggleFavourite` |
| Panels and workspaces | `Windows`, `Workspace`, `Palette`, `WindowAction` |
| Dialogue | `Ask` |

**Queued is not completed.** `Launch` returning `ok` means Ghostty accepted the
handoff, not that a window appeared. Workspace/action changes are reflected
in subsequent state. Search results are descriptions; use action gates to act.

Providing these IPC gates does not mean a corresponding tool exists in your
installed [XivMcp](https://github.com/Spaceghost/xivmcp-dalamud). Check the
client/server capabilities instead of assuming that planned MCP integration
or another branch's methods have shipped.

## Troubleshooting

| Symptom | Check first |
| --- | --- |
| `/desktop` is unknown | Enable the staged dev plugin and inspect the Dalamud log for `XivDesktop`. |
| Apps list, but launch is disabled | Ghostty's expected IPC is missing. Check the loaded plugin version, not only whether a terminal opens. |
| Launch says `ok`, but no window appears | Check the host agent, compositor, app launch and subsequent window state. IPC acceptance is not visual success. |
| Too few apps appear | Check whether the agent catalog is available. The Wine/Flatpak fallback may see only sandboxed paths; compare with the host scanner. |
| Icons are letter tiles | PNG may be absent or inaccessible; SVG-only icons are not rendered. |
| Super+D does nothing | Open `/desktop` directly, then resolve host-desktop/game key conflicts or choose another modifier. |
| Plain D opens the palette (GNOME) | GNOME takes Super for the overview, so Wine sees Super go down but not up and keeps reporting it held until the next Super press. XivDesktop now only counts Super after seeing it pressed in game (host-tested, not yet verified in game); on older builds press and release Super once, or pick another modifier. |
| A panel action fails while simpler ones work | Check the actual Ghostty method/capability. Older window-only backends do not provide all panel operations. |

Include the XivDesktop and Ghostty commits/versions, host OS, launcher packaging
(Flatpak or otherwise), Dalamud version, agent arguments, reproduction steps
and redacted errors in an [issue](https://github.com/Spaceghost/xivdesktop-dalamud/issues).
Do not share tokens, private app/session output or full personal configuration.

## Privacy and security

App entries and palette actions can launch real programs on the Linux host.
Quoting `.desktop` arguments does not sandbox the launched program. Review
unfamiliar launch commands and give the host agent only the privileges it needs.

Ghostty's token authenticates its agent connection; it is not transport
encryption. Keep that connection local or protected. App catalogs reveal
installed software, and favourites, recents, window titles, dialogue/session
state and screenshots can contain private information. Model-backed features
send selected data to their configured backend; do not assume that every
feature is local-only because the launcher itself runs in game.

## Development and limits

From the checkout, with the pinned SDK on PATH:

```sh
dotnet build
dotnet test
```

Host tests exercise pure logic and temporary filesystem fixtures, not a live
Dalamud session. Keep reported test results tied to the exact commit. Validate
Wine keys, IPC against a running Ghostty build, real app panels, workspaces and
Umbra separately before claiming in-game support.

Not implemented in the documented baseline: clipboard/file-drop integration,
saved layouts, placement per zone, app icons in the Umbra taskbar and recording
a chord by pressing it. GNOME/KDE/phone-style desktop bridges are **plans**, not
shipped integrations. See [architecture](docs/ARCHITECTURE.md) and the
[full guide](GUIDE.md) for requirements and roadmap detail.

## Releasing

```sh
tools/release.sh test            # the next testing build, from master as it is
tools/release.sh stable X.Y.Z    # the stable release X.Y.Z
```

One command: it checks the tree and CI, writes the version everywhere it lives, dates
the changelog, tags, pushes, waits for the Release workflow, and verifies the published
files and the live listing. `-n` is a dry run. See [docs/RELEASING.md](docs/RELEASING.md).

## Documentation and support

[GUIDE.md](GUIDE.md) preserves the previous detailed README at the repository
root, including catalog precedence, key tables, IPC payloads and roadmap notes;
its relative file links keep the same base. Prefer this README's corrected
staging path and explicit capability boundaries when following a quick start.

Maintained by [Spaceghost](https://github.com/Spaceghost). This is an independent
third-party project built on Dalamud and Ghostty's plugin/agent infrastructure.
Preserve dependency attribution and licensing; neither a build nor a feed
entry establishes official approval.

Named desk plates have host-tested planning and native-panel support. `/desktop plate save <name>` records live graphical and terminal apps, their workspaces, known placement arguments, and pet order; `>plate <name>` previews every piece in the palette and starts applying it. `/desktop plate list`, `delete <name>`, `status`, and `cancel` manage the saved plates and the current application. Missing apps launch one at a time, and placement and pet ordering wait for their matching queued results. Applying visits each workspace and ends on the workspace saved with the plate. Cancellation or failure keeps pieces already applied. The additive `XivDesktop.v1.Plates` and `XivDesktop.v1.ApplyPlate` IPC gates expose the same operation and status.

Terminal apps launched through XivDesktop retain their catalog identity alongside workspace membership and can be reused or relaunched when applying a plate. Plain shells and adopted UI with no catalog app identity are omitted from capture. Matching Ghostty builds report full placement arguments, including HUD coordinates and fixed-pin positions and orientation relative to the character. On older builds, pinned panels must first be placed through XivDesktop. Pins left in another territory cannot be captured until brought here. Scratchpad panels are excluded from capture, and unknown graphical apps produce an error. Applying pauses no applications and closes no unrelated windows. Duty stowing or the privacy veil cancels an application; plate details are omitted from the palette and Plates IPC while veiled. In-game restoration has not been verified.

Bind your current territory in Settings → Windows → Layouts by territory, or with `/desktop zone ws <1-9>` / `/desktop zone plate <name>`. `/desktop zone clear` removes its rule, `list` lists rules, and `duties on|off` changes that territory's duty opt-in. There are no rules by default. Arrival waits for loading to finish, a local player to be available for one second, pending graphical launches to finish, and any active plate, duty stow, or privacy veil to end. The rule then applies once, leaving subsequent manual workspace changes alone until another arrival or rule edit. Applying a plate can launch its missing apps. A territory transition cancels an unfinished plate, keeping pieces already applied. Failed plate applications report an error and are not retried continuously.

Rules currently use territory IDs exposed by IClientState. Housing wards, plots, and instanced rooms sharing that ID share the same rule; no ward/plot identity is exposed by the client-state API used here. Nothing changes in a duty unless the rule explicitly allows it, and an active duty guard still takes precedence. Host tests cover arrival timing and guards; territory transitions and housing behavior remain unverified in game.

The Umbra Windows taskbar uses resolved app icons from the additive `iconPath` fields in ListApps and Windows IPC. It shares a bounded file cache across widgets, validates images once, rechecks files after 30 seconds, and falls back to a letter tile for missing or invalid icons. Each button shows its workspace (or Bag for a scratchpad panel). Newly live panels have a thin highlight for four seconds; existing panels at initial load do not flash. The privacy veil removes window icon paths and refreshes open menu titles. SVG-only apps still need the separately voted host rasterization feature. Umbra rendering, menu behavior, and image loading in Wine remain unverified in game.

SVG icon size routing is now wired: Settings → General → App icon size (24–128) is sent through the optional icon_size parameter on agent.windows.refresh. Matching agents render their app-list SVGs at that size and preserve the requested size across subsequent app-list refreshes. The renderer is host-tested at 32 and 192 pixels, and old refresh calls retain their default behavior. Compositor compilation/runtime verification and icons outside the host app list remain unfinished.

Controller ring (in development): enable **Controller ring launcher** in Settings → General. Hold the configured chord (default `L3+R3`), flick the left stick toward one of the first eight available favourites, and release to launch. `L1`/`R1` change workspaces and `East` cancels. Those three buttons are reserved and cannot be part of the hold chord. Adjust the raw-stick deadzone for your controller. The game also receives the buttons, so choose an unbound chord. The ring cancels under duty stow, privacy veil, foreground loss, or ImGui gamepad navigation; in-game behavior has not been verified.

Experimental clipboard bridge: settings now include an opt-in text sync toggle.
The service runs on the UI thread, pauses during privacy veil/duty stow, baselines
existing contents, and waits for host-write acknowledgement. Transfers require a known-safe metadata reply. Windows agent and compositor
app offers now have metadata providers; outside-desktop Linux metadata remains
unavailable. Runtime/game verification remains open.

Game clipboard sampling now checks registered exclusion/concealed formats
before reading text and checks the Windows clipboard sequence around the read.
Missing API/access and changing samples are skipped. Any exclusion-format
presence is conservatively skipped, even if its value would permit history.
Microsoft API references: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-isclipboardformatavailable
and https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getclipboardsequencenumber
Wine behavior and agent metadata remain unverified/incomplete.

Experimental app notifications: enable **App notification toasts (opt in)** in
Settings → General. **App notifications: title only** defaults to enabled.
Matching Ghostty agents provide notifications from compositor-launched apps on
their private D-Bus session. Click a toast to focus a matching app panel, including
switching to its workspace. Notifications without a known panel remain readable
but have no focus target. The service does not capture the host desktop's bus.

Private apps and panels, privacy veil, and duty stow suppress delivery. Existing
toasts are scrubbed before dismissal when privacy changes. Replacements in one
poll show only their final state; closes dismiss their toast even during a flood.
Delivery is capped at four toasts per ten seconds. Initial connection, reconnect,
and re-enabling establish a baseline and do not replay old messages. Production
lifecycle tests pass; native-host IPC, Wine icon rendering, and game toast/focus
behavior remain unverified.
