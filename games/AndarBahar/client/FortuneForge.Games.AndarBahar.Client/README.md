# @fortuneforge/games-andar-bahar

Reusable FortuneForge React 19 Andar Bahar table. Import the component and stylesheet:

```tsx
import { AndarBaharGame, HttpAndarBaharGateway } from '@fortuneforge/games-andar-bahar'
import '@fortuneforge/games-andar-bahar/styles.css'

const gateway = new HttpAndarBaharGateway('/api/games/andar-bahar', authenticatedFetch)
<AndarBaharGame gateway={gateway} playerId={`${userId}:account`} onBalanceChange={refreshAccount}/>
```

`gateway` accepts the authenticated host fetch wrapper. Never use a client rules engine or random outcomes for paid wagers. The supplied gateway validates the server's complete played prefix, fixed Andar-first ordering, first rank match, exact payout and wager identity; undealt deck fields are rejected. `currencySymbol` defaults to `R`; amounts are exact decimal rand at the API boundary.

`playerId` must identify the authenticated account and mode; change the React key when that scope changes. Pending requests use tab-scoped sessionStorage with the original idempotency key so a lost response and refresh can recover the same wager. History and mute use localStorage. Browser storage is required before accepting a wager.

The package owns the table and its game header. The host owns shared navigation/authentication/wallet. When placed in the shared shell's fixed-height body, set the component's `height:100%; min-height:0` as shown in the repository's prepared `games/AndarBahar/integration/AndarBaharRoute.css`.

The local sample is development practice only. Run `npm run dev:api` and `npm run dev:ui`; open http://127.0.0.1:5182. The API listens on 5202 and durably stores separate no-cash-value credit. It cannot access production funds.

Rules: a fresh standard 52-card deck, central Joker, Andar first then Bahar, stop on the first rank match. Andar profit pays 0.9:1; Bahar pays 1:1. A winning stake is returned in addition to profit; losing wagers return zero. R0.50–R100 in R0.50 steps, one main side per round. Skip only changes animation. See the repository's `games/AndarBahar/README.md`, `VERIFICATION.md` and `INTEGRATION.md` for exact mathematics, sources and host acceptance steps.

Original generated felt and approved FortuneForge chip assets are bundled. Shared card audio is CC0; see `src/assets/License.txt` included with this package.
