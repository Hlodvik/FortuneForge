# Snake backend

Snake is account-scoped free play with no wager, debit, settlement or cash prize. Its package owns hosted board admission, server-generated seeds, steering, movement, scoring, reset, interrupted-run recovery and deterministic private replay.

Existing `/api/games/snake` URLs and `snakeGames` document IDs, schema and `stateJson` are preserved. Both the legacy authenticated host facade and the packaged worker use the same authoritative backend implementation. A reset archives an unfinished run and starts a new private run on the same game ID. Completed evidence is immutable and written with the terminal transition. Snake retains its existing free-play reporting scope; it does not enter the paid-catalogue analytics outbox.

The worker authenticates requests through the shared signed game-worker runtime. Exact request responses live in `snakeRequestReceipts`; retries do not tick or reset a second time. The client uses the shared account-scoped recovery journal and restores a pending exact command before reading the latest board.

Build the selected packaged worker with `-p:GamePackage=Snake`. Run package tests against a Firestore emulator using `FIRESTORE_EMULATOR_HOST`. The implementation does not deploy or enable the hidden catalogue entry in production.
