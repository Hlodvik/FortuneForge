# FortuneForge Hi-Lo

A separately packaged, single-player casino card game. The reusable client, pure .NET engine, authenticated API adapter, isolated practice sample and focused tests all live here. No central manifests, route registries, Startup, solution, release scripts or live configuration were changed.

## Review the game

The running local practice preview is **http://127.0.0.1:5183**, backed by its isolated durable practice API on **http://127.0.0.1:5203**. These credits have no cash value. The preview uses FortuneForge's shared navigation; production integration uses the existing authenticated game shell and wallet conventions.

Run `npm run dev:ui` and `npm run dev:api` in `client/FortuneForge.Games.HiLo.Client` if the preview needs restarting. The sample has no production credentials and binds loopback only. Existing repository dependencies suffice; do not run a root install for this package.

- [Rules and payout derivation](RULES.md)
- [Chosen digital reference and original asset provenance](VISUAL_REFERENCE.md)
- [Verification and screenshot evidence](VERIFICATION.md)
- [Exact central integration steps](INTEGRATION.md)
- [Client contract and recovery](client/FortuneForge.Games.HiLo.Client/README.md)
- [Server wallet, persistence and security](server/FortuneForge.Games.HiLo.Server/README.md)

## Rules

Ace is low, King is high, and equal ranks lose. Each card is an independent draw from a standard 52-card deck with replacement. Higher/lower probabilities depend on the current rank; the uncapped step factor is `0.99 / probability`. The 1% house edge applies to **each risked choice**, and compounds through a streak. Caps and final cent flooring can increase the effective edge. The interface displays exact server quotes, current return, multiplier, streak, revealed history, skips and accessible rules.

Skip replaces the current card without changing return or streak, up to 52 times per round. A player may cash out while active, including refunding the wager before the first guess. Successful calls stop and settle automatically at the smaller of 1000× stake or 100000 credits, or after 100 wins. The engine stores no future cards. Paid outcomes and debits/credits are server-authoritative and transactionally durable.

## Verification commands

From the repository root:

```powershell
dotnet test games/HiLo/tests/FortuneForge.Games.HiLo.Engine.Tests --configuration Release
dotnet test games/HiLo/tests/FortuneForge.Games.HiLo.Server.Tests --configuration Release
```

From the client directory:

```powershell
npm run check
npm run test
npm run build
npm run test:e2e
```

Server tests run a private Firestore emulator, never a live database. The browser test requires the local sample/UI and an installed Chrome browser; `HILO_BROWSER_CHANNEL` and `HILO_URL` may override the defaults. It saves screenshots and a machine-readable report into `evidence/`.

The package is prepared for coordinated host integration and owner review. Deployment and owner approval remain release gates.
