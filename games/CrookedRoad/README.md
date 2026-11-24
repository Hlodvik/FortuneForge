# The Crooked Road

The Crooked Road is a fresh, paid-only slot game with runtime ID `crooked-road-v1`, engine version `crooked-road-engine-v1`, and math model `crooked-road-math-v1`. Players rearrange a persistent road to visit the Wishing Well, Fairy Fort, or Hollow Hill. Each destination plays differently and has the same expected funded return before downward rounding.

This source package is **unpublished**. The game declaration exists in `game-release.json`, but the repository's backend package lock has no published Crooked Road entry. Publishing a verified backend artifact, registering it, and enabling production routing remain separate release work. No deployment is performed by the commands below.

## Ownership and paid lifecycle

| Location | Responsibility |
| --- | --- |
| `src/FortuneForge.Games.CrookedRoad/` | Native reel evaluation, road connectivity and actions, destination state, public views, validation, and exact math. |
| `server/FortuneForge.Games.CrookedRoad.Server/` | Paid endpoints, authoritative Firestore state, operation recovery, wallet settlement, and replay evidence. |
| `server/GameWorkerModule.cs` | Registration in the shared packaged worker host. |
| `client/FortuneForge.Games.CrookedRoad.Client/` | Executable game entry, full screen, controller, paid API and recovery, reels, controls, audio, help, artwork, native journeys, and generated rule-contract mirror. |
| `tests/` | Domain and persistence regression tests plus an isolated local paid-protocol fixture. |
| `tools/FortuneForge.CrookedRoadMath/` at the repository root | Offline exact qualification and seeded Monte Carlo measurements. |

Authentication and the wallet remain host services. Crooked Road owns its gameplay and serializes validated native state into the shared paid store's `NativeStateJson`. It explicitly disables legacy free-game tracking, selects the standard wallet ledger IDs based on the settlement ID, and imports no previous game's entitlement or implementation. Ledger naming and legacy-entitlement tracking are independent shared-store options; existing four-argument callers retain their legacy defaults.

The bundled route is `CrookedRoadHost` → `CrookedRoadBundled` → package `CrookedRoadGame`. The independent release uses `src/browser.ts` → that same game component → `useCrookedRoad` + `CrookedRoadView` + `api.ts`. Neither path calls a host slot renderer, host controller, or showcase builder. The application supplies only account/session transport and its generic navigation shell. The manifest remains catalogue compatibility data and does not execute the game.

Every paid reel spin and every zero-charge destination step follows the same prepared-round pipeline: freeze the request, state and bounded random-index tape; settle the wallet idempotently; then commit the resulting state and protected evidence. A lost reply resumes the original operation rather than drawing again. Ordinary wagers use cryptographic randomness. Destination steps use `UseFreeSpin=true` with the server's locked funded wager; this is a transport field for the active destination, not a separate source of free-game value.

Road edits and destination choices use revision-checked, idempotent actions. They never transfer money, and they are blocked while a prepared spin is unresolved. Hill prize decks remain private; public state exposes selections and completed reveals without the unchosen prizes.

The worker owns these routes:

- `POST /api/slots/prepared-spins` and `POST /api/slots/spins`: the same native prepared-spin handler.
- `GET /api/slots/crooked-road/state`: native state; the application host forwards its generic `/api/slots/state?gameId=crooked-road-v1` request here.
- `POST /api/slots/crooked-road/actions`: map edits, setting off, and destination choices.

Both mutation routes require an `Idempotency-Key`. `CrookedRoad:AllowNewPaidRounds` controls fresh paid admission; exact recovery and already-funded destination steps can continue while new wagers are paused. There is no product demo route, purchased boost, energy meter, or shared classic collection mechanic.

## Authoritative rules

[`src/FortuneForge.Games.CrookedRoad/Data/crooked-road-v1.json`](src/FortuneForge.Games.CrookedRoad/Data/crooked-road-v1.json) is the single source for symbol weights and pays, paylines, payout scaling, wager limits, funding, bonus distributions, and road geometry. The C# domain embeds it. Client `check` and `build` regenerate `src/assets/contracts/crooked-road-v1.json` from it through `scripts/copy-static.mjs --sync-contract`; edit the canonical file rather than the mirror.

The base game has five reels, three visible rows, and ten paylines. Each visible cell is an independent weighted symbol draw. Six symbols pay for three, four, or five consecutive matches from the left. `ACE` substitutes for paying symbols, with the highest valid interpretation paying once per line; an entirely wild line uses that same rule. The paytable stores hundredths of the wager, followed by the canonical rational scale. Each line award rounds down to integer points. Wagers are 100–5000 points in increments of 100, with one point worth one cent.

Every paid spin earns one road piece and one funding unit. Twenty paid spins earn one ticket whose funded wager is the floor of those twenty wagers' average. Three or more `FREE` symbols anywhere on a paid board earn one additional ticket at that spin's wager. Both tickets can be earned on the same spin. `POWER` is nonpaying and grants no additional monetary award. Destination steps earn no tiles, funding units, tickets, or retriggers.

The ticket bank holds sixteen tickets. Paid admission checks the maximum possible ticket award before drawing and requires ticket redemption when that award would exceed the bank. Earned value is never silently discarded. The two-piece hand and twelve-piece queue have lossless overflow counters by tile kind.

## Reusable roads and destinations

The 3×3 map starts at the south edge of cell 7. Destinations are the Well west of cell 3, Fort north of cell 1, and Hill east of cell 5; cells are numbered 0–8 in row order. Straight, corner, and junction pieces rotate in quarter turns. Connections require matching ports on both neighboring tiles.

Setting off requires a connected route and one funded ticket. It consumes the ticket and clears the connected route from the map, returning those pieces to the front of the hand and queue. Unused branches and reserves remain. Pieces are reusable, so redeeming a banked ticket does not require a further paid spin to replace the previous route.

| Destination | Actual play | Award timing |
| --- | --- | --- |
| Wishing Well | Choose one of three spring rows before play. Three native reel boards receive progressively wider wild ripples centered on the middle reel, with radii 0, 1, and 2. Each spring position has its own exact normalization. | Each of the three boards pays its normalized feature award. |
| Fairy Fort | A separate 3×3 grid automatically holds coin symbols. Each empty cell has a 1-in-4 landing chance per respin. Any new hold resets the three-miss counter; three misses or a full grid end the feature. The feature is finite, with at most 27 steps. | The terminal sum of held scores is converted through the exact feature normalization. Raw coin scores are not wager multipliers. |
| Hollow Hill | Choose one of three doors in each of three stages. Each stage's prize layout is independently shuffled when the journey starts and remains fixed. The choice is committed before the following prepared step reveals and settles it. | Each chosen door pays its configured prize at the funded wager. |

Each destination has mean return **6× the funded wager before downward rounding**, with distinct mechanics and variance. This is an expectation, not a guaranteed award. Before rounding, all spring positions and door selections share the configured expected return; integer-point rounding can produce small differences in the final averages. The funded wager can be an integer between normal cash-wager increments; destination admission accepts the saved funded value and locks it for the journey.

## Math qualification and its limits

For the current canonical contract, the exact pre-rounding return bound is:

```text
base return                         = 0.6265951801158
twenty-spin funded tickets          = 6 / 20
scatter tickets                     = 6 × P[Binomial(15, 0.02) >= 3]
combined analytic upper bound       = 0.9448314274950068
exact combined fraction             = 14416983451767071409612273 / 15258789062500000000000000
```

Thus the analytic upper bound is **94.4831427495%**, below the repository's 95% release ceiling. `CrookedRoadMath` obtains it from exact independent-line enumeration, a finite Fort recurrence, separately normalized Well spring expectations, and the symmetric Hill door expectation. Downward payout/funded-wager rounding and unredeemed funding can reduce returns. The calculation assumes the configured independent, uniform random-index source.

The recorded reports are:

- [`math-v1-seed-20261122.json`](../../output/artwork/irish-crooked-road-2026-11-22/qualification/math-v1-seed-20261122.json): 100,000 paid spins for each of nine destination/wager policies.
- [`math-v1-seed-20261123-1m.json`](../../output/artwork/irish-crooked-road-2026-11-22/qualification/math-v1-seed-20261123-1m.json): 1,000,000 paid spins per policy, with observed returns of approximately 94.076%–94.777%.

The harness immediately redeems all earned tickets under ideal routing, without adding tickets or stake. It uses the production domain rules and destination engines while bypassing map placement for this upper-return measurement. Real route placement and paid settlement are exercised separately by lifecycle tests. Reports retain funding state across batches and show approximate batch confidence intervals. Some finite-sample intervals cross 95%; the exact analytic bound is the qualification criterion. Measurements are regression evidence, not certified RTP, and policy rows sharing a seed are not pooled as independent samples. Rule changes require renewed qualification.

## Local paid-game testing

For an isolated browser shell independent of the main site's development server, start the paid fixture described below, then run `npm run local --workspace @fortuneforge/games-crooked-road`. Open `http://127.0.0.1:5175/slots/crooked-road`. The `local/` adapter mounts the exact production `CrookedRoadGame`, obtains the isolated fixture account, and injects authenticated transport plus a generic balance navbar. It supplies no gameplay engine, outcomes, bonus triggers or altered rules. These local files are excluded from the published package and release entry.

Run commands from the repository root with .NET 10 and restored workspace dependencies. Browser QA uses three terminal processes: the loopback Firestore emulator, the paid fixture on port 5299, and Vite on port 5174. Start the emulator in the first terminal:

```powershell
firebase emulators:start --only firestore --project demo-crooked-road-local --config scripts/e2e/firebase-emulator.json
```

Set the actual emulator endpoint for persistence tests; the supplied QA configuration uses `127.0.0.1:8787`. Tests create unique emulator projects and skip when no loopback emulator is configured.

```powershell
$env:FIRESTORE_EMULATOR_HOST = '127.0.0.1:8787'
dotnet test games/CrookedRoad/tests/FortuneForge.Games.CrookedRoad.Tests/FortuneForge.Games.CrookedRoad.Tests.csproj
dotnet build games/CrookedRoad/server/FortuneForge.Games.CrookedRoad.Server/FortuneForge.Games.CrookedRoad.Server.csproj
npm run check --workspace @fortuneforge/games-crooked-road
npm run test --workspace @fortuneforge/games-crooked-road
npm run build --workspace @fortuneforge/games-crooked-road
```

Run the test-only fixture in the second terminal:

```powershell
$env:FIRESTORE_EMULATOR_HOST = '127.0.0.1:8787'
dotnet run --project games/CrookedRoad/tests/FortuneForge.Games.CrookedRoad.FixtureHost/FortuneForge.Games.CrookedRoad.FixtureHost.csproj -- --port=5299
```

In the third terminal, point the existing Vite API proxy at that fixture and open `http://127.0.0.1:5174/slots/crooked-road`. The browser QA configuration pins these dedicated ports:

```powershell
$env:FORTUNEFORGE_API_URL = 'http://127.0.0.1:5299'
npm run dev --workspace fortuneforge.client -- --host 127.0.0.1 --port 5174 --strictPort
```

With all three processes running, execute the browser checks from another shell:

```powershell
node node_modules/@playwright/test/cli.js test --config fortuneforge.client/playwright.crooked-road.config.ts
```

The fixture calls the unchanged production paid endpoint mapper, native engine, cryptographic random source, prepared store, actions, and payout rules. Only the local account resolver and isolated wallet transport are substitutes. Its `/api/accounts/me` provider reads that wallet, and its generic state path rewrites to the native state handler with the prepared-spin response header. It binds to loopback and requires a loopback-only emulator. Each fixture start creates a new isolated project and wallet; it exposes no prize triggers or alternative game rules. These fixture services are confined to the test project and are not registered by the product worker.

To reproduce offline qualification:

```powershell
dotnet run --project tools/FortuneForge.CrookedRoadMath/FortuneForge.CrookedRoadMath.csproj --configuration Release -- --cycles=1000000 --seed=20261123 --wagers=100,1000,5000 --output=output/artwork/irish-crooked-road-2026-11-22/qualification/math-v1-seed-20261123-1m.json
```
