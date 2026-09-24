# PNG asset audit — 2026-09-23

The consolidated repository contains 236 PNG files after cleanup.

- No unrelated chat-platform filenames, source references, embedded PNG metadata, or public assets remain.
- 16 obsolete opaque Pirate chest variants were removed. The game now renders one transparent empty chest per collection and fills it with gem overlays.
- 26 other definitely unused PNGs were removed: superseded roulette/dice/asteroid/horse assets and abandoned host/Wukong artwork.
- `scripts/OptimizePirateAssets.cs` now produces only the four empty chest bases, so the removed filled variants cannot be accidentally regenerated.
- Playwright screenshot baselines, Blackjack review evidence, slot tooling inputs, and source PNGs with runtime WebP derivatives were retained because they still have a test, tooling, evidence, or authoring purpose.

All removals remain recoverable from Git history.
