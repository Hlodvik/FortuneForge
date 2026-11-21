Texas Hold'em's authoritative backend lives in `server/FortuneForge.Games.TexasHoldem.Server`. The package owns paid queue admission, buy-in escrow, legacy per-hand wagering, tables, deadlines, managed seat decisions, persistence, history, and financial/replay evidence. The host controller is a forwarding/fail-closed adapter. Legacy namespaces, collections, state codecs, escrow tickets and existing hand commitments remain readable.

Every mutation freezes its game state, ordered financial journal, original read versions, response and audit intents under durable game locks. One signed atomic wallet batch applies all net account effects; minimum prefix balances preserve the original ordering of payout and new blind admission. Declines publish no game state and remain immutable for the original request key. Pending batches recover before reads, later commands or worker progression can publish competing state. Remote roster releases use a durable game outbox.

Completed rounds freeze actual batch requests/receipts and exact historical ledger evidence in protected round captures. Mixed old/new funding remains explicit. Missing historical evidence is recorded as unavailable, without rebuilding or charging the original stake. Protected captures remain independently readable when the source journal changes.

Worker configuration requires `GameWallet:Url`, `GameWallet:SigningKey`, `GameWallet:BatchSigningKey`, and the managed roster URL/signing key. Platform game configuration must enable multi-account batches, balance reads and trusted player-name reads for `texas-holdem`. Preserve `Cards:CreditTexasHoldem:AllowSingleHumanBotFill` from the host configuration. Forward all existing routes, including DELETE, even while `Features:CreditTexasHoldemEnabled` pauses new admission. Recovery by account requires a Firestore index on prepared mutation `status` and array field `accountIds`.

Local sign-off on 2026-11-21: all 119 package tests passed with zero skips against the localhost Firestore emulator. The standalone packaged worker built with zero warnings and errors. Test evidence: `artifacts/test-results/texas-authoritative-final/texas-authoritative-final.trx`. This is a source and local verification sign-off; deployment and shared host rollout are coordinated separately.

Validation commands:

```powershell
$env:FIRESTORE_EMULATOR_HOST='127.0.0.1:8791'
dotnet test games/TexasHoldem/tests/FortuneForge.Games.TexasHoldem.Tests/FortuneForge.Games.TexasHoldem.Tests.csproj -m:1 -p:BuildInParallel=false
dotnet build games/shared/FortuneForge.Games.PackagedGame.Server/FortuneForge.Games.PackagedGame.Server.csproj -p:GamePackage=TexasHoldem -m:1 -p:BuildInParallel=false
```
