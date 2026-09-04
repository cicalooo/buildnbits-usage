# BuildnBits.Usage 1.1.0

Portable Windows 11 tray app for remaining Codex, Grok, and Google Antigravity usage.

## Asset

- `BuildnBits.Usage-1.1.0-portable-win-x64.zip` — self-contained win-x64. No .NET install. Unzip and run `BuildnBits.Usage.Tray.exe`. Cache and settings are stored in `data\\` next to the exe (`portable.flag`).

Verify:

```powershell
Get-FileHash -Algorithm SHA256 BuildnBits.Usage-1.1.0-portable-win-x64.zip
```

Compare with `SHA256SUMS.txt`.

## Included

- Codex 5-hour and 7-day remaining usage via `codex app-server`
- Grok weekly remaining usage via `grok --no-auto-update agent stdio`
- Google Antigravity model quota pools via `agy -p /usage --output-format json`
- Square notification-area icons and combined flyout for all three providers
- Antigravity quota details in the popup and Widgets Board card data
- Launch at login, per-icon visibility, larger digits, and resilient cached refreshes

## Requirements

Requires the Codex, Grok, and/or Antigravity CLI, depending on the providers used, plus the corresponding subscription login. The app does not ship or store API keys, cookies, tokens, or CLI credentials.

Antigravity must be installed and authenticated separately. Run `agy` once to complete sign-in if needed. The app only requests the read-only `/usage` command and does not start an agent turn.

## Not in this release

- Widgets Board card registration (Future Plan; provider data is prepared)
- MSIX / Store package
