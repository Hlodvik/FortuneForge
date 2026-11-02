# Mines server package

The host supplies normal account authentication through `MinesAccountResolver`; no player
request supplies a trusted account ID. `AddMinesServer` registers package services, and
`MapMines` maps the four account operations. The main website supplies its Firestore client,
rate limits and an exact allowed-origin list. The isolated sample separately uses
`ForcePractice` and a private file ledger for non-cash development testing.

## API and gates

| Method | Path | JSON body |
| --- | --- | --- |
| GET | `/api/games/mines/session` | none |
| POST | `/api/games/mines/rounds` | `{ "requestId":"<stable unique ID>", "wagerCents":100, "mineCount":3 }` |
| POST | `/api/games/mines/rounds/{roundId}/reveal` | `{ "requestId":"<stable unique ID>", "tile":24 }` |
| POST | `/api/games/mines/rounds/{roundId}/cashout` | `{ "requestId":"<stable unique ID>" }` |

Both the general feature flag and paid flag must permit account-credit play.
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
payload/action reuse conflicts. Successful receipts have no expiry. Failed validation does not
consume an ID.

## Transactions, privacy and history

A single Firestore transaction updates the existing account wallet, any financial ledger entry,
private round, latest-round pointer and immutable command receipt. Terminal settlement, including
a positive mine return, also creates exactly one shared history entry with frozen policy/edge.
History does not contain the hidden board or command data. Missing real wallets have zero funds.
Concurrent starts, reveal/cash-out races, response loss and restart/replay cannot duplicate credit.

Private collections are `minesRounds`, `minesSessions` and `minesCommands`.
The root tracked Firestore rules deny every direct browser read/write; server access is separately
controlled by IAM. Deploy/verify that effective policy before enabling paid play.
The loopback-only privacy proof exercises browser REST requests with anonymous, owner,
different-player and forged-admin identities, including nested records. Server-SDK emulator tests
alone cannot prove browser rules are restrictive.

`FileMinesPracticeStore` is exclusively a local development store with separate non-cash balances.
It uses an exclusive cross-process lock and atomic ledger replacement and survives restart.
It is not a distributed production backend. Preserve its ignored ledger when restarting samples.

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
