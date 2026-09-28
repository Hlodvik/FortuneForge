# Shared bot directory

`BotDirectory` seeds the first stable application-managed player profiles. The identity
factory only proposes human-style identities; the generator resolves collisions and asks
the profile repository to create the same durable `users`, username-key, balance, and
statistics records used by player-created profiles. These profiles cannot sign in;
their server-controlled authentication provider is private operational metadata and is
never included in a table or public profile projection.

The queuer owns only availability policy: it reuses a compatible profile before asking the
generator for another one. A separate assignment store owns cross-game leases, heartbeats,
and releases, which prevents one profile from appearing in two games at once. The profile
repository owns profile persistence and lookup. Game adapters pass only legal game state to
each game's agent. Managed balances remain zero and never enter real payment or payout
flows; virtual game results are nevertheless written to the normal `cardGameResults`
history.

When adding a game, include its stable game ID in the appropriate `SupportedGames`
lists, then add that game's adapter and legal-action bot agent. Keep public rendering
and any product disclosure policy outside this server-side identity component.
