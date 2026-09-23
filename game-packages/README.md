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

## Verification

From the repository root:

```powershell
dotnet test game-packages/FortuneForge.Games.slnx --configuration Release
npm ci
npm run games:check
npm run games:test
npm run games:build
```
