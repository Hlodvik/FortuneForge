# Managed game profiles

The identity factory proposes human-style identities. The generator resolves collisions
and asks the profile repository to create the same durable `users`, username-key, balance,
and statistics records used by player-created profiles. Each generated profile carries the
internal `bot` profile tag and the managed authentication provider. These profiles cannot
sign in; their server-controlled authentication provider is private operational metadata
and is never included in a table or public profile projection.

The queuer owns only availability policy: it queries only `bot`-tagged profiles and reuses a
compatible available profile before asking the generator for another one. A separate
assignment store owns cross-game leases, heartbeats,
and releases, which prevents one profile from appearing in two games at once. The profile
repository owns profile persistence and lookup. The repository and assignment store both
verify the tag and managed provider before writing, so a normal user profile cannot be
leased or updated through this system. Game adapters pass only legal game state to
each game's agent. Managed balances remain zero and never enter real payment or payout
flows; virtual game results are nevertheless written to the normal `cardGameResults`
history.

When adding a game, request that game's stable ID from the queuer and use the returned
profiles only through a server-owned game adapter. Keep public rendering and any product
disclosure policy outside this server-side identity component.
