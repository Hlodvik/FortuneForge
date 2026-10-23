# @fortuneforge/games-hi-lo

Reusable FortuneForge casino card Hi-Lo client. The package consumes authoritative integer-cent snapshots; it never draws cards, chooses paid results, or updates a wallet locally. See the [game rules](../../RULES.md) and [host integration checklist](../../INTEGRATION.md).

```tsx
import { HiLoGame, HttpHiLoGateway } from '@fortuneforge/games-hi-lo'
import '@fortuneforge/games-hi-lo/styles.css'

<HiLoGame
  gateway={new HttpHiLoGateway('/api/games/hi-lo', authenticatedFetch)}
  playerId={`${accountId}:practice`}
  onBalanceChange={refreshAccount}
/>
```

Create the gateway once with `useMemo`. Supply an account-and-mode-scoped `playerId`, and use the same value as the React key when switching accounts or practice mode. The ID scopes browser recovery information; the server identifies the account solely from its authenticated cookie.

## Local practice preview

The isolated preview uses `http://127.0.0.1:5183`; Vite proxies `/api` to the isolated practice sample at `http://127.0.0.1:5203`. The sample's cookie and durable practice balances are separate from production accounts. Practice credits have no cash value.

Run commands in this package directory, using the existing repository dependencies:

```powershell
npm run dev:api
npm run dev:ui
```

Run the API and UI in separate terminals, or use `npm run dev`. Ports are strict so another game's server is never displaced. No root dependency installation is needed for the current workspace.

```powershell
npm run check
npm run test
npm run build
npm run test:e2e
```

If Windows sandbox permissions prevent Vitest from renaming its default temporary cache, use a package-local temporary directory for that invocation:

```powershell
$hiLoTemp = Join-Path (Get-Location).Path '.tmp'
New-Item -ItemType Directory -Force -Path $hiLoTemp | Out-Null
$env:TEMP = $hiLoTemp
$env:TMP = $hiLoTemp
npm run test
```

## HTTP and recovery contract

- `GET /api/games/hi-lo/session` restores the current wallet, limits, revealed history, round revision and next-choice quotes.
- `POST /api/games/hi-lo/rounds` submits `{ operationId, wagerCents }`.
- `POST /api/games/hi-lo/rounds/{roundId}/actions` submits `{ operationId, expectedRevision, choice }`; `choice` is `higher`, `lower`, `skip`, or `cashout`.
- Every response is a complete authoritative session snapshot. Every amount is integer cents. The quote multiplier is the total cumulative gross round multiplier if that choice succeeds.

The gateway sends cookie credentials, disables caching, and supplies the required `X-FortuneForge-Game: hi-lo` CSRF header on mutations. It validates response structure, revealed cards, limits, integral amounts, and the expected round/revision before exposing actionable UI state. Error responses retain `{ code, message }` and HTTP status.

`HiLoPendingOperationStore` stores only the exact pending request in `sessionStorage`, under the account-and-mode scope. `submitHiLoCommand` saves before sending and blocks a new request from replacing unresolved work. A lost connection, abort, incomplete success body or transient server failure keeps the original request. `recoverHiLoPendingCommand` repeats that request with its original operation ID and expected revision, then fetches the newest session because a duplicate response may be the operation's older original snapshot. Definitive server rejections clear the rejected request and permit a fresh authoritative read. Storage failure blocks a wager before it is sent.

Approved CC0 card/chip sounds are copied from `games/assets/audio/cards`, with their license preserved in `src/assets/audio`. `hiLoAudio.ts` exposes semantic cues, quiet volume, a persisted mute preference and silent handling of browser media-policy rejection. Trigger cues only from user interactions. Raster artwork and the final game UI are owned by this package.
