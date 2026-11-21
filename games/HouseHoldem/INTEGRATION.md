House Hold'em's authoritative paid backend lives in `server/FortuneForge.Games.HouseHoldem.Server`. It owns the rules adapter, round lifecycle, legacy codec, pending commands, immutable responses, history and protected completion evidence. The host controller forwards or fails closed; it does not retain a second wallet lifecycle.

Admission and actions freeze their outcome before calling the generic signed wallet. The wallet enforces the historical admission balance requirement atomically: the initial wager plus one additional ante must be available, while only the initial wager is charged. A confirmed immutable receipt permits final publication. Lost replies recover from the same frozen command before later play. Legacy stored decks/action steps and historical ante sizes remain recoverable.

Completion proof freezes actual signed receipts and selected original ledger evidence inside the protected round capture. Mixed legacy/new funding is identified explicitly. Missing original funding evidence is marked unavailable; it is never reconstructed as a new charge. Later changes to the original ledger do not change the protected proof.

Worker configuration requires the ordinary signed wallet URL/signing key and platform permissions for the game's account balance read and settlement. Preserve forwarding for `/api/games/house-holdem` when new admission is paused, so reads and pending settlement recovery remain reachable. Do not register a second unkeyed host wallet client for this package.

Local sign-off on 2026-11-21: all 10 package tests passed with zero skips against the localhost Firestore emulator. The standalone packaged worker built with zero warnings and errors. Test evidence: `artifacts/test-results/house-final-proof/house-final-proof.trx`. This is a source and local verification sign-off; deployment and shared host rollout are coordinated separately.

Validation commands:

```powershell
$env:FIRESTORE_EMULATOR_HOST='127.0.0.1:8791'
dotnet test games/HouseHoldem/tests/FortuneForge.Games.HouseHoldem.Tests/FortuneForge.Games.HouseHoldem.Tests.csproj -m:1 -p:BuildInParallel=false
dotnet build games/shared/FortuneForge.Games.PackagedGame.Server/FortuneForge.Games.PackagedGame.Server.csproj -p:GamePackage=HouseHoldem -m:1 -p:BuildInParallel=false
```
