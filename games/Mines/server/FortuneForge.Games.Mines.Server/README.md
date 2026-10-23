# Mines server package

`AddMinesServer` registers the isolated development practice ledger. `MapMines` maps the four authenticated API operations. The host supplies its existing account authentication through `MinesAccountResolver`; the package never accepts a user ID from a player request and never owns production authentication.

```csharp
using FortuneForge.Games.Mines.Server;

services.AddMinesServer(options => {
    options.ReadRateLimitPolicy = "host-read-policy";
    options.WriteRateLimitPolicy = "host-write-policy";
    options.PracticeLedgerPath = "<private writable path outside web root>";
});
// Register the host's existing Google.Cloud.Firestore.FirestoreDb as usual.
endpoints.MapMines(async (context, cancellationToken) => {
    var account = await ResolveExistingHostAccount(context, cancellationToken);
    return account is null ? null : new MinesAccount(account.UserId);
});
```

The two configuration gates `Features:MinesEnabled` and `Features:MinesPaidEnabled` default to false. The general gate controls all endpoints. Paid storage is used only when both gates are true, `ForcePractice` is false, and the request does not select `X-FortuneForge-Practice: true`. The sample forces practice and explicitly leaves the paid gate false. Do not enable paid play until host integration and release verification are complete. No live configuration is changed by this package.

## HTTP contract

| Method | Path | JSON body |
| --- | --- | --- |
| GET | `/api/games/mines/session` | none |
| POST | `/api/games/mines/rounds` | `{ "requestId":"<unique stable ID>", "wagerCents":100, "mineCount":3 }` |
| POST | `/api/games/mines/rounds/{roundId}/reveal` | `{ "requestId":"<unique stable ID>", "tile":24 }` |
| POST | `/api/games/mines/rounds/{roundId}/cashout` | `{ "requestId":"<unique stable ID>" }` |

All successful operations return a `MinesSessionResponse`: `balanceCents`, `practice`, `round` (nullable), `minimumWagerCents` (10), `maximumWagerCents` (10000), and `houseEdgeBasisPoints` (100). Create returns 201; other success responses return 200. Every response is `Cache-Control: no-store`.

The public round contains `roundId`, `wagerCents`, `mineCount`, `status` (`active`, `lost`, `cashed-out`, `cleared`), `safeReveals`, `gemsRemaining`, `currentMultiplier`, nullable `nextMultiplier`, `cashOutCents`, `canCashOut`, `payoutCents`, and 25 `tiles` (`index`, `isRevealed`, `kind`: `hidden`/`gem`/`mine`). While active, every unselected tile remains `hidden`; no board seed, hash, mine mask, account ID, or private state is returned. Terminal rounds expose the fixed board and preserve `isRevealed` for the player's actual choices.

Errors return `{code,message}`. Invalid input is 400; wrong-player and absent rounds both return 404; active-round conflicts, repeated selections, payload reuse, and insufficient funds are 409. Disabled or retryable wallet service is 503. Unexpected failures are 500 with no private state or exception detail. Mutations require JSON and reject a foreign `Origin` (or cross-site fetch metadata if Origin is absent). Paid origins must match the host's own request scheme and authority; the host must configure trusted proxy forwarding normally and keep its account cookie/CSRF protections in place. `PracticeOrigins` grants only explicitly configured sample origins and never applies to paid requests.

## Durable transaction behavior

The engine uses an exact rational 99% gross-return curve and integer cents. The displayed decimal multiplier is never used for wallet credits. State is validated after persistence reads. The wager is validated, an existing active round is checked, and sufficient wallet funds are checked inside the transaction before the CSPRNG creates the fixed board. No player action can regenerate or move mines. Revealing all safe tiles automatically settles; normal cash-out requires at least one gem. `long` supports the verified maximum return of 51,482,970,000 cents at the maximum 10000-cent wager.

`FirestoreMinesStore` uses a single Firestore transaction to update the existing `userBalances/{userId}_slotsCredits` balance, a `balanceTransactions/mines-{commandId}` entry when money moves, the private round, the user's latest-round pointer, and the immutable idempotency receipt. Wallets retain the host's `available` whole units plus `availableFractionalCents` and increment `version` only when money moves. A missing production wallet has zero funds; it is never funded implicitly. Each account has at most one active Mines round. Concurrent starts and reveal/cash-out requests serialize through that account's wallet and session documents. Production collections are `minesRounds`, `minesSessions`, and `minesCommands`.

**All Mines collections must be server-only: deny direct client reads and writes in Firestore rules.** Private round JSON includes the board. Command receipts are also private despite containing only projected game responses. The host must audit broader wildcard rules before enabling paid play. Existing wallet/ledger permissions remain the host's responsibility; do not grant browser Firestore access to these game collections.

`FileMinesPracticeStore` provides a separate 100000-cent development balance per practice identity. It never resolves or writes a production wallet. It takes a cross-process exclusive file lock around reads and writes, serializes all practice state and command receipts, flushes a temporary ledger to disk, and atomically replaces the ledger. Instances and process restarts read the same durable data. Use it for the local sample; the whole-file ledger is not a production deployment backend and is not for distributed hosts/network filesystems.

`requestId` is an account-scoped idempotency key, 16–128 ASCII letters/digits/underscores/hyphens, bound to the full action, round ID and relevant payload. A duplicate successful command returns its exact historical public response without moving money or changing state. A changed payload/action using the same ID returns 409. Successful receipts have no expiry; retain them with their financial records. Failed validations do not consume an ID. Do not discard or prune successful receipts while a retry remains possible.

**Historical command receipts are intentionally snapshots.** After retrying an uncertain mutation, clients must GET `/session` and reconcile before enabling input. An older response can have an earlier balance or active state; the fresh session is authoritative. A historical replay never reopens a terminal round, replaces a newer round, debits again, or credits again. Refresh/resume always uses `/session`, not local client state. The client must retain a pending request ID/payload until recovery and isolate pending commands when the authenticated player changes.

## Local sample and verification

Run only this sample project; no root dependency install, solution edit, or host Startup change is required:

```powershell
dotnet run --project games/Mines/samples/FortuneForge.Games.Mines.Web --configuration Release
dotnet test games/Mines/tests/FortuneForge.Games.Mines.Server.Tests --configuration Release
```

The API binds loopback port 5201. The client on 5181 should proxy `/api/games/mines` to 5201 and use `credentials: "include"`. The sample uses an HttpOnly SameSite Strict `ff-mines-practice` cookie valid for 30 days. Its untracked `App_Data/mines-practice.json` survives server restarts. The sample returns and displays practice status; no production account credentials or funds are used.

Server tests cover the durable practice ledger, HTTP feature/auth/origin gates, private active projections, malicious client outcome fields, insufficient funds, request payload conflicts, concurrent starts/reveals/cash-outs, wrong-player privacy, restart recovery, lost-response/historical-receipt reconciliation, clean reset, and maximum auto-win. Firestore tests use a private random localhost emulator port and a unique `demo-mines-*` project, never real credentials. They use an already installed emulator JAR and Java, or an explicitly configured localhost `FIRESTORE_EMULATOR_HOST`. If local NuGet vulnerability lookup is unavailable, run restore with `-p:NuGetAudit=false` for this isolated test invocation, then use `--no-restore`; this does not edit project or host security configuration.
