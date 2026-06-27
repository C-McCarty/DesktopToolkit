# Desktop Toolkit

A PowerToys-style suite of Windows desktop utilities. A single host — one tray icon and
one dashboard — discovers and supervises independent **modules**, each a self-contained
executable described by a `module.json` manifest. Drop a new module folder into `modules/`
and it appears in the dashboard with no host recompile.

> This repository began as a standalone Python monitor-arranging tool (now retired to
> [`legacy/`](legacy/)) and was grown into the suite described here.

## Modules

| Module | Kind | What it does |
|---|---|---|
| **Monitor Arrangement** | window | Arrange monitors with pixel precision — drag, snap to grid/edges, type exact offsets, identify, profiles, apply without reboot. |
| **Taskbar Manager** | background | Show/hide the taskbar per monitor; maximized windows reclaim the freed space. |
| **Animated Wallpaper** | background | Play a GIF/video as a live desktop background behind the icons (LibVLC, hardware-decoded), assigned per-display **or spanning several displays**, auto-pause on fullscreen. |

`window` modules launch on demand from the dashboard; `background` modules run while
enabled and are restarted by the host if they die. Either way, a card's **Configure**
button opens that module's window (its settings/manager, or the app itself).

## Architecture

```
DesktopToolkit.sln
  src/Host/
    Toolkit.Common/     Shared library: manifest model + loader, named-pipe IPC,
                        SettingsStore, MonitorService, StartupManager, ModuleSupervisor.
    Toolkit.Runner/     WPF host: single tray icon + dashboard, schema-driven settings.
  src/Modules/          Module source projects.
  modules/              Built module folders the host discovers at runtime (one per module,
                        each with a module.json). Binaries here are git-ignored.
  tools/                deploy-modules.ps1, publish.ps1, Toolkit.SmokeTest.
  legacy/               The original Python tool (superseded).
```

- **Settings** live in one file: `%AppData%\DesktopToolkit\settings.json` (a section per
  module), including Animated Wallpaper's rich per-monitor media settings. Because the host
  and modules are separate processes writing this one file, `SettingsStore.Save()` merges
  over the on-disk copy so writers never clobber each other's keys.
- **Start with Windows** is a single suite-level Run-key entry owned by the host; it
  launches enabled modules at logon.
- **Control** flows host → module over a per-module named pipe
  (`DesktopToolkit.<id>`): Ping, SetEnabled, ApplySettings, Identify, Activate, Shutdown.

## Build & run

```powershell
dotnet build DesktopToolkit.sln
powershell -ExecutionPolicy Bypass -File tools\deploy-modules.ps1   # stage modules into modules/
src\Host\Toolkit.Runner\bin\Debug\net9.0-windows\Toolkit.Runner.exe
```

The host starts in the tray (a four-square glyph). Double-click it (or right-click → Open
Dashboard, or press **Ctrl+Alt+D**) to manage modules. **Exit** is on the tray menu.

### Verify

```powershell
dotnet run --project tools\Toolkit.SmokeTest -c Release -- "<repo>\modules"
```

Exercises discovery → enable/launch → IPC ping/pong → shutdown for every deployed module.

## Adding a module

1. Create a project under `src/Modules/<Name>` referencing `Toolkit.Common`.
2. In its entry point, run a `ModuleServer` on pipe `DesktopToolkit.<id>` and handle the
   commands you care about (at minimum `Ping`; `Shutdown`/`Activate` as appropriate).
3. Add a `module.json`:

   ```json
   {
     "id": "my-tool",
     "name": "My Tool",
     "description": "What it does.",
     "version": "1.0.0",
     "executable": "MyTool.exe",
     "kind": "background",          // or "window"
     "settingsWindow": false,        // true = host opens YOUR config window via Activate
     "settings": [                   // host renders these generically when settingsWindow is false
       { "key": "level", "type": "int", "label": "Level", "default": 3, "min": 0, "max": 10 }
     ]
   }
   ```
4. Add it to `tools/deploy-modules.ps1`, run the script, and it shows up in the dashboard.

Setting types the host renders: `bool`, `int`, `enum` (with `options`), `path`, `string`.

## Managing modules from the dashboard

The dashboard has two tabs:

- **Installed** — your module cards (Configure, enable for background modules). Each card
  also has **Reset** (restore that module's settings to its manifest defaults) and **Remove**
  (stop it and delete its folder). A toolbar adds **Open modules folder** and **Rescan**
  (re-discover `modules/` live, e.g. after dropping a folder in by hand).
- **Catalog** — a **Source** URL (a GitHub raw `catalog.json`) + **Refresh** lists installable
  modules with **Install / Update / Reinstall** (version-aware). **Install from .zip…** does
  the same from a local package. All of this is live — no host restart.

A module **package** is just a deployed module folder zipped (`module.json` + exe + deps).
`catalog.json`:

```json
{ "modules": [
  { "id": "my-tool", "name": "My Tool", "description": "...", "version": "1.0.0",
    "package": "https://github.com/<owner>/<repo>/releases/download/v1.0.0/my-tool.zip" }
] }
```

Packages are hosted as **GitHub Release assets** (Releases handle large files — the Animated
Wallpaper package is ~280 MB). Producer flow:

```powershell
pwsh tools/deploy-modules.ps1        # populate modules/
pwsh tools/package-modules.ps1       # -> dist/packages/<id>.zip + a catalog.json template
# upload the zips as Release assets, commit catalog.json, point the Catalog Source at its raw URL
```

> **Trust:** installing **downloads and stores an executable** from the source you configure.
> Point the catalog only at a repo you control. Installing just places files — running still
> requires you to enable/open the module — and the installer validates the package
> (`module.json` + its exe) before extracting. The install dialog shows the name, version,
> and source URL.

## Known limitations

- **Hiding the *primary* taskbar also hides the host's tray icon** (the host tray lives on
  the taskbar). Press **Ctrl+Alt+D** to open the dashboard anyway — a process-wide hotkey
  backed by a message-only window, registered for exactly this case.
- Animated Wallpaper bundles ~280 MB of LibVLC natives; first cold launch scans plugins and
  can take several seconds (cached afterward). GIFs may skip frames — videos are the smooth
  path. See its design notes in the module's source.

## Packaging

`tools/publish.ps1` produces a **fully self-contained** `dist/` — the host and every module
carry their own copy of the .NET runtime, so nothing needs to be installed to run it. The
dist is large (especially Animated Wallpaper + LibVLC). `PublishSingleFile` is off
everywhere (LibVLC needs its plugin folder as real files beside the exe).

`tools/package-modules.ps1` produces per-module package zips + a `catalog.json` template for
publishing a module catalog (see [Managing modules](#managing-modules-from-the-dashboard)).
A sample catalog is at [`docs/catalog.sample.json`](docs/catalog.sample.json).
