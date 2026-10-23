# Parent batch integration — Andar Bahar

This is the handoff for central files intentionally left unchanged. Re-read their current contents before editing: Hearts, Dragon Tiger, Horse Flight and other new-game chats coexist on `main`. Do not overwrite another game's changes or assume inventory counts are still the values seen here. This package is independently playable on 5182/API5202; no central integration or deployment has been performed.

## Workspace and build

1. Add workspace `games/AndarBahar/client/FortuneForge.Games.AndarBahar.Client` to root `package.json`.
2. Add `"@fortuneforge/games-andar-bahar": "0.1.0"` to `fortuneforge.client/package.json` dependencies. Update the root lockfile once for the complete new-game batch, preserving all existing package changes. No package-specific install is necessary before this step; the current root dependencies already build this isolated client.
3. Add project references/solution entries for:
   - `games/AndarBahar/src/FortuneForge.Games.AndarBahar/FortuneForge.Games.AndarBahar.csproj`
   - `games/AndarBahar/server/FortuneForge.Games.AndarBahar.Server/FortuneForge.Games.AndarBahar.Server.csproj`
   - `games/AndarBahar/samples/FortuneForge.Games.AndarBahar.Web/FortuneForge.Games.AndarBahar.Web.csproj`
   - `games/AndarBahar/tests/FortuneForge.Games.AndarBahar.Tests/FortuneForge.Games.AndarBahar.Tests.csproj`

The server project references its engine, which references the approved shared card/abstraction projects. Client exports the component/gateway/types from the package root and styles from `@fortuneforge/games-andar-bahar/styles.css`.

## Authentication, routing, navigation and wallet

Copy the prepared [AndarBaharRoute.tsx](integration/AndarBaharRoute.tsx) and [AndarBaharRoute.css](integration/AndarBaharRoute.css) to `fortuneforge.client/src/app/routes/`. They follow the existing Baccarat route: authenticated account hook, shared `InGameShell` navigation, account-session fetch, isolated practice fetch, player-specific pending/history state, and refreshing the displayed host wallet after paid settlement. The stylesheet makes the game fill the available shared-shell body; do not leave its standalone viewport-height default beneath an additional host navbar.

Register a lazy `AuthenticatedAndarBaharRoute` in `fortuneforge.client/src/app/AppRoutes.tsx` with the repository's existing casino route pattern at **`/games/andar-bahar`**. Add **`'/games/andar-bahar': 'Andar Bahar — Fortune Forge'`** to `fortuneforge.client/src/app/usePageTitle.ts`.

In `fortuneforge.client/src/pages/games/OtherGamesPage.tsx`, add an entry to `casinoGames` without shifting existing indexed `cardGames` references:

```tsx
{ name: 'Andar Bahar', category: 'Casino card game', href: '/games/andar-bahar', icon: '♠', summary: 'Choose the side that first matches the central Joker.' },
```

Add it to `newGames` by a named constant or actual entry rather than guessing an existing positional index. Use the reviewed table screenshot if a catalogue image is desired. The main component owns its FortuneForge/table title; the shared shell still owns navigation and the authenticated account.

Copy [andar-bahar.game.json](integration/andar-bahar.game.json) into `games/catalog/games/andar-bahar.game.json`. Its schema reference is prepared for that destination. Keep `rules-pilot` until the host route and wallet acceptance checks pass.

## Server

Follow [the server integration document](server/INTEGRATION.md) for exact registration, endpoint mapping, resolver, rate limits, Firestore schema and feature flag. Add the server package ProjectReference to `FortuneForge.Server.csproj`; call `AddAndarBaharServer` in Startup and `MapAndarBahar` in the host pipeline.

The account resolver must use the existing account session cookie. Never bind player identity, cards, outcomes or balance from a client request. Paid outcomes, wallet debit/credit, ledger, settlement event and latest pointer commit in one Firestore transaction. The public result contains only the central Joker and dealt prefix through the first match; the undealt deck is never returned or persisted. There is no cash-out endpoint for this instantly settled game.

`Features:AndarBaharEnabled` defaults false. Keep it disabled until acceptance; enable through the normal release process. `AllowPracticeRequests` defaults false and should remain false for a paid production host. Development may enable the same existing practice header. A disallowed practice request returns 403 and never silently spends paid funds. Its local practice file is for development, not a production wallet substitute.

## Tests and release inventory

Add `/games/andar-bahar` to `scripts/game-smoke-inventory.mjs`, then update the route-count assertions in `scripts/verify-game-route-inventory.mjs` and `scripts/smoke-production.mjs` based on the final combined catalogue. Extend the app's packaged CSS/route-shell coverage and add an authenticated route test for account/practice isolation, `playerId` scoping, auth failure/retry, and settled-wallet refresh. Delay an old account's wallet refresh across an identity change and verify it cannot replace the new user or pending-wager scope; the prepared route includes that guard. Reuse the catalogue verification tests.

Run the isolated commands in [VERIFICATION.md](VERIFICATION.md), then the normal ownership, inventory, package, host build and route tests after central changes. Test the shared shell at desktop, 320–430px portrait and short landscape; standalone screenshots prove the component, not the future composed host route. Exercise paid mode only with an isolated emulator account, including .50-cent Andar payouts, duplicate/concurrent posts, insufficient funds, refresh/resume, private reads and a lost response followed by replay. Do not use production funds.

The owner must review the playable integrated route and approve its release through the existing ship-review loop. No commit, push, live configuration change or deployment is authorized by this build task.
