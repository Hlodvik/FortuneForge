# FortuneForge Mines

A single-player 5×5 Mines game with the approved dark slate design. Choose 1–24 mines, wager
R1–R5, reveal gems and cash out, or continue through every safe tile.

New rounds use **90% nominal RTP**, a **50× maximum gross return** and **no early automatic
cash-out**. A R5 wager can return at most **R250**. The late-game curve includes a disclosed mine
consolation return to preserve the expected RTP while allowing full-board play. Returns round down
once to whole cents. With current wagers the rounded expected RTP is above 89% and at most 90%.
Old accepted rounds retain their original terms. See [RULES.md](RULES.md) for the exact math.

## Main website

Source integration is complete: authenticated `/games/mines`, account-credit transactions,
catalogue, shared navigation/reporting, player history and admin history/visibility. There is no
website practice mode, URL override or paid-to-practice fallback. The main paid-release flag remains
off until the source, server-only Firestore rules and normal release verification are deployed.
No live deployment is implied by local integration. See [HOST_INTEGRATION.md](HOST_INTEGRATION.md).

## Isolated local preview

The sample API is a separate developer tool using non-cash test credits. It is not a practice mode
on the website and never loads production credentials or moves production funds.

Run in separate terminals from the repository root:

```powershell
dotnet run --project games/Mines/samples/FortuneForge.Games.Mines.Web --configuration Release
npm run dev --workspace @fortuneforge/games-mines
```

Open **http://127.0.0.1:5181/**; its API proxy uses **http://127.0.0.1:5201/**.
An HttpOnly sample cookie identifies the local test ledger. The ignored sample `App_Data`
directory survives restart and must stay outside a static web root. Existing ledgers retain their
historical accepted rounds. Refresh or Restore round reconciles uncertain commands without paying
or debiting again.

## Packages

- `src/FortuneForge.Games.Mines`: immutable .NET engine, cryptographic board generation and exact rational math.
- `server/FortuneForge.Games.Mines.Server`: account API, atomic Firestore wallet/round/history, separate development ledger.
- `client/FortuneForge.Games.Mines.Client`: React library, HTTP validation, responsive controls, audio and account-scoped recovery.
- `samples/FortuneForge.Games.Mines.Web`: isolated non-cash local API.
- `tests`: engine, HTTP, persistence and concurrency checks.

## Verification

```powershell
dotnet test games/Mines/tests/FortuneForge.Games.Mines.Tests --configuration Release
dotnet test games/Mines/tests/FortuneForge.Games.Mines.Server.Tests --configuration Release
npm run check --workspace @fortuneforge/games-mines
npm test --workspace @fortuneforge/games-mines
npm run build --workspace @fortuneforge/games-mines
node --test scripts/tests/verify-firestore-privacy.test.mjs
# With a localhost Firestore emulator running:
node scripts/verify-firestore-privacy.mjs
```

The privacy proof creates a unique `demo-*` project on an explicit loopback emulator, loads the
checked-in rules, proves mock Firebase authentication works, then checks 200 denied browser
read/list/create/update/delete operations across game and wallet collections, including nested
documents. It also verifies the privileged server path remains functional. It refuses production
or external targets. This is not a live IAM audit.

[Policy-2 verification](evidence/V2_VERIFICATION.md) records the completed scoped tests,
integrated account-flow checks and mobile measurements.

Historical browser E2E source and [evidence](evidence/e2e-report.json) remain available; those
October 20 screenshots/quotes used the older 99% policy and are visual evidence only, not current
payout evidence. Current verification should use the versioned rules and integrated account route.

## Visual reference

The approved slate layout retains a manual wager panel, square 5×5 grid, raised tiles, green gems,
dominant cash-out control and fast reveal feedback. FortuneForge navigation, generated game art
and shared CC0 chip audio are reused; no provider branding is shipped.
[Reference evaluation](evidence/REFERENCES.md) records the original professional-reference review.
