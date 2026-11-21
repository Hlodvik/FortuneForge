# Dragon Tiger server package

This package owns the Dragon Tiger API, practice wallet, cryptographic eight-deck shoe, and Firestore adapter. It does not depend on `FortuneForge.Server`, authenticate accounts, or touch another game's code.

## Local preview

From the repository root:

```powershell
dotnet run --project games/DragonTiger/samples/FortuneForge.Games.DragonTiger.Web/FortuneForge.Games.DragonTiger.Web.csproj
```

The API listens at `http://127.0.0.1:5198`. The local sample always uses an isolated R10,000 practice wallet. It permits the client origins `http://127.0.0.1:5180` and `http://localhost:5180`. Restarting the sample clears its process-local practice wallet and history.

```powershell
dotnet test games/DragonTiger/tests/FortuneForge.Games.DragonTiger.Tests/FortuneForge.Games.DragonTiger.Tests.csproj
```

The tests include a real local Firestore emulator. Set `FIRESTORE_EMULATOR_HOST` to an existing localhost emulator, or have a Firebase emulator JAR cached under `~/.cache/firebase/emulators` and Java installed (the fixture finds a Windows Eclipse Adoptium installation). Every Firestore test uses a unique `demo-dragon-tiger-*` project; it cannot reach production credentials or endpoints. The fixture owns and stops only the emulator process it starts.

## Paid worker integration

The packaged worker is the production owner. Its module registers `AddDragonTigerServer` and `AddGameWalletClient("dragon-tiger")`. The shared host authenticates the account and forwards paid requests to this worker; the standalone sample and authorized host practice use the separate practice store.

Set worker `GameWallet:Url` and `GameWallet:SigningKey`. The generic account service needs the matching `GameWallet:Games:dragon-tiger:SigningKey` and `AllowAccountBalanceRead=true`. This game uses the standard wallet ledger; leave `LegacySlotLedger`, free-game allowances and reservations disabled. Signed worker authentication uses the existing `GameWorker` configuration. Deployment tooling and shared gateway activation remain owned by the shared integration lane.

`DragonTigerWalletStore` freezes a schema-2 intent containing the wager, original request observation, shoe and dealt cards. The signed wallet settles that immutable request exactly once. A subsequent game transaction commits the receipt, accepted shoe position, analytics/audit and activity. Pending intents recover through the original POST or GET/latest after a lost response, including while `Features:DragonTigerEnabled=false` blocks new keys. A declined key stays declined after a top-up.

`DragonTigerFirestoreStore` is a read-only adapter for schema-1 historical rounds. It checks their original saved cards, wagers, wallet ledger and event before replay; it cannot create a new paid round. The package does not update account balances or wallet ledger documents. Shared audit and wallet receipt rules come from `FortuneForge.Games.Firestore` and `GameWalletReceiptVerifier`.

This migration has local package verification; no production deployment is implied. Shared gateway routing must keep exact retries on this worker when new play is paused and fail unavailable when the worker cannot be reached. The paid browser gateway sends `X-FortuneForge-Expected-Account` for the account that opened the table. Journals survive session changes and recover a conflicting earlier tab intent through GET/latest before replaying the frozen command.

## API

All paths are under `/api/games/dragon-tiger`; money is JSON numeric rand values, and cards use lowercase English rank and suit names.

| Request | Response |
| --- | --- |
| `GET /status` | `available`, `minimumStake` = 0.50, `maximumStake` = 100, `stakeIncrement` = 0.50, `maximumTotalStake` = 100, `balance`, `mode` |
| `POST /rounds` | A settled round; body `{ "dragonStake": 0.50, "tigerStake": 0, "tieStake": 0.50 }`; requires `Idempotency-Key` |
| `GET /rounds/{roundId}` | An owned settled round and its original wallet receipt balance (historical schema 1 uses the current wallet balance) |
| `GET /rounds/latest` | The latest owned round, or HTTP 204 if none |

`Idempotency-Key` must contain 16–128 ASCII letters, digits, hyphens, or underscores. Reusing the key with identical stakes returns the original round and receipt balance without another wager or shoe advance; historical schema-1 rounds retain their current-wallet-balance convention. Different stakes on the same key return HTTP 409. The client must retain its original request and key until its outcome is known, including after refresh or a network timeout. GET recovery must be scoped to the same authenticated account and practice/account mode.

The settled round contains `roundId`, `balance`, `phase: "settled"`, `dragonCard`, `tigerCard`, `dragonValue`, `tigerValue`, `outcome`, `bets`, `totalStake`, `profit`, `totalReturn`, `shoeCardsUsed`, and `shoeCardsRemaining`. `bets` always has Dragon, Tiger, and Tie entries in that order, with `side`, `stake`, `disposition`, `profit`, and `totalReturn`; an empty spot is `not-placed` with numeric zeros. A tied main bet is `half-loss`.

HTTP 400 means the wager was invalid. HTTP 401 requires authentication. HTTP 404 means that account cannot access the round. HTTP 409 means conflicting stakes or insufficient funds. HTTP 503 `dragon-tiger-retry-required` means wallet contention or a transient database failure: retain the request and key and retry it. HTTP 500 and network failures are also ambiguous; preserve the pending request and recover rather than submitting a fresh key. Feature-disabled HTTP 503 rejects new keys while existing-key and GET/latest recovery remains available. Responses disable caching.

## Rules and paytable

Eight standard 52-card decks, no jokers. One card goes to Dragon and then one to Tiger. Ace = 1 through King = 13; suits never break a tie. Dragon and Tiger win at **1:1 profit**. On any equal-rank tie, each Dragon/Tiger stake loses half and returns half; a Tie bet wins at **8:1 profit**, or nine times stake total return. There is no suited-tie bet or bonus multiplier. All three bets are independent and may be combined. Each placed spot is R0.50–R100.00 in R0.50 steps, and combined stakes cannot exceed R100.00 per deal. R0.50 steps make every half-stake return an exact number of cents.

The eight-deck shoe persists per player and mode. Two cards are consumed on a new accepted deal; replay consumes none. A cryptographically shuffled replacement shoe is used before the deal following 312 cards consumed (75% penetration). The server alone selects cards and calculates settlement.

The 8:1 Tie paytable and main-bet half-loss match [SA Gaming's published Dragon Tiger rules](https://rules.sa-portal.com/swp_en/dragon-tiger/). Visual references from another provider do not imply that provider's separate Tie paytable applies here.
