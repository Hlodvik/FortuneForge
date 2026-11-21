# Sic Bo paid backend

This package owns paid-round admission, secure outcome generation, accepted rules, immutable
results, history and interrupted-operation recovery. The shared account API owns generic account
reads and wallet balance/ledger mutations. The game does not write `userBalances` or
`balanceTransactions`.

The public contract retains `GET /api/games/sic-bo/status`, `POST /api/games/sic-bo/rounds` and
`GET /api/games/sic-bo/rounds/{roundId}`. POST uses the original `Idempotency-Key` and exact
payload for every retry; changing the payload for an existing key conflicts. Requests identify
the player through the signed worker account resolver. Browser gateways also send
`X-FortuneForge-Expected-Account` to bind recovery to the account that opened the game.
The prior internal compute resolve URL remains available as a compatibility adapter to the pure
package engine; the paid public lifecycle runs through this worker.

## Persistence and wallet recovery

The game first persists its immutable accepted input and private generated outcome in
`sicBoRounds`, including an outcome digest and the original authenticated request observation.
It then submits one game/account-scoped signed `Direct` wallet settlement containing the wager
and payout. The final game transaction saves the verified wallet receipt and exact returned
balance, publishes shared audit/security/participation records, and writes `sicBoRoundEvents`.
The shared wallet's idempotent operation prevents another charge after a lost response or restart.
A GET can finish an interrupted prepared round. Concurrent retries return the original result.
Declined wallet receipts remain declined after a top-up; no success history is created for them.
Stored balances and receipts are validated again on replay.

Schema-1 saved bets, dice and larger historical stakes remain readable. Only genuinely new
bets use the current R10 per-spot cap.
Historical rounds without the new wallet-protocol marker were already paid by the original
atomic backend and never go through settlement again. Their outcomes remain inspectable through
the same round URL.

## Configuration and release

The worker registers `AddGameWalletClient("sic-bo")` in its game-specific worker setup. Configure
`GameWorker:Mode=hosted`, the worker signing key and hosted Firestore; configure `GameWallet:Url`
and `GameWallet:SigningKey` for the generic account API. The account API requires matching
`GameWallet:Games:sic-bo:SigningKey` and `AllowAccountBalanceRead=true`.
This single-round game uses `Direct` settlements; wager reservations, legacy free-game spending
and legacy-slot ledger layout are not required.

The gateway uses `GameWorkers:sic-bo:ForwardPublicRoutes` with the packaged worker URL/key.
`Features:SicBoEnabled` controls new wagers inside the worker. Keep the forwarding owner enabled
when pausing new wagers so exact retries and round recovery continue on the original backend.
An unavailable worker must not fall back to another engine after an ambiguous paid request.

Local source completion and test evidence do not imply production deployment. This change does
not deploy a worker/client/API or enable a live wager flag. Latest observed local checks and
remaining shared integration work are recorded in
`artifacts/backend-completion/table-number-subhandoff.json`.
