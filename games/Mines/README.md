# FortuneForge Mines

An independently packaged, single-player 5×5 Mines game. The player chooses 1–24 mines, wagers,
reveals gems, and cashes out before hitting a mine. The server fixes the board at wager acceptance;
the browser receives no unrevealed results. Exact probability-based integer-cent payouts, durable
wallet transactions, recovery, and the responsive React client are included.

## Play locally

The isolated preview uses practice credits with no cash value. It never loads production credentials
or moves production funds. Start these commands in separate terminals from the repository root:

```powershell
dotnet run --project games/Mines/samples/FortuneForge.Games.Mines.Web --configuration Release
```

```powershell
Set-Location games/Mines/client/FortuneForge.Games.Mines.Client
npm run dev
```

Open **http://127.0.0.1:5181/**. The API uses **http://127.0.0.1:5201/**. These ports were free when
the sample started. Vite proxies `/api` to the sample. Existing root dependencies are reused;
no root install or manifest edits are needed for this preview. If the managed sandbox blocks local
sockets, launch these exact loopback-only commands with the environment's normal execution approval.

A persistent HttpOnly sample cookie identifies the local practice wallet, initially 100,000 cents.
The private ledger in the sample's ignored `App_Data` directory survives process restart. Keep that
directory outside any static web root. Refresh resumes the active round; Restore round retries an
uncertain action with its original durable request identity, then reads the current server session.

## Packages

- `src/FortuneForge.Games.Mines`: immutable .NET 10 engine, secure board generation, exact payout math.
- `server/FortuneForge.Games.Mines.Server`: authenticated API adapter, transactional Firestore wallet,
  private rounds, receipts, and separately funded durable development practice store.
- `client/FortuneForge.Games.Mines.Client`: React 19 library, typed HTTP gateway, audio, generated art,
  keyboard/touch controls, native modal rules, reduced motion, and account-scoped recovery.
- `samples/FortuneForge.Games.Mines.Web`: isolated practice API; production play disabled.
- `tests`: independent engine and server test projects.

See [RULES.md](RULES.md) for the derivation, house edge, rounding, uniform randomness proof and
maximum return. See [INTEGRATION.md](INTEGRATION.md) for all deliberately deferred shared changes.

## Verification

```powershell
dotnet test games/Mines/tests/FortuneForge.Games.Mines.Tests --configuration Release
dotnet test games/Mines/tests/FortuneForge.Games.Mines.Server.Tests --configuration Release
Set-Location games/Mines/client/FortuneForge.Games.Mines.Client
npm run check
npm test
npm run build
# With the two local servers running:
npm run test:e2e
```

The server suite includes real Firestore emulator transactions, wallet limits, exact maximum payout,
concurrent requests, cross-player privacy, origin/auth/feature gates, replay conflicts and durable
restart recovery. The client suite checks financial response boundaries, account changes, uncertain
requests, stale receipts, read-after-commit failures, local storage failures, controls and sounds.

On October 20, 2026: **17 engine tests, 23 server tests, 117 client tests**, TypeScript and library
build passed. Browser tests passed full wagering, safe reveal, cash-out, mine loss, clean reset,
keyboard, native touch, audio API activity, lost responses after server commit, and refresh recovery.
The [browser report](evidence/e2e-report.json) records measured layouts at 1440×900, 320×568,
360×640, 390×844, 430×932, 844×390 and 667×375. Desktop has no document or body scrolling.
Small portrait has 45px or larger board tiles; short landscape has 34px or larger tiles. All primary
controls and the whole board fit in the first viewport. Supporting content may scroll on mobile.

This package is ready for owner review and host integration. It has not been deployed or approved
as shippable in the owner-led review loop. The two host feature flags default false.

## Visual reference and evidence

Core engine tests passed before visual research began. Three actual digital game screens were viewed:
[Stake](https://files.bo3.gg/uploads/image/79352/image/webp-882f39db98959b87dcf531fa29deac5a.webp),
[Spribe](https://mines-slot.com/assets/images/8.webp), and
[Hacksaw](https://slotcatalog.com/userfiles/image/games/Hacksaw-Gaming/24304/Mines-Hacksaw-Gaming-6891946.jpg).
The live Stake page was also inspected; its region notice prevented playing there. No account was
created, real-money wager placed, or restriction bypassed. Screen references were viewed, not extracted
into the package. [Reference evaluation](evidence/REFERENCES.md) explains the chosen layout.

Stake's layout was selected: left manual wager panel, large square 5×5 grid, raised slate tiles,
high-contrast green gems, dominant cash-out control, and restrained near-instant reveal feedback.
FortuneForge owns its chrome and branding. The gem/mine and slate raster assets were generated for
this package; no provider logo or artwork is shipped. Shared FortuneForge navigation and CC0 chip
audio are reused. Screenshots: [desktop](evidence/desktop-active.png),
[cash-out](evidence/desktop-cashout.png), [loss](evidence/desktop-loss.png),
[320px portrait](evidence/portrait320.png), [390px portrait](evidence/portrait390.png),
[short landscape](evidence/landscape667.png).
