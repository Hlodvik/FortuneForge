# Fortune Forge Craps client

`@fortuneforge/games-craps` is the reusable browser UI for the `FortuneForge.Games.Craps` rules package. It deliberately does not know about Fortune Forge authentication, balances, payment providers, or settlement stores.

## Host contract

Mount `CrapsGame` with a `CrapsGateway`. `HttpCrapsGateway` targets the manifest-compatible `/api/games/craps` route by default. The Fortune Forge website can inject its authenticated adapter and shell navigation while retaining the packaged table, styles, assets, accessibility, and game-specific interaction flow.

```tsx
import { CrapsGame, HttpCrapsGateway } from '@fortuneforge/games-craps'
import '@fortuneforge/games-craps/styles.css'
import tableArtworkUrl from '@fortuneforge/games-craps/table-art.png'

const gateway = new HttpCrapsGateway('/api/games/craps')

export function CrapsRoute() {
  return <CrapsGame gateway={gateway} backHref="/games" playerName="Player" tableArtworkUrl={tableArtworkUrl} />
}
```

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
