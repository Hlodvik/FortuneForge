# Roulette backend integration

`FortuneForge.Games.Roulette.Server` owns the shared single-zero table, membership, wagers, local table-credit balances, managed actions, background lifecycle, pocket outcome, command receipts, private history and replay. The current mode remains `free-play-shared-single-zero`; account money is never debited or credited by Roulette.

The public `/api/games/roulette` status, round, bet and spin URLs remain. Mutations carry an `Idempotency-Key`; bodies can include `expectedRevision`. DELETE wager commands also retain their exact JSON bodies. A repeated operation returns its original response before checking whether fresh actions are paused. Changed bodies or stale revisions are rejected without another bet or spin. `GET /rounds/{roundId}/history/{roundNumber}` returns only the authenticated participant's recorded view.

The gameplay revision changes when a command or managed transition changes the table. Participant polling appends its own audit sequence without invalidating an otherwise current command; presence-only refreshes do not advance gameplay revisions. Earlier checkpoints without a separate revision resume from their recorded event version. Status availability follows the fresh-action gate, independently of recovery reads and exact saved retries.

Firestore owns the active table roster, per-table checkpoints, private participant history and operation receipts. The package checks each restored checkpoint against its saved bets and pocket. Table discovery remains oldest-first, with up to eight seats and separate balances. Open wagers survive disconnect maintenance. Every participant in a shared completed spin receives a private history view, including participants offline at settlement. The browser uses the shared account-scoped command journal, pins the expected account, and retries a pending command before starting another board. Authentication expiry, changed sessions and rate limits retain the pending identity.

`SignedRoulettePopulation` adapts the shared `IManagedPlayerRosterClient`, `ManagedTablePopulationDirector` and `ManagedPlayerTableStayPolicy`. It reserves, heartbeats and releases account-platform player leases; the Roulette package owns bet choices and table state. Configure `ManagedPlayerRoster:Url` and `ManagedPlayerRoster:SigningKey` on the worker and the matching game-scoped roster settings on the platform. Human display names come from the signed `IGameWalletAccountReader.PlayerName` contract. Enable `GameWallet:Games:roulette:AllowPlayerNameRead` on the platform and configure `GameWallet:Url`/`SigningKey` on the worker. This profile permission is independent of balance access; Roulette makes no settlement request. Missing trusted names prevent new table admissions, while exact saved-operation replay uses its original response without another profile read.

Managed seats renew through the shared signed `TryRenewAsync` contract before another table action. Confirmed lease loss retires those managed seats and refunds/removes only their open virtual wagers; human seats and wagers remain. Transport failures or malformed roster replies preserve the committed table and return a retryable response. Settled private history is created with the first accepted completion and remains unchanged when a managed seat later retires.

The hosted worker requires signed worker authentication and Firestore configuration. `Features:RouletteEnabled` gates new mutations. The shared gateway must activate `GameWorkers:roulette:ForwardPublicRoutes`, select the packaged worker, and retire the old host table worker. Shared project/registry references and the client dependency lock update belong to the shared integration owner. Production rollout must drain or explicitly carry any still-running legacy process-local tables; those old tables had no persisted checkpoint.

The repository currently serializes changes through one durable table roster and bounds active roster transactions to 256 tables. Production capacity and contention testing remain deployment work.

## Verification status

The package baseline passed 31/31 tests and four focused lease/history follow-ups passed, all with zero skipped. The refreshed selected standalone Release worker built with zero warnings and errors. Existing Roulette engine and shared-table tests are linked into the suite. Focused tests cover replica recovery, exact concurrent bet/spin retry, polling between commands, old checkpoints, stale revision rejection, private history, disconnected open wagers, malformed saved state, and the actual worker module's signed HTTP routes. The shared owner's signed MVC gateway test also passed with real profile and roster transports, trusted-name grants, free wallet invariance, DELETE bodies and private history. A narrow signed rerun after the lease/history guard change remains pending; no deployment has occurred.

```powershell
dotnet test games/Roulette/tests/FortuneForge.Games.Roulette.Tests/FortuneForge.Games.Roulette.Tests.csproj -m:1 -nr:false /p:UseSharedCompilation=false
```

Local client verification: 37 focused tests, TypeScript checking, Vite library build and declaration build passed. Shared gateway and deployment checks remain pending.
