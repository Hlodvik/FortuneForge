# Competitive Solitaire backend

The `FortuneForge.Games.Solitaire.Server` package owns competitive routes, rules,
queue admission, managed seats, settlement, claims, history, and the recovery and
deadline sweeper. The platform forwards `/api/solitaire` to this worker and returns
503 when it is unavailable. `/api/solitaire/free` remains owned by the separate
FreeSolitaire package. Host tests compose the package through
`FortuneForge.Server.Tests/Solitaire/CompetitiveSolitaireTestStore.cs`; production
must not register the old host store, service, or sweeper.

New entries require `Solitaire:AllowNewPaidRounds=true`. Recovery, claims, and
deadlines continue while new entries are paused. Managed filling retains
`Cards:CompetitiveSolitaire:AllowSingleHumanBotFill`. The worker uses the shared
signed wallet batch and managed-player roster clients. The platform wallet scope
for `solitaire` requires `AllowPlayerNameRead=true`; new admission rejects a missing
trusted name. Exact retries recover their original result without a fresh profile
name read. Generic wallet and roster implementations remain platform owned.

Existing ticket, match, player, house funding, revenue, result, queue, and history
collections and identifiers remain in use. New financial actions are frozen in
`solitairePreparedWalletActions`, with immutable responses in
`solitaireWalletActionReplies`. A global durable gate at
`solitaireWalletShards/legacy-cohorts-v1` is read by every game-domain transaction.
It serializes all cohort mutations while a wallet outcome or response is unknown.
This deliberately trades throughput for correct recovery across legacy cohorts;
narrower gates require all legacy mutators to adopt the same partition scheme.
Reads and sweeps recover the original actor's operation before advancing state.
Frozen writes contain validated relative root document paths and the shared
Firestore field codec; no platform balance or ledger writes enter the domain plan.

Completions retain `paid-solitaire-match-v1` private board evidence, add
`payoutDisposition: claimable-entitlement-only`, and attach the shared
`financialProof` envelope with owner `solitaire-package-v1` and contract
`solitaire-protected-wallet-evidence-v1`. Each real entrant has either its cached,
verified batch request and receipt or its original legacy ledger evidence. Missing
retained legacy evidence is marked unavailable; it never triggers another debit.

Claims preserve the original completion. A separate protected `payout-claimed`
event has `state.match`, `state.players`, `state.versionScope`,
`state.completionEvidenceId`, `state.completionEvidenceSha256`,
`state.completionEvidenceAvailable`, and the common `state.financialProof` for the
claim batch. Those terms and the original completion hash are frozen before the
wallet call. Claims compare live entitlement to the protected completion before
paying. Results that predate completion capture retain eligibility, with an
explicit unavailable anchor. New package results require their original capture.
Claim retries reuse the same event and receipt, and requeue the independent audit
without rewriting its immutable completion evidence.

Verification uses the local Firestore emulator and serialized .NET builds:

```powershell
$env:FIRESTORE_EMULATOR_HOST='127.0.0.1:8791'
dotnet test games/Solitaire/tests/FortuneForge.Games.Solitaire.Tests/FortuneForge.Games.Solitaire.Tests.csproj --nologo --verbosity minimal -m:1 -p:BuildInParallel=false -nr:false
dotnet build games/shared/FortuneForge.Games.PackagedGame.Server/FortuneForge.Games.PackagedGame.Server.csproj -p:GamePackage=Solitaire --nologo --verbosity minimal -m:1 -p:BuildInParallel=false -nr:false
```
