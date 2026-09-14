# BuildnBits.Usage

Windows 11 notification-area app for remaining Codex, Grok, and Google Antigravity usage.

## Features

- One tray icon for each selected usage option. Codex 5-hour and 7-day windows can appear separately; selected Antigravity pools can appear separately; Grok exposes Build only.
- A popup shows every provider window, percentage remaining, reset time, refresh status, settings, and diagnostics.
- Refreshes on startup, manual refresh, resume, network recovery, and a 3/5/10-minute interval (5 minutes by default).
- Keeps the last successful values visible when a provider refresh fails and marks them stale.
- Portable release is a self-contained, single-file win-x64 app. It does not need a .NET installation.

## Install

1. Download the [latest portable release](https://github.com/cicalooo/buildnbits-usage/releases/latest).
2. Extract the ZIP.
3. Run `BuildnBits.Usage.Tray.exe`.
4. Open Settings from any tray icon to choose the visible options.

Portable mode stores settings, cache, and logs in `data\` beside the executable. A normal build uses `%LOCALAPPDATA%\BuildnBits\Usage\`.

## Providers

| Provider | CLI | Data shown |
| --- | --- | --- |
| Codex | `codex app-server --listen stdio://` | 5-hour and 7-day remaining usage |
| Grok | `grok --no-auto-update agent stdio` | Build remaining usage; Bot data in the popup/card when billing returns it |
| Antigravity | `agy -p /usage --output-format json` | Remaining quota for each model pool |

The app uses the provider CLIs for authentication. It never reads or stores API keys, tokens, cookies, prompts, or CLI credential files.

### Grok

The app requests `x.ai/billing` (or `_x.ai/billing`) through Grok agent stdio with `cached_token`. It does not scrape grok.com or read `~/.grok/auth.json`.

- Build is the only Grok tray option.
- `PRODUCT_CHAT` and Chat wire values map to the popup/card label Bot. Bot and Chat are not separate tray icons.
- Displayed remaining usage is `100 - usedPercent`.
- At the start of a complete active weekly period, Grok may omit zero-valued usage fields. The app treats that exact unified-billing response as **0% used / 100% remaining** and rejects incomplete or malformed responses instead of guessing.

## Data and privacy

The cache stores percentages, reset timestamps, plan labels, and status. The log stores sanitized status and timing information; raw provider responses are not logged.

- Portable: `data\settings.json`, `data\usage-cache.json`, and `data\app.log`
- Standard: `%LOCALAPPDATA%\BuildnBits\Usage\`

## Build and test

Requirements: Windows 11 and the .NET 8 SDK. Provider CLIs are only needed for the providers you use.

```powershell
dotnet test BuildnBits.Usage.sln -c Release
dotnet build src/BuildnBits.Usage.Tray/BuildnBits.Usage.Tray.csproj -c Release
pwsh -File publish-portable.ps1
```

The publisher creates `artifacts\portable\` and a versioned ZIP with `SHA256SUMS.txt` under `artifacts\release\`. Do not commit `artifacts\` or any `data\` directory.

## Limitations

Widgets Board integration and MSIX packaging are present as future work and are disabled. Replacement taskbars may work when they preserve Windows `NotifyIcon` behavior, but they are not guaranteed.

## Updates

See [docs/update-notes.md](docs/update-notes.md) for the short release history.
