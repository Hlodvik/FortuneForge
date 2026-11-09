# Fortune Forge game packages

This directory is the authoritative source for Fortune Forge game engines and reusable game clients.
It is part of the main Fortune Forge repository so an application commit, its game rules, and its user
interface packages always move together.

## Layout

- `src/` contains shared .NET primitives used by multiple games.
- `games/<Game>/src/` contains independently testable and packable .NET game engines.
- `games/<Game>/client/` contains npm workspace packages used by the web application.
- `games/<Game>/samples/` contains local sample hosts where one exists.
- `tests/` contains the shared .NET game-package test suite.
- `catalog/` contains package descriptors and compatibility metadata.

The projects remain packages, but no external sibling checkout is part of the build. NuGet and npm
archives are generated artifacts and are not committed as source dependencies.

## Neon Nights ownership

Neon Nights is the reference for a complete game module under `games/NeonNights/`.
Its engine owns reel generation policies, symbol matching, payouts, feature triggers,
cascades, and versioned feature state. Its client owns its controller, presentation,
art, audio, and video. The host supplies authentication, wallet transactions, and
an authenticated HTTP transport through explicit adapters.

Game packages may depend on neutral contracts and primitives in `games/shared/`
and `packages/`. They must not import the host application or another game. A
shared helper must not choose game behavior by a game ID. The host may select a
registered game module, then delegate the complete game operation to that module.

The Neon client is loaded through a dynamic route entry. Catalogue metadata does
not import its controller or media. `npm run verify:neon-standalone` checks source
boundaries, and `npm run verify:neon-bundle` checks the production chunk graph.
These checks complement each package's own build and behavioral tests.

## Verification

From the repository root:

```powershell
dotnet test games/FortuneForge.Games.slnx --configuration Release
npm ci
npm run games:check
npm run games:test
npm run games:build
```

## Empty Slots Game

The in-repository backend starter is in [EmptySlots/](EmptySlots/). Its engine uses neutral
`Snn` symbol identifiers and owns its game rules. The host supplies authentication,
balances, and persistence. Start with [EmptySlots/AGENTS.md](EmptySlots/AGENTS.md) for
the instructions for continuing work on this module.
