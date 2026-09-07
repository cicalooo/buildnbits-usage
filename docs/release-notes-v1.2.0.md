# BuildnBits.Usage 1.2.0

## Highlights

- Compact, scrollable usage flyout with Codex 5-hour/7-day windows, Grok weekly usage, and every Antigravity quota pool.
- Configurable 3-minute, 5-minute (default), and 10-minute refresh intervals, including tray-menu quick picks.
- Credential-safe app.log with a 512 KB cap, .1 rotation, and Diagnostics actions to open or copy recent entries.
- Hidden process launch hardening, executable-first CLI discovery, and persistent Codex/Grok stdio sessions between polls.
- New three-provider application and MSIX logos using Codex teal, Grok orange, and Antigravity blue.
- Single-file compressed portable publishing with a small install-folder root.

## Portable asset

BuildnBits.Usage-1.2.0-portable-win-x64.zip is self-contained and does not require a .NET installation or MSIX. Extract it and run BuildnBits.Usage.Tray.exe; settings, cache, and data\app.log stay beside the executable.

## Privacy

The app continues to use the provider CLIs for authentication and never reads or stores their credentials. Logs contain statuses and timing only after redaction; raw responses, prompts, cookies, and tokens are not written.
