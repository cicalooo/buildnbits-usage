# BuildnBits.Usage 1.2.2

## Highlights

- Added independent tray-square choices for Codex 5-hour and 7-day usage, Grok weekly usage, and Antigravity weekly usage.
- A provider keeps one supported `NotifyIcon` square; its displayed percentage is the lowest remaining value among selected periods and pools.
- Existing provider-only settings migrate to explicit period visibility on the next save.
- The popup continues to show every usage row even when a period is excluded from its provider tray square.

## Portable asset

`BuildnBits.Usage-1.2.2-portable-win-x64.zip` is self-contained and does not require a .NET installation or MSIX. Extract it and run `BuildnBits.Usage.Tray.exe`; settings, cache, and `data\app.log` stay beside the executable.

## Privacy

The app continues to use the provider CLIs for authentication and never reads or stores their credentials. Settings and logs contain usage metadata and sanitized diagnostics only; raw responses, prompts, cookies, and tokens are not written.
