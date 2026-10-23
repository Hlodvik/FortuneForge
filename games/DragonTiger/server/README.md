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

## Host integration

Reference `FortuneForge.Games.DragonTiger.Server.csproj`. In startup, call `services.AddDragonTigerServer`, setting the existing read and wager rate-limit policy names. In the host pipeline, call `app.MapDragonTiger` with an account resolver returning the authenticated account's user ID and `SlotsCredits` balance. The host owns its existing cookie and account service. Add the game module's descriptor to the host catalogue separately.

The feature flag is `Features:DragonTigerEnabled`, and defaults to false. The main application must opt in after verification. `X-FortuneForge-Practice: true` uses the separate per-account practice wallet; the standalone sample forces practice regardless of this header. Practice and account rounds have separate stores.

The connected wallet uses the existing `userBalances/{userId}_slotsCredits`, `available` whole-rand value, and `availableFractionalCents` remainder. One Firestore transaction records the stake debit, optional payout, settled round, event, current shoe, latest-round pointer, and final wallet balance. Every ledger mutation uses integer cents internally. The ledger `amount` and `balanceAfter` fields follow the existing wallet's numeric-rand convention. Replays verify recorded settlement, ledger identity/amount/balance, and event integrity; corrupt persisted data fails without an additional debit.

## API

All paths are under `/api/games/dragon-tiger`; money is JSON numeric rand values, and cards use lowercase English rank and suit names.

| Request | Response |
| --- | --- |
| `GET /status` | `available`, `minimumStake` = 0.50, `maximumStake` = 100, `stakeIncrement` = 0.50, `maximumTotalStake` = 100, `balance`, `mode` |
| `POST /rounds` | A settled round; body `{ "dragonStake": 0.50, "tigerStake": 0, "tieStake": 0.50 }`; requires `Idempotency-Key` |
| `GET /rounds/{roundId}` | An owned settled round with the current wallet balance |
| `GET /rounds/latest` | The latest owned round, or HTTP 204 if none |

`Idempotency-Key` must contain 16–128 ASCII letters, digits, hyphens, or underscores. Reusing the key with identical stakes returns the original round and current balance without another wager or shoe advance. Different stakes on the same key return HTTP 409. The client must retain its original request and key until its outcome is known, including after refresh or a network timeout. GET recovery must be scoped to the same authenticated account and practice/account mode.

The settled round contains `roundId`, `balance`, `phase: "settled"`, `dragonCard`, `tigerCard`, `dragonValue`, `tigerValue`, `outcome`, `bets`, `totalStake`, `profit`, `totalReturn`, `shoeCardsUsed`, and `shoeCardsRemaining`. `bets` always has Dragon, Tiger, and Tie entries in that order, with `side`, `stake`, `disposition`, `profit`, and `totalReturn`; an empty spot is `not-placed` with numeric zeros. A tied main bet is `half-loss`.

HTTP 400 means the wager was invalid. HTTP 401 requires authentication. HTTP 404 means that account cannot access the round. HTTP 409 means conflicting stakes or insufficient funds. HTTP 503 `dragon-tiger-retry-required` means wallet contention or a transient database failure: retain the request and key and retry it. HTTP 500 and network failures are also ambiguous; preserve the pending request and recover rather than submitting a fresh key. Feature-disabled HTTP 503 accepts no wager. Responses disable caching.

## Rules and paytable

Eight standard 52-card decks, no jokers. One card goes to Dragon and then one to Tiger. Ace = 1 through King = 13; suits never break a tie. Dragon and Tiger win at **1:1 profit**. On any equal-rank tie, each Dragon/Tiger stake loses half and returns half; a Tie bet wins at **8:1 profit**, or nine times stake total return. There is no suited-tie bet or bonus multiplier. All three bets are independent and may be combined. Each placed spot is R0.50–R100.00 in R0.50 steps, and combined stakes cannot exceed R100.00 per deal. R0.50 steps make every half-stake return an exact number of cents.

The eight-deck shoe persists per player and mode. Two cards are consumed on a new accepted deal; replay consumes none. A cryptographically shuffled replacement shoe is used before the deal following 312 cards consumed (75% penetration). The server alone selects cards and calculates settlement.

The 8:1 Tie paytable and main-bet half-loss match [SA Gaming's published Dragon Tiger rules](https://rules.sa-portal.com/swp_en/dragon-tiger/). Visual references from another provider do not imply that provider's separate Tie paytable applies here.
