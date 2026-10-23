# Dragon Tiger

An independent FortuneForge solo casino package: server-drawn cards, atomic wallet settlement, an illustrated responsive table, accumulated chip stacks, deliberate card reveals, sound controls, and recovery of interrupted wagers. Source, CSS and decorative art are owned by this package. No other game's artwork or code is imported.

## Local preview

From the repository root, in two terminals:

```powershell
npm run dev:api --prefix games/DragonTiger/client/FortuneForge.Games.DragonTiger.Client
npm run dev:ui --prefix games/DragonTiger/client/FortuneForge.Games.DragonTiger.Client
```

Open **http://127.0.0.1:5180/**. The API listens on **http://127.0.0.1:5198/**. The sample is explicitly practice mode and starts its separate in-memory ledger at R10,000. It does not call a production wallet. Restarting the API resets its practice ledger; browser history is local to the preview account. The same HTTP client and settlement package are used by the host integration.

Alternatively run `npm run dev --prefix games/DragonTiger/client/FortuneForge.Games.DragonTiger.Client` to start both. The existing root dependencies are sufficient while this package awaits workspace registration.

If the shared workspace dependencies are being rebuilt by another game, the client also works with its own ignored `node_modules`: install this package with the workspace option disabled rather than changing the root manifest. The local preview remains separate from the main application release.

## Product references and rules

- [Pragmatic Play Dragon Tiger RNG](https://www.pragmaticplay.com/en/games/dragon-tiger-rng/) is the chosen digital product reference: a solo two-card comparison rather than filmed live dealing. Reviewed professional RNG table screenshots also informed the three distinct betting spots, bottom chip rail, readable card faces, wallet totals and player-controlled deal pacing.
- [Evolution First Person Dragon Tiger](https://games.evolution.com/first-person/first-person-dragon-tiger/) is a secondary rendered-table reference for outcome lighting and the focus on two cards. Its advertised side bets and paytable are not this table's rules.
- [SA Gaming's provider rules](https://rules.sa-portal.com/swp_en/dragon-tiger/) verify the selected 8-deck, Ace=1 through King=13 comparison, rank ties, 1:1 main payouts, half the Dragon/Tiger stake returned on ties, and 8:1 Tie payout. This package intentionally offers only Dragon, Tiger and Tie. It does not implement SA's live-dealer burn procedure or extra side bets. It reshuffles after 312 dealt cards.

Dragon/Tiger winners return twice their stake. Tie winners return nine times their stake. Equal ranks return half of each main stake and pay the Tie stake. Suits never break a tie. The guide gives concrete examples and distinguishes return, net profit and the Win display. Limits are R0.50 minimum per spot in R0.50 increments; R100 maximum per spot and R100 total per hand. Half-stake returns therefore always settle in exact cents, including R0.25.

The decorative felt is an original generated illustration optimized to a 122 KB WebP. Small chip PNGs are independent copies of the established FortuneForge chips, preserving their visual style. Card/chip audio copies retain the existing asset license at `client/FortuneForge.Games.DragonTiger.Client/src/assets/License.txt`.

## Package layout

- `src/FortuneForge.Games.DragonTiger`: shared-card-based rank/settlement engine and descriptor.
- `server/FortuneForge.Games.DragonTiger.Server`: authenticated endpoint extensions, authoritative crypto shuffle, persistent shoe, independent practice store and Firestore transactional store.
- `client/FortuneForge.Games.DragonTiger.Client`: reusable React game, typed/validated HTTP gateway, standalone Vite entry and client tests.
- `samples/FortuneForge.Games.DragonTiger.Web`: local practice API sample.
- `tests/FortuneForge.Games.DragonTiger.Tests`: engine, API, recovery and Firestore emulator tests.

## Verification

```powershell
npm run check --prefix games/DragonTiger/client/FortuneForge.Games.DragonTiger.Client
npm run test --prefix games/DragonTiger/client/FortuneForge.Games.DragonTiger.Client
npm run build --prefix games/DragonTiger/client/FortuneForge.Games.DragonTiger.Client
dotnet test games/DragonTiger/tests/FortuneForge.Games.DragonTiger.Tests/FortuneForge.Games.DragonTiger.Tests.csproj
npm run test:e2e --prefix games/DragonTiger/client/FortuneForge.Games.DragonTiger.Client
```

For a restrictive local sandbox, point TEMP and TMP at a writable package `.test-tmp` folder when running Vitest. The browser smoke expects the two local servers above, uses only the practice API, and checks a real mixed-spot wager, settlement, idempotent replay, conflicting-key rejection, lost response followed by reload recovery, chip stacks, controls, rules, and desktop/mobile viewports. Set `DRAGON_TIGER_BROWSER_CHANNEL=chrome` to use installed Chrome or leave unset for Playwright Chromium. Set `DRAGON_TIGER_SCREENSHOTS=1` for viewport screenshots under the client's ignored `src/.e2e-artifacts/` directory.

## Main-host release

The original Baccarat release gate is complete. Dragon Tiger is integrated into the authenticated main application at `/games/dragon-tiger`, using its shared black-themed navbar and standard card faces, with game-specific Asian card backs. The reviewed release enables the game and lists it in the runtime catalogue. Add `?mode=practice` for the isolated practice wallet; normal account play uses authenticated server settlement. See [INTEGRATION.md](INTEGRATION.md) for ownership and wiring, and use the repository's guarded release workflow when publishing.
