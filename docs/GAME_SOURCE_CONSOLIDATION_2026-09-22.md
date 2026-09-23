# Game source consolidation — 2026-09-22

## Outcome

Fortune Forge now owns the source for every reusable game package consumed by the application. The former sibling game-source checkout is no longer a build, test, packaging, deployment, or maintenance dependency after this change is committed and pushed.

## Architecture

- Server game engines live under `game-packages/games/<Game>/src` and are referenced with `.NET` `ProjectReference` entries.
- Shared game primitives live under `game-packages/src`.
- Deployed reusable clients live under `game-packages/games/<Game>/client` and are npm workspaces in the root lockfile.
- Package tests and catalog descriptors live beside the package source.
- `.nupkg` and `.tgz` files remain optional generated release artifacts; they are not committed application dependencies.

Only packages used by the current product were retained. Unused Spades, generic Slots, and Tetris package skeletons were intentionally not carried into the maintained package solution. The live slot implementations remain in the application, and the canonical roster remains the 38 games recorded in the game audit.

## Guardrails

`npm run verify:game-source` fails when:

- the server reintroduces a `FortuneForge.Games.*` binary package reference;
- a game client points at a copied `file:` archive;
- a referenced server project or client workspace is missing;
- a client dependency version differs from its workspace source; or
- an obsolete copied-package feed reappears.

CI restores, builds, and tests both .NET solutions; checks, tests, and builds every deployed game-client workspace; tests and builds the web host; and builds the deployment image from monorepo source.
