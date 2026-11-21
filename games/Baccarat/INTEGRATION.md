# Baccarat backend integration

`FortuneForge.Games.Baccarat.Server` owns production paid-round admission, the eight-deck shoe, dealing, pair wagers, settlement requests, durable recovery, reporting and outcome replay. The account platform authenticates requests and performs generic signed wallet operations.

The worker keeps `/api/games/baccarat/status`, `POST /rounds` and `GET /rounds/{roundId}`. It also supplies `GET /rounds/latest` for a pending or latest worker deal. Starts use the original `Idempotency-Key` and exact stakes. New wagers retain the R0.25 increment and R100 per-spot limit. Saved V1/V2 wagers are read and retried at their recorded whole-cent values, including values outside current admission increments. V1 records have no invented pair wagers.

A Firestore transaction freezes a deal and holds the account's shoe before the wallet call. The signed wallet request binds the operation, outcome digest, total wager and return. Confirmation advances the shoe and writes one immutable receipt, round event, analytics settlement and private audit. A lost wallet reply leaves the same pending deal; retrying its key or reading that round completes it. A decline releases the shoe without consuming cards. Later calls verify stored wallet receipts with `GameWalletReceiptVerifier`.

Hosted worker configuration requires `GameWorker:Mode=hosted`, `GameWorker:SigningKey`, Firestore settings, `GameWallet:Url`, and `GameWallet:SigningKey`. `Features:BaccaratEnabled` gates fresh admission; existing operation recovery and history remain readable when new starts are paused. The shared gateway must activate `GameWorkers:baccarat:ForwardPublicRoutes` and use this packaged worker. Existing process-local practice UI remains a separate QA adapter using the package's `PracticeBaccaratStore`.

The package preserves `baccaratRounds`, `baccaratShoes` and historical round IDs. Shared integration must replace the former host game/store/auditor source with this package and update its project references, registry and paid forwarding. The compute-only module is not the paid lifecycle authority.

The client preserves the immutable settled receipt for the result and history. After accepting or restoring a receipt, it refreshes current wallet status before enabling another wager; the receipt's historical balance never replaces current funds or affordability checks.

## Verification status

Focused package tests cover wallet-response loss, independent replicas, concurrent retries, exact declines, private history, corrupt outcome rejection and historical odd-cent wagers. Existing Punto Banco primitive, tableau, dealer and paytable tests are linked into this package suite, including Player/Banker tie pushes, Tie returns and 5% Banker commission. All 89 package cases passed locally, including the actual worker-module signed HTTP routes with an injected wallet. The selected standalone Release worker built with zero warnings/errors. The shared owner's signed MVC gateway test passed against the real platform wallet, including a committed lost reply, paused retry, exact ledger cents and private history. The protected financial recheck and configuration activation remain pending; nothing has been deployed.

```powershell
dotnet test games/Baccarat/tests/FortuneForge.Games.Baccarat.Tests/FortuneForge.Games.Baccarat.Tests.csproj -m:1 -nr:false /p:UseSharedCompilation=false
```

Emulator tests require a loopback `FIRESTORE_EMULATOR_HOST`; the local checkout defaults to `127.0.0.1:8085`. They use isolated `demo-*` projects.

Local client verification: 27 focused tests passed, including current-funds refresh after an old recovered receipt. The final TypeScript check, Vite library build and declaration build passed after that change. Backend, selected worker and signed public wallet checks passed; the protected financial recheck remains open.
