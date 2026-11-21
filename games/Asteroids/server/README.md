# Asteroids backend ownership

The game package owns the deterministic engine, persistent input-state replay rules,
canonical replay evidence, and authenticated free-run lifecycle. The server library
opens the existing `asteroidsFreeRuns` documents with the original schema version 1,
rules version 2, SHA-256 identity, seed encoding, and field names. Exact start and
completion retries return the first committed seed and outcome.

`GameWorkerModule.cs` composes the generic Firestore arcade journal. The library maps
the existing POST routes under `/api/arcade-competitions/asteroids/free/runs` and adds
authenticated GET `/{runId}` recovery. Completed recovery replays the stored canonical
inputs and verifies the digest, score, terminal state, and timestamp order.

`AsteroidsPaidRuns` owns paid run entropy, the original `asteroidsRuns` schema,
terminal replay verification, and completed retry validation. `AsteroidsPaidRunService`
uses the shared competition entry coordinator and resolves the committed attempt's
original window for recovery and completion. Paid endpoints preserve the existing
entry/replay URLs and add authenticated recovery. Generic standings and settlement
rules remain in the shared competition runtime.

The private worker composes the shared signed wallet debit/proof/payout client,
standings, prepared-entry recovery, and settlement scheduler. Paid entry intent
freezes its original expected window. Recovery-only requests cannot charge a new
competition window, and completed replay retries resolve the committed window.
Browser recovery retains the original request terms and uses a signed query control
for legacy empty-body recovery.

Paid routing requires an explicit exact-body digest accessor. The hosted worker
passes the authenticated request's verified SHA-256 through the endpoint and game
service into the prepared entry. Unsigned local workers use the shared trusted
capture before model binding, preserving the same exact-byte contract. Historical
direct composition callers may omit the digest; HTTP admission fails before preparing
or charging an entry when the digest is missing. No caller substitutes a
reserialized admission object for the actual request bytes.

Local implementation verification covers 34 Release .NET cases with no skips,
58 package browser cases, 26 focused catalogue/transport cases, and the package
typecheck and bundle. The final .NET follow-up verifies the authenticated original
body digest against both pending and accepted paid attempts across rollover.
The route account-binding regression passes all three cases, including original
paid intent bytes and key through both bearer retries and cookie fallback after an
account switch. Shared host activation and production deployment remain separate.

The selected Release private worker builds with zero warnings or errors. The
repeatable `../tests/worker-smoke.mjs` check passes local emulator and signed hosted
modes against isolated `demo-*` Firestore projects. It covers health, free
start/completion retries, recovery, standings, practice isolation, unsigned and
wrong-game rejection, and exact original whitespace/CRLF digest persistence.
Changed bytes conflict with the original pending key; identical recovery preserves
one pending intent. The wallet is deliberately unconfigured, so this check makes no
wallet RPC and creates no accepted paid run. Both workers stop after the check.
Results are saved in `artifacts/backend-completion/asteroids-worker-smoke.json` at
the repository root. Live signed platform gateway, wallet debit/payout, historical
host audit, registry and scheduler cutover remain shared integration checks.

The existing standalone preview component uses its sample API under
`/api/games/asteroids`; the catalogue uses `AsteroidsCompetitionPage` and
`/api/arcade-competitions/asteroids`. A release entry must retain that distinction.
The backend-only release entry builds with `GamePackage=Asteroids` and preserves
the existing catalogue competition frontend. No worker release has been activated.

Package verification:

```powershell
dotnet test games/Asteroids/tests/FortuneForge.Games.Asteroids.Tests/FortuneForge.Games.Asteroids.Tests.csproj -m:1 -nr:false
dotnet build games/shared/FortuneForge.Games.PackagedGame.Server/FortuneForge.Games.PackagedGame.Server.csproj -p:GamePackage=Asteroids -c Release -m:1 -nr:false
node games/Asteroids/tests/worker-smoke.mjs
npm test --workspace @fortuneforge/games-asteroids
```

Set `FIRESTORE_EMULATOR_HOST` to the running local lane emulator to reuse it; otherwise
the package tests use the repository's existing local emulator fixture.
They cover lost-response recovery across new service instances, exact retries,
account isolation, conflicting replays, existing host documents, corrupt persisted
outcomes, and nonterminal replay rejection. HTTP tests protect the legacy `wasReplay`
wire properties. Paid tests exercise one charge after a lost wallet response,
completed retries after rollover, zero new charges for an unaccepted earlier intent,
and a one-time refund of the actual legacy entry fee after a lost payout response. The host's
independent replay auditor and shared competition settlement checks remain shared
integration verification.
