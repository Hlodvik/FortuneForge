# Empty Slots Game backend

This assembly contains game rules and neutral symbol identifiers. It has no
frontend, display names, artwork, audio, fonts or browser dependencies.
Implementation baseline: FortuneForge commit `ae40278`. Identifier remapping
does not change reel weights, random draw order, payout arithmetic or feature
transitions. Engine version: `empty-slots-engine-v1`.

The assembly owns outcome generation, matching, payout rounding, scatter
frequency, special-point boosts, pattern payouts, collection progression,
feature transformations, pity and deterministic evaluation. Its embedded
`Data/empty-slots.json` is the complete base ruleset. `SymbolMetadata` exposes
symbol IDs, backend roles, payout rules, strip eligibility and track bindings.

The only dependency is `FortuneForge.Games.Slots.Contracts`, containing neutral
data contracts and infrastructure ports. The existing host owns authentication,
wallet admission, transaction persistence, idempotency and entitlement storage.
No identity, database, frontend or deployment code belongs to this package.

Demo and paid collection arithmetic preserve their separate rounding and carry
policies. Legacy meter-balance inputs remain accepted for exact historical
frequency and audit behavior; the current rules disable meter accumulation.
