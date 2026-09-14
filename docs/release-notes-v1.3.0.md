# BuildnBits.Usage 1.3.0

- Grok popup and Adaptive Card can show **Build** and **Bot** remaining usage from `config.productUsage`.
- **Chat** wire products (`PRODUCT_CHAT` / `CHAT` / product `4`) display as **Bot**, not a third popup row.
- When billing omits `productUsage`, the aggregate weekly pool is labeled **Build** only — Bot is not invented.
- Compact one-line reset captions (`4h 12m · Fri 16:01`) and short labels (`5h`, `7d`, `Gemini 5h`) across providers.
- Rows within each provider are ordered by soonest reset.
- Settings independently control Codex 5-hour, Codex 7-day, Grok Build, and Antigravity usage options.
- Each selected settings option gets its own tray square; Grok exposes only the Build square, while popup rows remain unfiltered.
- Legacy provider icon flags migrate to period settings when no explicit period map exists.

Portable asset: `BuildnBits.Usage-1.3.0-portable-win-x64.zip`.
