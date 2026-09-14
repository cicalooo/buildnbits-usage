# Update notes

Short release history for BuildnBits.Usage.

## 1.3.1 — Grok rollover fix

- Fixed unified-billing responses that omit zero-valued usage fields at the start of a weekly period. The app now shows **100% remaining** instead of retaining a stale zero or reporting a missing payload.
- Added strict parser coverage for malformed values, invalid periods, and mismatched billing metadata.
- Published the self-contained portable release with a matching SHA-256 file.

## 1.3.0 — Per-option tray icons

- Added one tray icon per selected usage option.
- Codex 5-hour and 7-day windows can appear separately, and selected Antigravity pools can appear separately.
- Kept Grok Build as the only Grok tray option while retaining Bot/Chat data in the popup and Adaptive Card.
- Preserved settings compatibility, popup rows, and Explorer/taskbar icon recovery.

## 1.0–1.2 — Core app and refresh work

- Added Codex and Grok usage tracking, then Antigravity quota pools.
- Added the compact popup, refresh interval controls, stale-cache behavior, diagnostics, persistent CLI sessions, hidden process launches, and portable single-file publishing.
- Added launch-at-login, icon visibility, larger tray digits, and credential-safe logging.

## Current limits

Widgets Board integration and MSIX packaging remain disabled future work.
