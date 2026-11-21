# `@fortuneforge/games-2048`

Reusable 2048 React client and HTTP gateway. For a local preview:

```powershell
cd games/TwentyFortyEight/client/FortuneForge.Games.TwentyFortyEight.Client
npm install
npm run dev
```

Open `http://127.0.0.1:5182`. The development host uses the real deterministic 2048 engine at `http://127.0.0.1:5192`.

## Authenticated session recovery

The private 2048 backend owns the board, rules, undo history, completion evidence and replay. This game uses account free play; it has no paid admission or wallet settlement. Existing API URLs remain under `/api/games/2048`.

Bind recovery to the authenticated account when creating the gateway:

```ts
import { createGameCommandRecovery } from '@fortuneforge/client-presentation/game-command-recovery'

const recovery = createGameCommandRecovery({
  accountId: account.userId,
  gameId: '2048',
  routePrefix: '/api/games/2048',
})
const gateway = new HttpTwentyFortyEightGateway(
  '/api/games/2048', authenticatedFetch, recovery, account.userId,
)
```

`recoverGame()` replays any pending serialized action with its original idempotency key, then reads the latest saved board. With no pending action it reads the active board; a new board starts when there is no saved session. A different mutation waits for recovery of an unresolved action. Transport errors, transient responses and invalid success payloads preserve the pending command.

The shared gateway opt-in is `GameWorkers:2048:ForwardPublicRoutes`. New private sessions have version7 GUIDs so their reads and actions stay on the authoritative worker even if new-session forwarding is paused. Older host-memory sessions keep their existing compatibility path. Local tests and builds do not deploy this configuration.

Existing version4 host-memory boards recover uncertain actions with a read of the current board, avoiding a second application on the receiptless compatibility service. Those boards retain their original host-memory lifetime; only version7 private sessions provide cross-replica exact mutation replay. An old memory-only start whose response never supplied a board ID cannot be recovered by ID.
