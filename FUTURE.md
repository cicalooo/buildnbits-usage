# Future Plan

## Widgets Board card (disabled)

Windows 11 Widgets Board only lists providers delivered as an **MSIX** app extension. The portable tray and `dotnet run` hosts cannot register a widget.

When this is picked up again:

- Sideload or Store-publish the existing `BuildnBits.Usage.Package` MSIX (tray + COM widget provider).
- Pin **BuildnBits Usage** from Win+W.
- Keep Adaptive Card layouts (small / medium / large), cache-first paint, then async refresh, Refresh and Open app actions.
- Do not inject into Explorer or fake an inline taskbar widget. There is no supported arbitrary taskbar-widget API.

Settings currently greys out Widgets Board actions so the portable product does not promise a surface that is unavailable.

## Other possible follow-ups

- Per-monitor icon size override
- Optional refresh interval in settings
- Signed Store package if distribution needs it
