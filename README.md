# BuildnBits.Usage

> A Windows 11 notification-area app for checking your remaining Codex, Grok, and Google Antigravity usage at a glance.

<p align="center">
  <img
    width="480"
    alt="ChatGPT Image Sep 7, 2026, 09_22_53 PM"
    src="https://github.com/user-attachments/assets/957ce101-0590-4e3b-b14c-f3270c3dc03c"
  />
</p>

## At a glance

- Shows Codex 5h/7d windows, Grok Build remaining usage (and Bot when billing sends it), and Google Antigravity model quotas.
- Uses compact reset captions (`4h 12m · Fri 16:01`) and short labels (`5h`, `7d`, `Gemini 5h`).
- Keeps three compact tray icons visible for the Codex, Grok, and Antigravity percentages.
- Opens a compact, scrollable status popup with every Antigravity quota pool, remaining time, reset times, refresh controls, settings, and diagnostics.
- Refreshes on startup, manual refresh, resume from sleep, network recovery, and roughly every 5 minutes by default. Provider results appear progressively, so a slow CLI does not hold back the others.
- Supports 3-minute, 5-minute, and 10-minute update intervals from Settings or the tray menu.
- Stores usage metadata only—never tokens, cookies, prompts, or account credentials.

## Screenshots

The checked-in screenshots show the three-provider tray experience; current builds use the compact, scrollable provider-row popup described below.

### Usage tracker popup

The popup shows all tracked subscription windows, their remaining percentages, remaining/reset times, and the current provider status.

![BuildnBits.Usage popup showing Codex 5-hour and 7-day usage plus Grok weekly usage](docs/images/usage-tracker-popup.png)

### Notification-area icons

Three small notification-area icons keep the current Codex, Grok, and Antigravity remaining percentages visible without opening the popup. The Antigravity icon shows the lowest remaining quota pool.

![Codex, Grok, and Antigravity percentage icons in the Windows notification area](docs/images/usage-tracker-tray-icons.png)

## How it works

Windows does not provide a supported API for arbitrary inline taskbar widgets. BuildnBits.Usage uses three supported `NotifyIcon` / `Shell_NotifyIcon` icons instead. A Widgets Board card remains in the source as a future plan and is disabled in Settings.

Replacement taskbars such as StartAllBack or ExplorerPatcher may work when they preserve `Shell_NotifyIcon`; compatibility is not guaranteed.

## Providers

| Provider | Connection | What is shown | Authentication |
| --- | --- | --- | --- |
| **Codex** | `codex app-server --listen stdio://` | 5-hour and 7-day remaining usage, reset times | ChatGPT subscription login; API-key authentication is rejected |
| **Grok** | `grok --no-auto-update agent stdio` | Build remaining usage; Bot row when `productUsage` includes Chat/Bot; shared weekly reset | `cached_token` only |
| **Google Antigravity [agy]** | `agy -p /usage --output-format json` | Weekly remaining quota for each model pool and reset times | Existing `agy` sign-in; credentials stay in the CLI keyring |

### Codex

BuildnBits.Usage calls `initialize`, `account/read`, and `account/rateLimits/read`. It recognizes usage windows by duration, including **300 minutes (5-hour)** and **10,080 minutes (7-day)**. Remaining usage is calculated as `100 - usedPercent`, and the Codex tray icon shows the lowest remaining percentage across active windows.

### Grok

The app calls `x.ai/billing` (and `_x.ai/billing` when needed) using `cached_token` from `grok --no-auto-update agent stdio`. It never reads or stores `~/.grok/auth.json`, cookies, or tokens, and it does not scrape grok.com.

#### Build vs Bot (Chat)

| Popup / Adaptive Card label | Matched `config.productUsage[].product` values |
| --- | --- |
| **Build** | `2`, `"2"`, `PRODUCT_GROK_BUILD`, `GROK_BUILD`, `BUILD` |
| **Bot** | `4`, `"4"`, `PRODUCT_GROK_BOT`, `GROK_BOT`, `BOT`, `PRODUCT_CHAT`, `CHAT` |

- **Chat is not a separate row.** Wire names like `PRODUCT_CHAT` / `CHAT` map to the same **Bot** label. Imagine, Voice, API, App Builder, and other unknown products are ignored.
- Remaining for each matched product is `100 - usagePercent` (same convention as `creditUsagePercent`). Both rows share the weekly `currentPeriod.end` reset when present.
- Matching is case-insensitive after stripping a `PRODUCT_` prefix. The first Build match and the first Bot match win. If both Chat and Bot strings appear, the first Bot-mapped entry is kept.
- **If `productUsage` is missing or empty** (common on some SuperGrok CLI billing replies today), the aggregate `creditUsagePercent` is shown as a single **Build** row. The app does **not** invent a Bot row or a `0%` Bot value.
- **If Bot/Chat is absent from `productUsage` but Build is present**, only Build is shown.
- The Grok tray icon is the lowest remaining percent among the Grok rows that exist (Build and/or Bot).

### Google Antigravity [agy]

The app runs Antigravity's read-only `/usage` command in headless mode and parses its machine-readable `command.data.groups[].buckets[]` response. It records each quota pool separately and shows the lowest remaining pool in the tray icon. The `/usage` command does not start an agent turn or spend model quota. Antigravity authentication remains in the CLI's secure Windows credential store; this app never reads or stores it.

## Privacy and cache

The cache contains only percentages, period timestamps, plan labels, and status:

```text
%LOCALAPPDATA%\BuildnBits\Usage\usage-cache.json
```

Tokens, cookies, prompts, and account credentials are never copied or logged.

- **Standard installation:** cache and settings are stored in `%LOCALAPPDATA%\BuildnBits\Usage\`.
- **Portable build:** cache and settings are stored in `data\` next to the executable when `portable.flag` is present.

Temporary refresh failures retain the last successful percentages and mark the status as stale.

Antigravity's one-shot `/usage` command is capped at 15 seconds. A timeout leaves its cached values visible as stale while Codex and Grok continue to update.

The application log is append-only, capped at 512 KB, and rotates to app.log.1. It contains startup, refresh, provider-status, and process-launch diagnostics after credential-safe redaction; raw JSON-RPC responses, stderr, tokens, cookies, and prompts are never logged.

## Projects

| Project | Responsibility |
| --- | --- |
| `BuildnBits.Usage.Core` | Models, process clients, Codex/Grok/agy parsers, cache, refresh loop, and Adaptive Card JSON |
| `BuildnBits.Usage.Tray` | WinForms host, DPI-aware icons, combined popup, launch-at-login, and diagnostics |
| `BuildnBits.Usage.Widgets` | COM Widgets Board provider |
| `BuildnBits.Usage.Package` | MSIX packaging for the tray app and widget |
| `BuildnBits.Usage.Tests` | Parser, auth, cache, JSON-RPC, icon, and Adaptive Card tests |

## Requirements

- Windows 11
- .NET SDK 8 to build from source
- An authenticated Codex, Grok, and/or Antigravity CLI, depending on the providers you use

## Build from source

```powershell
# Run the test suite
dotnet test BuildnBits.Usage.sln

# Build the tray application
dotnet build src/BuildnBits.Usage.Tray/BuildnBits.Usage.Tray.csproj -c Release
```

### Run unpackaged

```powershell
dotnet run --project src/BuildnBits.Usage.Tray/BuildnBits.Usage.Tray.csproj
```

## Portable build

Create a self-contained, single-file win-x64 folder with no .NET installation or MSIX required. The first launch may briefly extract native runtime components to the normal user temporary directory:

```powershell
pwsh -File publish-portable.ps1
```

Run `artifacts\portable\BuildnBits.Usage.Tray.exe`, or extract the GitHub Release asset named `BuildnBits.Usage-<version>-portable-win-x64.zip`.

You can also force portable mode with:

```powershell
$env:BUILDNBITS_USAGE_PORTABLE = "1"
```

Release binaries are created under `artifacts/release/` locally and are not committed.

## MSIX package

### Visual Studio

Install the Windows Application Packaging workload, then open:

```text
src/BuildnBits.Usage.Package/BuildnBits.Usage.Package.wapproj
```

### Command line

Requires the Windows SDK `makeappx` tool:

```powershell
pwsh -File src/BuildnBits.Usage.Package/pack.ps1
```

Explorer restarts re-register the three `NotifyIcon` instances through the documented `TaskbarCreated` broadcast. Icons are not reparented into Explorer.

For development signing, see [the signing guide](src/BuildnBits.Usage.Package/signing/README.md). Private certificates are not committed (`*.pfx` is ignored); the development publisher subject is `CN=BuildnBits-Dev`.

## Settings and future work

Settings—available from either tray icon—cover launch at login, which icons to show, larger tray digits, the 3/5/10-minute refresh interval, and the cache folder. Diagnostics also offers Open log and Copy recent log. Tokens and cookies are never stored in Settings or the application log.

Widgets Board integration is currently disabled and tracked in [FUTURE.md](FUTURE.md). The MSIX packaging project remains in the repository for that future work.

## Manual environment checks

Where the host OS allows it, verify the following:

- Explorer restart, auto-hide taskbar, overflow chevron, multiple monitors, mixed DPI, sleep/resume, and light/dark/high-contrast themes.
- Stock Windows 11 taskbar, plus StartAllBack or ExplorerPatcher when installed.
- MSIX installation and widget discovery.
- HTTP 429 responses, network loss, malformed JSON, and process timeouts.

Automated tests cover these error paths where a child process can be spawned.

