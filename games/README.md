# Fortune Forge game packages

This directory is the authoritative source for Fortune Forge game engines and reusable game clients.
It is part of the main Fortune Forge repository so an application commit, its game rules, and its user
interface packages always move together.

## Layout

- `shared/` contains neutral .NET contracts and primitives used by multiple games.
- `<Game>/src/` contains independently testable and packable .NET game engines.
- `<Game>/client/` contains npm workspace packages used by the web application.
- `<Game>/samples/` contains local sample hosts where one exists.
- `tests/` contains the shared .NET game-package test suite.
- `catalog/` contains package descriptors and compatibility metadata.

The projects remain packages, but no external sibling checkout is part of the build. NuGet and npm
archives are generated artifacts and are not committed as source dependencies.

## Slot package ownership

Every slot has one first-class module under `games/<Game>/`. Its engine owns
frozen definitions, reel generation policy, matching, payouts, feature triggers and
versioned replay. Its client owns the manifest, presentation configuration, CSS and
in-game media. The host supplies authentication, wallet transactions, Firebase
persistence, navigation and authenticated HTTP transport through explicit adapters.

Game packages may depend on neutral contracts and primitives in `games/shared/`
and `packages/`. They must not import the host application or another game. A
shared helper must not choose game behavior by a game ID. The host may select a
registered game module, then delegate the complete game operation to that module.

The authoritative route, server ID and package mapping for all 20 slots is
[`catalog/slot-packages.json`](catalog/slot-packages.json). Slot clients are loaded
through dynamic route entries; lightweight lobby metadata stays in the host so the
lobby does not load every game's runtime or in-game media. `npm run
verify:slot-packages` checks the common source boundary, complemented by package-local
build and behavioral tests.

### Slot package contract

Each .NET package exposes its stable `GameId`, immutable `Profile`, definition
factories (`CreateGame`, `CreateSymbolSet`, `CreateReelSet`, `CreatePaytable`) and a
host-independent `Spin` entry point. A game-specific mechanic belongs beside that
engine and may be surfaced through a narrow package API; the server registry only
adapts package outcomes to wallet, audit and Firebase settlement records.

Each browser package exports its manifest, catalogue record, sound contract and
stylesheet from its own `package.json`. Custom controls or presentation helpers use
narrow subpath exports. Game art and audio live below that package's `src/assets/`;
only lobby thumbnails, the shared navbar and shared shell assets stay in the host.

When adding another playable slot, add one row to `catalog/slot-packages.json`, one
`games/<Game>/src/FortuneForge.Games.<Game>` project and one
`games/<Game>/client/FortuneForge.Games.<Game>.Client` workspace. Then register those
two packages at the server and route composition boundaries. Do not add game rules,
reels, paytables, feature policy, in-game CSS or in-game media to the host.

Jade Dragon uses an independently built embedded client package under
`games/JadeDragon/client/jade-dragon`. Its lazy host route owns authentication and the
shared navbar; the embedded client owns all game presentation.

## Verification

From the repository root:

```powershell
dotnet test games/FortuneForge.Games.slnx --configuration Release
npm ci
npm run games:check
npm run games:test
npm run games:build
```

## Jade Dragon

The in-repository Jade Dragon game is in [JadeDragon/](JadeDragon/). Its engine uses neutral
`Snn` symbol identifiers and owns its game rules. The host supplies authentication,
balances, and persistence. Start with [JadeDragon/AGENTS.md](JadeDragon/AGENTS.md) for
the instructions for continuing work on this module.
