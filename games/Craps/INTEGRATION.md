# Craps backend integration

`FortuneForge.Games.Craps.Server` owns Pass Line admission, come-out/point lifecycle, locked principal, adjustable odds and next-roll side wagers, outcomes, recovery, accepted action history and independent replay. The account platform supplies authenticated forwarding and generic signed wallet operations.

All `/api/games/craps` URLs and request operation IDs remain. The package saves each proposed action before calling the wallet. A per-account pending pointer prevents another action overtaking an unconfirmed one. The saved transition contains the exact dice, wager/refund delta, expected revision and roll number. Signed receipt confirmation applies that transition once and records the original response. A lost response is recovered by replaying the original operation or reading the active/specific round. Declined transitions release the pending pointer without changing the committed round.

Opening wagers debit before any dice reveal. Odds and side-bet increases debit only their incremental amount; replacement/removal refunds their difference once. Rolls credit newly resolved side bets immediately and pay locked Pass Line/odds when terminal. Settled requests are checked with the shared wallet receipt verifier. The package never writes `userBalances` or `balanceTransactions` directly.

Legacy V1/V2 rounds and operation receipts remain readable. Only new continuation actions receive new identities; historical side wagers and journal entries are not rewritten. The package-owned `PaidCrapsReplayAudit` independently replays the accepted dice/wager stream and reconciles mixed historical ledgers and new signed wallet receipts. Prepared transitions have separate private journal instances so they do not alter the historical accepted action stream.

Hosted configuration requires `GameWorker:Mode=hosted`, `GameWorker:SigningKey`, Firestore settings, `GameWallet:Url`, and `GameWallet:SigningKey`. `Features:CrapsEnabled` gates fresh openings; existing paid rounds and exact retries can finish while new starts are paused. The shared gateway must activate `GameWorkers:craps:ForwardPublicRoutes`. Shared host DI must retire the direct-wallet constructor and the old host auditor source in favor of this package. Existing paid browser command recovery remains per account and retains the original identity through expired authentication or changed sessions.

## Verification status

The package suite passed 20/20 tests with zero skipped, and the selected standalone Release worker built with zero warnings and errors. Focused package tests cover lost opening and roll replies, replica recovery, point hits, immediate side payouts, odds cancellation/refunds/replacement, concurrent requests, declines, legacy saved rounds and historical nullable-wire receipts, orphaned old ledger/action witnesses, malformed saved state, and mixed-source financial replay. Existing Craps engine tests cover come-out naturals/craps, point hits, no-decision rolls and seven-outs. The shared owner's signed MVC gateway test passed with the real platform wallet, a committed opening lost reply, saved point dice, immediate side payout, odds refund, exact historical operation replay and five exact wallet ledger legs while new starts were paused. Protected replay/financial rechecks remain open; no production deployment has occurred.

```powershell
dotnet test games/Craps/tests/FortuneForge.Games.Craps.Tests/FortuneForge.Games.Craps.Tests.csproj -m:1 -nr:false /p:UseSharedCompilation=false
```

Local client verification: 119 focused tests, TypeScript checking, Vite library build and declaration build passed. Signed public gateway and wallet checks passed; protected financial replay and deployment checks remain pending.
