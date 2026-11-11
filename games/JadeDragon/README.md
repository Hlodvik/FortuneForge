# Jade Dragon: The Golden Pearl

Jade Dragon is a complete standalone slot module. The former Empty Slots scaffold
has been absorbed into this game and no longer exists as a separate route, catalogue
entry, package, or server identity.

The browser presentation lives in `client/jade-dragon` and is built independently
before the Fortune Forge host. The authenticated route is `/slots/jade-dragon`; it
keeps the shared navbar and embeds the presentation through a narrow same-origin
message bridge. The existing Jade Dragon catalogue card remains host-owned lobby art.

Symbol protocol values remain anonymous IDs `S01` through `S23`. Presentation names,
models, graphics, audio, controls, and animations are owned by the Jade client.
`SYMBOLS.md` explains each backend role.

The backend owns its rules, reels, matching, payout calculation, boosts, scatter
awards, collections, features, and pity. The only shared dependency contains data
contracts and a random-source interface. Firebase authentication, Firestore,
wallets and deployment remain with the existing application.

The .NET 10 backend project is
`games/JadeDragon/src/FortuneForge.Games.JadeDragon/FortuneForge.Games.JadeDragon.csproj`.
Its host adapter is `FortuneForge.Server/Slots/Games/JadeDragon`.

Canonical identifiers are `jade-dragon`, `empty-slots-v1`, and
`empty-slots-engine-v1`. The legacy `/slots/empty-slots-game` URL only redirects to
the canonical route; it is not another game.
