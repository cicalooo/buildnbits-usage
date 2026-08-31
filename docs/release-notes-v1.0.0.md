# BuildnBits.Usage 1.0.0

Portable Windows 11 tray app for remaining Codex and Grok subscription usage.

## Asset

- `BuildnBits.Usage-1.0.0-portable-win-x64.zip` — self-contained win-x64. No .NET install. Unzip and run `BuildnBits.Usage.Tray.exe`. Cache and settings are stored in `data\` next to the exe (`portable.flag`).

Verify:

```powershell
Get-FileHash -Algorithm SHA256 BuildnBits.Usage-1.0.0-portable-win-x64.zip
```

Compare with `SHA256SUMS.txt`.

## Included

- Codex 5-hour and 7-day remaining usage via `codex app-server`
- Grok weekly remaining usage via `grok --no-auto-update agent stdio`
- Square notification-area icons and combined flyout
- Launch at login, per-icon visibility, larger digits

## Not in this release

- Widgets Board card (Future Plan; disabled in Settings)
- MSIX / Store package

Requires Codex and Grok CLIs on PATH (or the well-known install locations) and a ChatGPT / Grok subscription login. This app does not ship or store API keys, cookies, or tokens.
