# Arcane Archives client

The browser release exports `ArcaneArchivesGame` through `src/browser.ts`.
`useArcaneArchives.ts` owns input, paid request sequencing, reel animation, audio,
Tome and potion interaction, bonus presentation and displayed wallet updates.
`api.ts` owns Arcane's wire contracts and validation. The game does not call the
retired host slot page/controller or import host source.

The host supplies an authenticated transport, account context and optional
navigation shell. Shared libraries provide neutral rendering and infrastructure
primitives. Arcane owns its screen, feature contracts, symbols, assets, settings,
help and win presentation.

Paid requests use the existing Arcane server package. Server-side admission,
outcomes, payouts, persisted features and wallet settlement remain authoritative;
browser calculations and checks affect display only. There is no demo game
implementation in this client.

The current migration intentionally does not recover obsolete schema-v1 local
rounds, as authorized by the owner for this unused game. The server returns an
explicit conflict for obsolete state instead of running the retired host path.

From the repository root:

```powershell
npm run check --workspace @fortuneforge/games-arcane-archives
npm run test --workspace @fortuneforge/games-arcane-archives
npm run build --workspace @fortuneforge/games-arcane-archives
```

## Payline rules

Exactly three identical symbols in a straight row or diagonal can start on
reels 1, 2 or 3. Five identical symbols pay on any of the existing full paylines.

