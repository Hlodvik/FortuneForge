# Empty Slots Game

A clean backend foundation for Tian's slot game. There are no game graphics,
named symbols, audio, fonts, slot controls, animations, or renderer. The optional
page shell contains the existing shared navbar and an empty surface.

Symbol values are anonymous IDs `S01` through `S23`. They have backend roles,
not visual names. Tian can assign every name, graphic and animation in his new
client. Start with `AGENTS.md`; `SYMBOLS.md` explains each ID's rules.

The backend owns its rules, reels, matching, payout calculation, boosts, scatter
awards, collections, features, and pity. The only shared dependency contains data
contracts and a random-source interface. Firebase authentication, Firestore,
wallets and deployment remain with the existing application.

This game stays in the FortuneForge repository. Tian's agents should start with
`games/EmptySlots/AGENTS.md`. The .NET 10 backend project is
`games/EmptySlots/src/FortuneForge.Games.EmptySlots/FortuneForge.Games.EmptySlots.csproj`.
Its host adapter is `FortuneForge.Server/Slots/Games/EmptySlots`.

The blank page is `fortuneforge.client/src/app/routes/EmptySlotsGameRoute.tsx`,
available at `/slots/empty-slots-game`. It keeps the shared navbar and has no
game renderer, symbol display, controls, artwork or sound.
