# BuildnBits.Usage

Windows 11 notification-area app that shows remaining **Codex** and **Grok** subscription usage.

Windows does not provide a supported arbitrary inline taskbar-widget API. The supported surface is two `NotifyIcon` / `Shell_NotifyIcon` icons. A Widgets Board card exists in source but is a **Future Plan** (disabled in Settings). Replacement taskbars such as StartAllBack or ExplorerPatcher work only when they preserve `Shell_NotifyIcon`. Compatibility is not guaranteed.

## Projects

| Project | Role |
| --- | --- |
| `BuildnBits.Usage.Core` | Models, JSON-RPC stdio client, Codex/Grok parsers, cache, refresh loop, Adaptive Card JSON |
| `BuildnBits.Usage.Tray` | WinForms host: two DPI-aware icons, combined popup, launch-at-login, diagnostics |
| `BuildnBits.Usage.Widgets` | COM Widgets Board provider |
| `BuildnBits.Usage.Package` | MSIX that packages tray + widget together |
| `BuildnBits.Usage.Tests` | Parser, auth, cache, JSON-RPC, icon, and Adaptive Card tests |

## Providers

**Codex** (`codex app-server --listen stdio://`)

- `initialize` → `account/read` → `account/rateLimits/read`
- Windows are recognized by duration, including **300 minutes (5-hour)** and **10,080 minutes (7-day)**
- Remaining percent is `100 - usedPercent`
- The Codex icon shows the **lowest remaining percent** across active windows
- API-key authentication is rejected; ChatGPT subscription login is required

**Grok** (`grok --no-auto-update agent stdio`)

- Authenticate with `cached_token` only
- Call `x.ai/billing`
- Weekly remaining percent and reset time
- This app never reads or stores `~/.grok/auth.json` or other credentials

## Refresh

Startup, the Refresh action, resume from sleep, network recovery, and about every 10 minutes with jitter. Temporary failures keep the last successful percentages and mark status stale.

## Cache

Per-user `%LOCALAPPDATA%\BuildnBits\Usage\usage-cache.json` stores only percentages, period timestamps, plan labels, and status. Tokens, cookies, prompts, and account credentials are not logged or copied.

## Build

Requires .NET SDK 8.

```powershell
dotnet test BuildnBits.Usage.sln
dotnet build src/BuildnBits.Usage.Tray/BuildnBits.Usage.Tray.csproj -c Release
```

Unpackaged run:

```powershell
dotnet run --project src/BuildnBits.Usage.Tray/BuildnBits.Usage.Tray.csproj
```

## Portable

Self-contained win-x64 folder (no .NET install, no MSIX). Cache and settings go in `data\` next to the exe when `portable.flag` is present.

```powershell
pwsh -File publish-portable.ps1
```

Run `artifacts\portable\BuildnBits.Usage.Tray.exe`, or unzip the GitHub Release asset `BuildnBits.Usage-<version>-portable-win-x64.zip`. You can also set `BUILDNBITS_USAGE_PORTABLE=1`. Binaries live under `artifacts/release/` locally and are not committed.

## MSIX

Visual Studio (Windows Application Packaging workload):

Open `src/BuildnBits.Usage.Package/BuildnBits.Usage.Package.wapproj`.

Command line (Windows SDK `makeappx`):

```powershell
pwsh -File src/BuildnBits.Usage.Package/pack.ps1
```

Explorer restarts re-register the two `NotifyIcon` instances via the documented `TaskbarCreated` broadcast. Icons are not reparented into Explorer.

Development signing: see `src/BuildnBits.Usage.Package/signing/README.md`. Private certificates are not committed (`*.pfx` is gitignored). Publisher subject is `CN=BuildnBits-Dev`.

Widgets Board integration is **disabled** in Settings and tracked in `FUTURE.md`. MSIX packaging remains in the repo for that future work.

Settings (from either tray icon) cover launch-at-login, which icons to show, larger tray digits, and the cache folder. Tokens and cookies are never stored there.

Per-user (non-portable) cache: `%LOCALAPPDATA%\BuildnBits\Usage\`.

## Manual environment checks

Where the host OS allows it, verify:

- Explorer restart, auto-hide taskbar, overflow chevron, multiple monitors, mixed DPI, sleep/resume, light/dark/high-contrast themes
- Stock Windows 11 taskbar plus StartAllBack or ExplorerPatcher if installed
- MSIX installation and widget discovery
- 429, network loss, malformed JSON, and process timeout (covered in automated tests where a child process can be spawned)
