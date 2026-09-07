# BuildnBits.Usage 1.2.1

## Highlights

- Usage results now appear as each provider completes; a slow Antigravity command no longer holds back Codex or Grok.
- Concurrent refresh requests are coalesced into one provider pass, avoiding duplicate CLI work and process churn.
- Antigravity usage is bounded by a 15-second timeout and retains the last successful values when it times out.
- Refresh progress, provider-specific freshness, per-provider timings, and process-launch diagnostics are visible without exposing credentials.
- Provider process startup is centralized with redirected streams and hidden-window settings; widget callbacks are now concurrency-safe and isolated from refresh failures.

## Portable asset

BuildnBits.Usage-1.2.1-portable-win-x64.zip is self-contained and does not require a .NET installation or MSIX. Extract it and run BuildnBits.Usage.Tray.exe; settings, cache, and data\app.log stay beside the executable.

## Privacy

The app continues to use the provider CLIs for authentication and never reads or stores their credentials. Logs contain sanitized statuses, timing, and process metadata only; raw responses, prompts, cookies, and tokens are not written.
