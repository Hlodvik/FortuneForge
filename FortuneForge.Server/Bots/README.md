# Shared bot directory

`BotDirectory` owns stable application-managed bot profiles: a durable identifier,
display name, skill level, and the games the profile can join. The first roster is
configured in `appsettings.json` under `Bots:Directory` and contains eight profiles.

The queue scheduler does not select profiles or inspect skills; it only decides how
many automated seats may be requested. Game adapters select profiles from this
directory and pass each game's legal state to that game's own bot agent. The directory
does not create an authenticated user, persist a wallet, or enter payment flows.

When adding a game, include its stable game ID in the appropriate `SupportedGames`
lists, then add that game's adapter and legal-action bot agent. Keep public rendering
and any product disclosure policy outside this server-side identity component.
