# Flappy backend

This package owns both recorded free runs and paid daily/weekly competition runs. Hidden catalogue visibility does not remove the paid backend requirement. The worker owns run IDs, entropy, saved rules, replay validation, persistence, interrupted-operation recovery and terminal evidence. Shared libraries supply generic competition accounting/standings/settlement and authenticated wallet RPCs.

Existing public URLs are preserved:

- `/api/arcade-competitions/flappy/free/runs` and `/<runId>/replay`.
- `/api/arcade-competitions/flappy/<daily|weekly>/attempts`.
- `/api/arcade-competitions/flappy/<daily|weekly>/runs/<runId>/replay`.
- `/api/arcade-competitions/flappy/<daily|weekly>/runs/<runId>` for authenticated recovery/history.
- `/api/arcade-competitions/flappy/<period>` for shared standings.

Paid starts prepare a durable run before the signed wallet debit. Lost wallet replies recover the same operation and seed. Legacy paid rows require verification of their original debit instead of another charge. New browser intents freeze expected window bounds in the body; retries preserve those bytes and the original key. `?recoverOnly=true` recovers an existing operation without admitting an unknown intent into a new window. The verified original body digest is bound to the paid request. A completed replay remains recoverable after cutoff; a new score still follows the original competition cutoff.

Local verification uses `FIRESTORE_EMULATOR_HOST=127.0.0.1:8085` and a distinct `demo-*` project. Run `dotnet test games/Flappy/tests/FortuneForge.Games.Flappy.Tests/FortuneForge.Games.Flappy.Tests.csproj -c Release`, then build the shared packaged worker with `-p:GamePackage=Flappy`. The focused suites cover real Firestore state, preserved HTTP contracts, signed-wallet boundaries, lost debit/refund responses, exact retries, rollover and historical data.

Production deployment and activation remain separate. Configure `GameWallet:Url` and `GameWallet:SigningKey` and the normal worker identity/Firestore settings. The platform gates free and paid forwarding independently with `GameWorkers:flappy:ForwardFreeRoutes` and `GameWorkers:flappy:ForwardPaidCompetitionRoutes`. No deployment or forwarding flag is changed by this package manifest.
