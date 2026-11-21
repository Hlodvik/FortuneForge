# Horse Flight backend

This package owns both recorded free runs and paid daily/weekly competition runs. Hidden catalogue visibility does not remove the paid backend requirement. The worker owns run IDs, entropy, saved rules, replay validation, persistence, interrupted-operation recovery and terminal evidence. Shared libraries supply generic competition accounting/standings/settlement and authenticated wallet RPCs.

Existing public URLs are preserved:

- `/api/arcade-competitions/horse-flight/free/runs` and `/<runId>/replay`.
- `/api/arcade-competitions/horse-flight/<daily|weekly>/attempts`.
- `/api/arcade-competitions/horse-flight/<daily|weekly>/runs/<runId>/replay`.
- `/api/arcade-competitions/horse-flight/<daily|weekly>/runs/<runId>` for authenticated recovery/history.
- `/api/arcade-competitions/horse-flight/<period>` for shared standings.
- `/api/games/horse-flight/status`, `/runs`, and `/runs/<runId>/complete` retain the original free API.

Paid starts prepare a durable run before the signed wallet debit. Lost wallet replies recover the same operation and seed. Legacy paid rows require verification of their original debit instead of another charge. New browser intents freeze expected window bounds in the body; retries preserve those bytes and the original key. `?recoverOnly=true` recovers an existing operation without admitting an unknown intent into a new window. The verified original body digest is bound to the paid request. A completed replay remains recoverable after cutoff; a new score still follows the original competition cutoff.

Horse Flight retains versions 1–4, including missing-version legacy rows. Start receipts carry `rulesVersion`; browser recovery uses that version instead of upgrading an accepted run. Fifteen archived version 1 browser replay vectors are independently checked against the C# engine. The worker allows a 192 KiB request body for the supported 30,000-tick/20,000-right-click replay contract.

Local verification uses `FIRESTORE_EMULATOR_HOST=127.0.0.1:8085` and a distinct `demo-*` project. Run `dotnet test games/HorseFlight/tests/FortuneForge.Games.HorseFlight.Tests/FortuneForge.Games.HorseFlight.Tests.csproj -c Release`, then build the shared packaged worker with `-p:GamePackage=HorseFlight`. The focused suites cover real Firestore state, preserved HTTP contracts, signed-wallet boundaries, lost debit/refund responses, exact retries, rollover and historical data.

Production deployment and activation remain separate. Configure `GameWallet:Url` and `GameWallet:SigningKey` and the normal worker identity/Firestore settings. The platform gates free and paid forwarding independently with `GameWorkers:horse-flight:ForwardFreeRoutes` and `GameWorkers:horse-flight:ForwardPaidCompetitionRoutes`. No deployment or forwarding flag is changed by this package manifest.
Horse Flight retains the default-disabled `Features:HorseFlightEnabled` maintenance gate across status, start and completion, including recovery. To enable a selected hosted worker revision, set `Features__HorseFlightEnabled=true` on that revision. Host forwarding and catalogue visibility are independent settings; enabling the worker does not activate forwarding or make the game visible in the catalogue.
