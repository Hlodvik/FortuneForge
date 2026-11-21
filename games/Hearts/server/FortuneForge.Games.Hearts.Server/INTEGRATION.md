# Hearts authoritative backend integration

The package keeps the existing Hearts namespaces and pure game policy. Free play,
learning trials, onboarding and paid tables belong to the Hearts worker. The old
compute URLs remain available for compatibility. The host now retains forwarding-only routes and an authentication/503 fallback.
Local package verification is complete; this document is not deployment proof.

## Free and trial persistence

FirestoreHeartsFreeStore loads a transaction-local HeartsGameService. Commands read
only their private match, or the account's active trial pointer. The account gate
serializes conflicting commands across replicas. Starts, state, pass, card, advance,
next round, leave, onboarding and trial cancellation keep the existing response shapes.

Private snapshots in heartsFreeSessions retain all engine cards, actor decision and
timing seeds/counters, scheduled actions, lease/expiry state, event versions and
trial/analytics completion flags. heartsFreeAccounts owns the transaction gate and
active trial pointer; heartsFreeCommands owns exact authenticated command responses.
heartsAccountOnboarding remains the only onboarding owner.

The existing policy writes to buffered ports. The final transaction commits the
private state, observations, first immutable completion, analytics outbox, onboarding
and exact response together. A lost response replays before any new private outcome
is generated. Background workers load bounded due session IDs and use the same gate.
A failed account does not stop other due accounts. Labelled house actors have no
platform wallet, profile reservation or lease mutation.

Features:HeartsEnabled controls new free/trial admissions. Existing private boards,
exact retries, background progress and onboarding remain available while paused.
The shared managed-admission policy controls new labelled house cohorts; existing
cohorts continue. Trial onboarding is committed only after all thirteen real tricks.
Leaving or expiring a trial cannot satisfy onboarding.

## Public integration

Use AddHeartsFreeBackend and AddHeartsPaidBackend in the worker. MapHeartsFree and
MapHeartsPaid preserve /api/games/hearts. WithHeartsWorkerRoutes attaches forwarding
metadata to the host's MVC routes; the host forwards or fails closed after cutover.
The host's local Hearts store/service/sweeper registrations have been removed.
Production shared configuration remains Arcane's ownership. Paid wallet caps,
legacy funding and roster cleanup details are in [Paid/README.md](Paid/README.md).

Browser recovery uses the shared account-scoped command journal. It retains the
exact path/body/key and sends the expected account. The host remounts each account's
game session so stale callbacks cannot update a different account's balance or guard.

## Verification boundary

Six new emulator scenarios cover exact retry after restart, concurrent trial starts,
private random-state preservation, concurrent scheduled turns, atomic completed
trial/onboarding/evidence/outbox, admission pause/expiry, and poison-account isolation.
Existing game-policy, trial, labelled-house, lifecycle and onboarding tests are
migrated into the package. Local verification passed 106 unique backend cases,
145 client cases (including 46 gateway cases), twelve browser host-route tests,
TypeScript, production bundle/declarations, and the standalone worker build with
zero warnings/errors. The actual health-only worker returns health200 and API503
without database configuration. See [the combined test evidence](../../../../artifacts/backend-completion/hearts-local-verification.json).

The shared command journal preserves both explicit caller keys and an existing
pending key. Signed public gateway/wallet/replica checks and independent
financial-auditor integration are recorded by the shared owner before entire-game
sign-off. No production release was activated by the card lane.

Final local integrated sign-off passed: actual signed Hearts/card cutover 38/38
and final catalogue authority 238/238, with zero skipped. The shared verification
record confirms all 40 main and five hidden games. Hearts is complete in checkout;
production worker configuration and deployment remain separate.
