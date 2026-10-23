# @fortuneforge/games-mines

React 19 Mines client. All outcomes and money come from the authenticated server gateway.

```tsx
import { MinesGame, HttpMinesGateway } from '@fortuneforge/games-mines'
import '@fortuneforge/games-mines/styles.css'

<MinesGame playerId={`${account.userId}:${practiceMode ? 'practice' : 'account'}`}
  gateway={gateway} onBalanceChange={refreshHostAccount} currencySymbol="R" />
```

`playerId` must identify the authenticated account **and** wallet mode. The component remounts its
internal session when this changes, isolating storage and pending commands across accounts.
The host owns authentication, shared `InGameShell`, account/practice navigation and account refresh.
The standalone preview imports the real shared shell directly only from `src/dev`; the packaged
library does not import application code. `onBalanceChange` receives **integer cents**, not currency
units; a host can simply refresh its existing account summary.

Construct `new HttpMinesGateway('/api/games/mines', fetchWithAccountSession)` for the host. In practice
mode use the existing `practiceAccountFetch(fetchWithAccountSession)` wrapper to send
`X-FortuneForge-Practice: true`. Default fetch credentials are same-origin. All POST bodies contain
the original `requestId`; no board, seed or outcome is sent by the browser.

The component persists only uncertain command identities/payloads, scoped by `playerId`. It keeps
input locked until the original command has been retried and a fresh `/session` read completes.
Historical replay responses are never used as the current balance. Failed financial responses are
not replaced with client-side balances or random outcomes. A storage failure prevents sending a
new mutation. A fresh server read recovers a damaged local recovery record.

Arrow keys move between covered board tiles; Enter/Space reveals. Tile focus remains reachable
after every reveal. Sound preference is stored as `fortuneforge:mines:muted`; optional browser audio
errors cannot interrupt play. See the package root README for build, test and preview commands.
