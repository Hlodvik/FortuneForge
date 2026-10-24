# FortuneForge Hi-Lo

A separately packaged, single-player casino card game. The reusable client, pure .NET engine, authenticated API adapter, isolated practice sample and focused tests all live here. The October 23 integration connects `/games/hi-lo` to the main site's authenticated shell, account wallet, catalogue and guarded release checks.

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

The owner approved the visual pass and requested the October 23 mobile, sound and gameplay audit before committing and deploying the integrated game. Deployment completion is recorded separately in the guarded release artifacts.

### October 23 local visual pass

The preview now uses classic green felt and black-and-gold controls in the Dragon Tiger visual family, with the real shared navbar/card faces and image-based quick wager chips. The chip picker changes the draft wager only; DEAL remains the sole start/debit action. The existing authoritative engine and recovery protocol are unchanged.

The final client suite passes 83 tests, covering quick wagers, shared faces, skip/tie/cap outcomes, maintenance, uncertain-response recovery and server-confirmed outcome audio. The engine passes 35 tests. Local browser checks include a practice deal, free skip, full pre-guess stake refund, a successful call, refresh of that active round and exact cashout, plus bounded 320×568, 390×844, 844×390 and 820×1180 layouts. Mobile inputs avoid iPhone focus auto-zoom and primary targets remain at least 44 px. Hi-Lo is released separately after Dragon Tiger; no previous game's files are rolled back.
