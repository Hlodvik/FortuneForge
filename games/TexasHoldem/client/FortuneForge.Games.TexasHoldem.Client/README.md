# Texas Hold’em presentation client

`@fortuneforge/games-texas-holdem` contains the five-seat table presentation, action dock, stake selection, and queue screen. The existing authenticated host owns sessions, account identity, server polling, idempotency keys, authoritative legal actions, and every wallet mutation. This package does not deal paid cards or simulate account balances.

## Units and integration

- `HoldemTable` chip fields (pot, stacks, committed amounts, raise targets and winnings) are integer cents.
- `HoldemTableRule` stakes and `HoldemLobby.balanceCredits` are decimal rand credits.
- Supply the host's canonical shared card renderer to the table surface; never disclose hidden cards or import a host component into the production package.
- Action controls require both the server-supplied legal action and the current viewer's active turn. The host must still validate all commands server-side.
- Leaving an active hand requires explicit confirmation. The existing server determines fold/settlement behavior.

## Buy-in and matching

The player chooses a whole-cent buy-in between the table's big blind and its maximum initial stack, subject to their account balance. Joining reserves that amount once; blinds and subsequent bets move reserved table chips, not the account wallet. Chips and winnings remain at the table between hands, without automatic rebuying or clipping winnings to the initial buy-in maximum. Canceling a queued reservation returns it; leaving or being released for inactivity returns uncommitted table chips once. Leaving during a live hand folds it and does not return chips already committed to the pot.

Matching searches existing eligible live tables first, admits a waiting player at a hand boundary, and otherwise waits through the human grace period before shared managed-player filling. An unadmitted reservation expires and refunds after five minutes. The server retains the old accounting model only for already-persisted legacy tables; new escrow tables use a separate queue partition.

## Development

Use `npm run dev:ui` for the standalone development preview at `http://127.0.0.1:5186/`. It has no production account adapter. `npm run check`, `npm test`, and `npm run build` provide focused checks.

The approved design follows Fortune Forge’s Dragon Tiger presentation language on borderless emerald felt: names and stacks printed directly on the table, clear community cards, a central pot and a shallow translucent action dock. There is no oval frame, leather rail, or decorative outer gold border. Responsive layouts retain the same composition with smaller cards and controls on phones.

## Reused assets

The seven numbered chip PNGs and locally served Sora/Fraunces fonts are copied unchanged from the existing Hi-Lo client. Font OFL files remain alongside the fonts. Cards are injected by the host using its canonical shared faces, approved one-ink court icons, and classic navy/gold FF casino backs; Asian-themed backs are not used here.

`emerald-felt-v2.webp` is an original generated background optimized to approximately 76 KB. See [artwork provenance and generation prompt](src/assets/ARTWORK.md). Earlier unused felt/leather source files are not imported or bundled in the production package.
