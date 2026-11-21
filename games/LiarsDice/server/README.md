# Liar's Dice backend

This is account-scoped free play against three managed opponents. There is no wager, wallet debit or payout. The worker owns admission, private hands, turn scheduling, complete round history, accepted inputs, completion and deterministic replay.

The public URLs remain under `/api/games/liars-dice`: status; matches; match reads; bid; challenge; spot-on; advance; next-round. New durable match IDs are version 7 GUIDs. Earlier version 4 memory-only matches retain the package-owned legacy service through the host compatibility adapter. Historical v2 journal evidence keeps its independent replay implementation.

Durable state is in `liarsDiceMatches`, exact authenticated responses in `liarsDiceRequestReceipts`, and immutable evidence in the existing shared audit collections. Every accepted transition, response receipt, participation record and final analytics outbox entry commits with the private state. Match responses contain only the player's cup and public outcomes.

Build the selected packaged worker with `-p:GamePackage=LiarsDice`. The shared worker uses its existing Firestore and signed game-worker configuration. Managed opponents use the shared signed roster client configured with `ManagedPlayerRoster:Url` and `ManagedPlayerRoster:SigningKey`; no bot profiles are synthesized inside the worker. Host forwarding must preserve the existing `Features:LiarsDiceEnabled` gate and affinity for durable match IDs.

New matches require the durable worker. Configure its URL, signing key and roster before releasing the gateway. The client binds the shared game command recovery journal to the authenticated account. After a lost response or remount it replays the saved exact path, body and idempotency key for durable v7 matches, then reads the latest match before accepting another mutation. Known legacy v4 mutations are reconciled through a validated GET without resubmission. A definite 404 expires the saved command. A legacy memory-only start whose response and ID were never received cannot be restored retroactively.

Run the package tests against a Firestore emulator using `FIRESTORE_EMULATOR_HOST`. This checkout contains implementation and verification work; it does not represent a production deployment.
