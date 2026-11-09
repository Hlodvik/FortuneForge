# Neon Nights

Neon Nights owns its backend game rules and its complete browser client. The
Fortune Forge host composes the module with shared authentication and wallet
services; it does not supply Neon's gameplay implementation.

## Source ownership

- `src/FortuneForge.Games.NeonNights/`: reel generation, matching, payouts,
  definitions, feature state, cascades, and replay policies.
- `client/FortuneForge.Games.NeonNights.Client/`: game controller, API contract,
  presentation, approved symbol artwork, music, effects, and intro videos.
- `tests/`: independent engine checks. The client has its own focused tests.
- `../shared/FortuneForge.Games.Slots.Contracts/`: game-neutral data contracts.
- `../../packages/slot-client-core/`: browser animation, controls, dialogs,
  image rendering, audio transport, and other neutral presentation primitives.
- `../../packages/client-presentation/`: reusable drawing primitives, including
  the settled-payline renderer. Neon owns its appearance settings.

The host's Neon adapter maps package outcomes to the existing HTTP and audit
envelopes and performs account transactions. The browser adapter passes the
account summary, authenticated fetch, and application shell into the game.
Neither game package references host source or another game's package.

## Independent builds

From the repository root, using the installed workspace dependencies:

```powershell
dotnet build games/NeonNights/src/FortuneForge.Games.NeonNights/FortuneForge.Games.NeonNights.csproj --configuration Release
dotnet test games/NeonNights/tests/FortuneForge.Games.NeonNights.Tests/FortuneForge.Games.NeonNights.Tests.csproj --configuration Release
npm run build --workspace @fortuneforge/client-presentation
npm run build --workspace @fortuneforge/slot-client-core
npm run check --workspace @fortuneforge/games-neon-nights
npm run build --workspace @fortuneforge/games-neon-nights
npm run test --workspace @fortuneforge/games-neon-nights
```

These commands do not build the other games or require a reference to the
Fortune Forge application project. Running through the application still uses
the existing `/slots/neon-nights` and `/slots/neon-nights/demo` routes and shared
account services.

## Loading and isolation

The host dynamically imports the Neon client when its route opens. Catalogue
data remains separate from its game controller and media. The normal Vite
production build checks the actual emitted module graph: Neon must not load
host game logic or a foreign game, and the lobby and other games must not
statically load Neon.

`npm run verify:neon-standalone` checks package source dependencies.
`npm run verify:neon-bundle` runs the production build and its loading check.
The existing source-ownership CI gate also enforces the Neon source boundary.

Game-specific changes belong here. Shared helpers accept explicit data and
options; they must not switch gameplay according to a game ID. Introducing a
new game rule into a neutral helper requires a new explicit strategy or a
game-owned implementation rather than another game's conditional branch.
