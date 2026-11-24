# Frontier Fortune client

This package owns Frontier Fortune’s native React interface, paid HTTP transport,
action recovery, reel-card selection, held-card staging, animation, help, artwork, and catalogue metadata. It imports no
other game’s frontend or gameplay rules. The host injects authentication, its
account summary, navigation shell, and authenticated fetch.

Every game state, reel outcome, saved ante, dealt hand, reward, and wallet
result comes from the production Frontier backend. There is no browser outcome
generator, practice mode, alternate engine, local game balance, forced bonus, or
browser-persisted game state. Local development uses the same paid API with
isolated infrastructure and test funds.

`games/HighNoonFortune/frontier-rules.json` owns the frozen base numeric rules;
`frontier-outlaw-rules.json` owns version-two ante and hand rules. Both are embedded
by C# and copied by `scripts/sync-rules.mjs` into generated client assets. The
frontend reads them only for static help and metadata. Server responses decide
the actual outcome, funding, hand and award.

The same five reels remain on screen. Two or more WANTED symbols fund saved ante
chips. Once ready, the player deals in, chooses a card from each open reel, then
holds hand cards before later included deals. Every selection confirms atomically
on the server; held masks commit with their next deal. The final confirmation
settles the hand once. Feature grids pay their hand table rather than base lines.

The game fills its actual host body without page scrolling. Wide screens put the
physical hand rail beside the reels; phones place it under the frame. Short
landscape screens put neutral platform controls beside the stage. Frame aperture
coordinates come from its generated geometry contract, and its aspect is preserved.

Transport uncertainty keeps the original operation ID and arguments for recovery.
Definitive rejections accept or reload authoritative state, then unlock the next
choice unless the server reports a genuinely pending operation.

From the repository root:

```powershell
npm run check --workspace @fortuneforge/games-frontier-fortune
npm run build --workspace @fortuneforge/games-frontier-fortune
npm run test --workspace @fortuneforge/games-frontier-fortune
```

The exported `FrontierFortuneGame` accepts the host’s neutral packaged-game props.
Its canonical paid route is `/slots/frontier-fortune`; the stable catalogue ID is
`high-noon-fortune`. There is no demo route.
