# Player game experience

`GET /api/games/experience` is one authenticated, non-cacheable batch for all 45
canonical games, including hidden games. Each row contains `hasPlayed: true`,
`hasPlayed: false`, or `hasPlayed: null` with an explicit knowledge state and provenance.
Opening a route, an issued free course, a queue entry, or a failed command does not
establish play. No tutorial or gameplay behavior is enabled by this API itself.

The host loads this batch alongside shared route loading. Game packages can consume
`@fortuneforge/client-presentation/game-experience`. Account changes clear the cache;
acknowledged mutations invalidate negative proofs and refresh the shared batch.

## Positive evidence

Accepted production state/financial commits merge a monotonic witness into
`catalogPlayerActivity/{SHA256(userId)}/games/{canonicalGameId}`. The shared pure
`AcceptedGameParticipationDocuments` builder uses capture version
`canonical-game-commit-v1`; callers determine real participation from authoritative
state. Existing recency, provenance and audit payloads are preserved. The observed
timestamp is not a claimed first-play date.

The acknowledged security-event sink covers in-memory practice engines in its existing
commit. Transaction-backed engines use the same commit that accepts play. Positive
legacy history can also be backfilled in bounded pages. Backfill progress and discovered
positive witnesses commit together. Incomplete history never establishes a negative.

## New-account negative evidence

Fresh account creation writes an explicit `gameExperienceOrigin` inside the same
`users/{userId}` document creation transaction. It contains a nonce, origin contract,
capture version, existing `K_REVISION` / `FF_RELEASE_SHA` identity and a server timestamp.
Existing users are not retrofitted. An account creation date alone is insufficient.

Negative coverage requires an operator-verified continuous capture epoch:

- Pointer: `catalogGameExperienceCoverage/canonical-capture-v1`.
- Permanent epoch: `catalogGameExperienceCoverageEpochs/{epochId}`.
- Both documents use schema version 1, an identical epoch ID and active status.
- The epoch records `sourceVersion`, `sourceProofSha256`, `captureVersion`,
  `originContract`, `continuousFromUtc`, `verifiedAtUtc`, and `validUntilUtc`.
- `canonicalGameIds` must exactly match the deployed 45-game catalogue.
- `servingApiRevisions` lists every serving revision. `writerRevisions` retains all
  writers in the continuous epoch, with exact revision, commit, origin contract and
  per-game `gameCaptureVersions`. The epoch also has a per-game version map.
- A game qualifies only when every recorded writer and every serving revision has
  the verified capture version for that game. The registering revision and source
  commit must match the explicit account origin.
- The origin's server timestamp must fall within the continuous epoch. Verification
  must precede the fresh evidence read; expiry is at most 24 hours after verification.

Use the deployed `ContinuousGameExperienceCoverage` constants as the source contract.
Do not activate the pointer merely because a build passed. First verify actual serving
traffic, every write path, source identities, and emulator evidence for the exact
candidate. Publish the permanent epoch before its pointer. No activation is performed
by application startup or deployment of this reader.

An incompatible writer or capture gap ends that epoch. Preserve its retirement, gap
or revocation marker permanently; never remove the marker or reuse its ID. A later
compatible rollout starts a new epoch. Earlier origins remain unknown unless other
authoritative evidence establishes their state.

## Slot safeguards and failures

Slot negatives additionally require a fresh complete read proving absence of every
permanent canonical/legacy admission guard. A present, pending, failed or malformed
guard leaves the game unknown unless independent positive evidence proves play.
Wukong includes the historical `classic-demo-v1` alias.

The optional independent lifetime-slot proof uses
`catalogGameExperienceCoverage/slot-guards-v1`; its historical source/revision
attestation is separate from the new-account epoch. See
`AuthoritativeGameExperienceCoverage` for its exact contract.

Each returned complete proof must have a newly successful durable account receipt.
An older read cannot claim a receipt superseded by a newer one. Expired proofs,
missing origins, legacy coverage gaps, unavailable reads and failed metadata commits
produce unknown values. Positive evidence takes precedence and is never overwritten
with a stored false flag.
