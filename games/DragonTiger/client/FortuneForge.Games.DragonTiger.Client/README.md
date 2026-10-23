# @fortuneforge/games-dragon-tiger

Reusable solo Dragon Tiger table with a validated HTTP gateway, independently scoped styles, small artwork, sound controls, visible chip stacks and interrupted-deal recovery. See the [game README](../../README.md) for rules, references, local preview and verification.

```tsx
import { DragonTigerGame, HttpDragonTigerGateway } from '@fortuneforge/games-dragon-tiger'
import '@fortuneforge/games-dragon-tiger/styles.css'

<DragonTigerGame
  gateway={new HttpDragonTigerGateway('/api/games/dragon-tiger', authenticatedFetch)}
  playerId={`${accountId}:account`}
  onBalanceChange={refreshAccount}
/>
```

Create the gateway once (for example with `useMemo`). Use an account-and-mode-scoped `playerId`, and a matching React key, so pending requests and result history remain scoped to their owner. Never implement real wallet settlement in a browser gateway. The package's standalone development entry calls the isolated C# practice API.
