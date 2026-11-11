# Fortune Forge slot game runtime

Neutral browser contracts and composition helpers for independently owned slot
clients. This package contains no host application imports, authenticated
transport, Firebase code, navbar code, game media, or game controller.

Game packages can use it for manifests, catalogue metadata, experience/rules
configuration, themed symbols, showcase composition, mascot contracts, and the
versioned engine-guide presentation snapshot. Rendering primitives remain in
`@fortuneforge/slot-client-core`; game-specific controllers, validators,
features, artwork, audio and CSS remain in each game package.

The application host is responsible for supplying authenticated transport,
account state, wallet settlement, the shared `InGameShell`/navbar, payment
alerts and route-level lazy loading.

From the repository root, after the package is registered as a workspace:

```text
npm run check --workspace @fortuneforge/slot-game-runtime
npm run build --workspace @fortuneforge/slot-game-runtime
```
