# Fortune Forge Craps client

`@fortuneforge/games-craps` is the reusable browser UI for the `FortuneForge.Games.Craps` rules package. The host supplies authentication and a gateway. Production mounts require the wallet-backed `paid-pass-line` mode; local sample play remains a separate testing fixture.

## Host contract

Mount `CrapsGame` with a `CrapsGateway`. `HttpCrapsGateway` targets the manifest-compatible `/api/games/craps` route by default. The Fortune Forge website can inject its authenticated adapter and shell navigation while retaining the packaged table, styles, assets, accessibility, and game-specific interaction flow.

```tsx
import { CrapsGame, HttpCrapsGateway } from '@fortuneforge/games-craps'
import '@fortuneforge/games-craps/styles.css'
import tableArtworkUrl from '@fortuneforge/games-craps/table-art.png'

const gateway = new HttpCrapsGateway('/api/games/craps')

export function CrapsRoute() {
  return <CrapsGame gateway={gateway} playerId="authenticated-account-id" requirePaid tableArtworkUrl={tableArtworkUrl} />
}
```

## Supported paid-table rules

Each positive wager is R1–R100 in whole Rand. The same fixed range applies to odds; this table does not use 3-4-5x odds limits. Pass Line principal is accepted before the come-out and remains locked until resolution. Come-out 7/11 wins 1:1, 2/3/12 loses, and 4/5/6/8/9/10 establishes the point. During the point, rolling the point wins 1:1 and 7 loses; other rolls continue the hand.

Between rolls while a point exists, players may increase, reduce, remove and re-add Pass Line odds or Field, Any Seven and Any Craps. Each command sets an absolute amount. Zero removes an existing working wager; zero for an absent wager and an unchanged amount are rejected without moving money. The wallet is debited or refunded only the difference. There is one working wager per kind. Odds remain working until the point or 7; each side bet settles exactly once on its specified next roll and does not renew automatically. Come-out side bets are accepted with the initial Pass Line wager; accepted come-out amounts are locked before that first roll.

Profit payouts are Field 1:1 on 3/4/9/10/11 and 2:1 on 2/12, Any Seven 4:1, and Any Craps 7:1 on 2/3/12. Odds pay 2:1 on points 4/10, 3:2 on 5/9 and 6:5 on 6/8. Displayed returns include the stake. Fractional returns remain in wallet cents. These are the supported rules, not a claim to implement every casino variant.

REBET restores the prior Pass Line and initial come-out side-bet layout. It excludes odds and bets placed for later rolls. After resolution, **Edit bet** restores that same layout as an editable draft.

## Paid continuation protocol

`POST /api/games/craps/rounds/{roundId}/wagers` accepts `{ kind, stake, operationId, expectedRevision, expectedRollNumber }`. Kind is `odds`, `field`, `any-seven` or `any-craps`; roll number is the next roll (`rolls.length + 1`). Roll and the compatibility odds endpoint also require the current revision and next roll number for new operations. A stale request is rejected and the client refreshes the authoritative hand and balance. Each accepted operation and its wallet entries commit in one transaction. An uncertain response must retry the identical operation ID, payload, revision and target roll, including after reload.

Responses include `revision`, `totalWagerCents` and `totalReturnCents`. Extras retain immutable wager identities and roll associations (`wagerId`, `placedForRoll`, `resolvedOnRoll`). Removed or replaced entries remain visible with `cancelled: true`; their stake is excluded from net wager totals, and refunds are excluded from payout totals. Historical records lacking those fields are read explicitly without inventing old roll identities or changing saved outcomes. Independent action and financial audits replay the complete accepted sequence and reconcile every wallet entry.

The paid frontend and backend form one release unit. Local sample gateways do not advertise paid continuation, and production does not fall back to local play when the wallet-backed API is unavailable.

## Reusable dice throws

`DiceThrow` exposes the same result-driven sprite animation used by the table. Supply the final face values and change `rollKey` for every new throw. Four 18-frame sprite sheets provide distinct center-origin, multi-axis tumbles with visible side faces, contacts, rebounds, and stopping times. Each sheet contains a row for every authoritative result, so the final orientation is part of the rendered motion. Larger hands cycle the four sheets. Optional landing positions are percentages of the stage.

```tsx
<DiceThrow
  values={[2, 5, 1, 6]}
  rollKey={rollNumber}
  landingPositions={[
    { x: 18, y: 32 },
    { x: 41, y: 69 },
    { x: 66, y: 38 },
    { x: 84, y: 70 },
  ]}
/>
```

## Local preview

```powershell
npm install
npm run dev
```

Open `http://127.0.0.1:5174`. The command also starts the local API adapter from `samples/FortuneForge.Games.Craps.Web`.

## Package checks

```powershell
npm run check
npm test
npm run build
```
