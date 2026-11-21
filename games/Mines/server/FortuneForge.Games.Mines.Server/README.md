# Mines server package

The host supplies normal account authentication through `MinesAccountResolver`; no player
request supplies a trusted account ID. `AddMinesServer` registers package services, and
`MapMines` maps the four account operations. The independent Mines worker supplies its Firestore
client, rate limits and exact allowed-origin list. Its `GameWorkerModule` alone registers
`AddGameWalletClient("mines")`; general `AddMinesServer` does not register an unkeyed wallet client
in a shared host. Paid operation without the correct worker wallet registration fails closed.
The isolated sample separately uses
`ForcePractice` and a private file ledger for non-cash development testing.

## API and gates

| Method | Path | JSON body |
| --- | --- | --- |
| GET | `/api/games/mines/session` | none |
| POST | `/api/games/mines/rounds` | `{ "requestId":"<stable unique ID>", "wagerCents":100, "mineCount":3 }` |
| POST | `/api/games/mines/rounds/{roundId}/reveal` | `{ "requestId":"<stable unique ID>", "tile":24 }` |
| POST | `/api/games/mines/rounds/{roundId}/cashout` | `{ "requestId":"<stable unique ID>" }` |

Both `Features:MinesEnabled` and `Features:MinesPaidEnabled` must permit a genuinely new paid
round. Existing commands, session recovery, reveals and cash-outs remain available when new
wagers are paused. The gateway must keep those routes assigned to the same configured worker.
The main host rejects an explicit practice header with 403; it does not silently charge that
request to a real wallet. `AllowedOrigins` is an exact host configuration allowlist;
`PracticeOrigins` never grants paid access. Mutation requests require JSON and trusted origin
or acceptable same-site fetch metadata. Responses are no-store.

Fresh sessions advertise `balanceCents`, `practice`, nullable `round`,
`minimumWagerCents=100`, `maximumWagerCents=500`, `houseEdgeBasisPoints=1000` and
`payoutPolicyVersion=2`.

Public rounds contain the accepted wager/mine count, status (`active`, `lost`, `consolation`,
`cashed-out`, `cleared`), safe/gems-remaining counts, current/next multiplier, exact cash-out and
payout cents, frozen edge and policy, `nextMinePayoutCents`, and 25 public tiles.
Active unselected tiles are always hidden; terminal views reveal the fixed board. No private
state, account ID, mine mask, seed or RNG data is returned. Client-supplied policy, payout or board
fields cannot change the server-accepted terms.

Errors are `{code,message}`: invalid 400, unauthenticated 401, practice/origin forbidden 403,
wrong-player or absent round 404, conflict/insufficient funds 409, disabled/retryable service 503.
Unexpected errors return no private exception/state.

## Versioned math and immutable records

New rounds accept R1–R5 and policy 2. The exact rational safe curve is bounded at 50× and extends
to every safe tile; only a mine, manual cash-out or natural full clear ends the round. Late-game
mine returns preserve 90% nominal expected RTP, with a single whole-cent floor at settlement.
The largest new round return is 25,000 cents (R250). See [RULES.md](../../RULES.md).

State freezes both edge and policy. Missing historical policy defaults to 1 (uncapped), missing
edge to 100 basis points (99%). Already accepted policy-1 99% and 90% rounds retain original
R0.10–R100 accepted wager limits and payout liability. Successful old JSON receipts also default
to original values; they are immutable snapshots, never rewritten or paid again.
A fresh session read after replay is required before allowing further client input.

`requestId` is an account-scoped idempotency key (16–128 ASCII letters/digits/underscore/hyphen),
bound to the complete action and payload. A duplicate returns its original response; changed
payload/action reuse conflicts. Successful receipts have no expiry. Malformed input does not
consume an ID. A valid insufficient-funds admission is immutable, including after a later top-up.

## Transactions, privacy and history

The game package owns the private board, accepted wager/policy, command admission, transitions,
resume pointer, outcomes and history. It never writes `userBalances` or `balanceTransactions`.
The shared account API performs generic signed wallet operations only.

A Firestore transaction first saves the exact private command intent and an account-scoped pending
pointer. A new round uses the signed wallet `Reserve` phase before its board is returned. Terminal
cash-out, full clear, mine loss or consolation uses `CompleteReservation` with that original
reservation digest. Safe nonterminal reveals require no wallet mutation. A final game transaction
records the response, state, audit/participation evidence and one terminal history entry.

A lost reserve or payout reply is recovered by resubmitting the identical stored wallet operation.
Every wallet response and saved response balance is checked against its account, operation and
outcome. The pending pointer serializes competing starts and reveal/cash-out races across replicas.
Before a terminal wallet call, existing history is checked so an inconsistent legacy active head
cannot cause another credit. History contains no hidden board or command data.

Historical `minesRounds`, `minesSessions` and schema-1 `minesCommands` remain readable. Legacy
active rounds have already paid their wager; their terminal operation is a credit-only signed
`Direct` settlement, never another debit. Their frozen wager, house edge and payout policy survive
new-entry limits. Old immutable replies are returned without rewriting them.

Private collections are `minesRounds`, `minesSessions` and `minesCommands`.
The root tracked Firestore rules deny every direct browser read/write; server access is separately
controlled by IAM. Deploy/verify that effective policy before enabling paid play.
The loopback-only privacy proof exercises browser REST requests with anonymous, owner,
different-player and forged-admin identities, including nested records. Server-SDK emulator tests
alone cannot prove browser rules are restrictive.

`FileMinesPracticeStore` is exclusively a local development store with separate non-cash balances.
It uses an exclusive cross-process lock and atomic ledger replacement and survives restart.
It is not a distributed production backend. Preserve its ignored ledger when restarting samples.

## Worker configuration and deployment

Configure the worker with `GameWorker:Mode=hosted`, its `GameWorker:SigningKey`, hosted Firestore,
`GameWallet:Url` and the game-specific `GameWallet:SigningKey`. The shared API requires matching
`GameWallet:Games:mines:SigningKey`, `AllowAccountBalanceRead=true` and
`AllowWagerReservations=true`. This game uses neither legacy free-game spending nor legacy-slot
ledger layout. Configure the public gateway worker URL/key and preserve forwarding during pauses.

Local source changes and local test results are separate from production deployment. This work
does not enable a live feature, change live wallet data, or deploy a worker/client/API. Current
lane evidence is recorded in `artifacts/backend-completion/table-number-subhandoff.json`.

## Verification

```powershell
dotnet test games/Mines/tests/FortuneForge.Games.Mines.Server.Tests --configuration Release
dotnet test FortuneForge.Server.Tests --configuration Release --filter FullyQualifiedName~Mines
node scripts/verify-firestore-privacy.mjs
```

Backend tests cover exact capped full-clear and consolation payouts, original legacy terms,
idempotency, concurrency, history, account-only enforcement, origins, malicious fields, privacy
and recovery. Firestore tests use a unique `demo-mines-*` project on localhost; no production
credentials or funds are used. If offline NuGet auditing blocks an isolated restore, use
`-p:NuGetAudit=false` for that invocation only; do not weaken committed security configuration.
