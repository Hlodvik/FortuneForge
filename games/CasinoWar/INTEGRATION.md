# Casino War backend

`FortuneForge.Games.CasinoWar.Server` owns the paid public API and persistence.
The host forwards `/api/casino-war`; its paid fallback returns 503 and practice
remains available locally. New rounds require `CasinoWar:AllowNewPaidRounds=true`.
The package retains the original `casinoWarRounds`, `casinoWarRoundEvents`, deck
codec, round IDs, event IDs, and history. `GET /rounds/pending` recovers uncertain
actions and discovers old open rounds; `GET /rounds/history` reads both legacy and
package rounds. History requires the existing `userId, createdAt DESC` round index.

Opening and decision actions are frozen in `casinoWarPreparedActions` before any
signed wallet operation. A per-account pending pointer blocks conflicting actions
until a confirmed receipt is saved. Each action retains its own immutable response,
so replaying an opening after a later decision returns the original opening.
A declined war action releases its pointer and preserves its declined key, allowing
a new surrender key without charging the original stake again.

The opening wallet action includes the original stake, optional tie-side stake,
and any opening returns. A later decision charges only the additional war stake
and pays only its primary return. Before that decision's wallet call, the package
freezes the original opening's verified receipt or historical legacy ledger terms.
Completion attaches the common protected `financialProof` with owner
`casino-war-package-v1` and contract `casino-war-protected-wallet-evidence-v1`.
The proof preserves both actions and the original tie-side return even if live
rows change after preparation. Missing retained legacy ledger terms are explicitly
unavailable and never cause the old stake or side award to be applied again.

Verification uses the local Firestore emulator and serialized .NET builds:

```powershell
$env:FIRESTORE_EMULATOR_HOST='127.0.0.1:8791'
dotnet test games/CasinoWar/tests/FortuneForge.Games.CasinoWar.Tests/FortuneForge.Games.CasinoWar.Tests.csproj --nologo --verbosity minimal -m:1 -p:BuildInParallel=false -nr:false
dotnet build games/shared/FortuneForge.Games.PackagedGame.Server/FortuneForge.Games.PackagedGame.Server.csproj -p:GamePackage=CasinoWar --nologo --verbosity minimal -m:1 -p:BuildInParallel=false -nr:false
```
