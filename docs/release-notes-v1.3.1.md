# BuildnBits.Usage 1.3.1

- Fix Grok unified-billing rollover responses that omit zero-valued usage fields: a complete active weekly period now displays **0% used / 100% remaining** instead of retaining a stale `0%` display or reporting a missing billing payload.
- Grok parser validation remains fail-closed for malformed usage values, invalid period metadata, mismatched billing bounds, and unrelated period-only responses.
- Grok popup and Adaptive Card can show **Build** and **Bot** remaining usage from `config.productUsage`.
- **Chat** wire products (`PRODUCT_CHAT` / `CHAT` / product `4`) display as **Bot**, not a third popup row.
- Each selected settings option gets its own tray square; Grok exposes only the Build square, while popup rows remain unfiltered.

Portable asset: `BuildnBits.Usage-1.3.1-portable-win-x64.zip`.
