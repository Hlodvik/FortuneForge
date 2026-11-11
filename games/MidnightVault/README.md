# Midnight Vault

Midnight Vault is an independently owned game package inside the Fortune Forge repository.
It deploys with the existing Firebase-hosted application, but its gameplay code does not
depend on another game or on the host's shared slot outcome engine.

## Ownership

- `src/FortuneForge.Games.MidnightVault` owns the rules, reel sets, symbols, paytables,
  outcome generation, feature transforms, payout evaluation, pity state, bonus-round
  queue, and current-version replay audit.
- `client/FortuneForge.Games.MidnightVault.Client` owns the browser controller, HTTP
  response validation, cabinet, help/paytable, bonus presentation, styles, audio, and
  game media.
- `FortuneForge.Server/Slots/Games/MidnightVault` is a thin adapter between the package
  and Fortune Forge account settlement/audit models.
- `fortuneforge.client/src/app/routes/MidnightVaultHost.tsx` supplies only the shared
  application shell, authenticated transport, account summary, and payment alerts.

Firebase authentication, wallet settlement, Firestore persistence, and deployment stay
host-owned. The public routes remain `/slots/dragon-hoard` and
`/slots/dragon-hoard/demo`, and the persisted game ID remains `dragon-hoard-v1` so
existing saves and audits continue to work.

## Changing the game

Make gameplay changes in this directory. Add a new immutable math/replay version when a
change would alter an already-recorded outcome; do not rewrite the frozen v1-v3 models.
Keep JSON field names, persisted IDs, and bonus-queue ordering compatible unless a
versioned migration is deliberately added at the host boundary.

The standalone source boundary can be inspected with:

```text
npm run verify:midnight-standalone
```
