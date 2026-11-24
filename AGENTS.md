# Fortune Forge working agreements

These repository rules supplement the inherited global working agreements.

## Required slot game structure

- Every slot game must be implemented as a complete, independently buildable
  and releasable game package with both client and server ownership. This is an
  implementation requirement, not a description applied to package folders.
- The client package owns its actual game entry, screen, controller, input,
  transient UI state, animation, audio, controls, help, win presentation, assets
  and styles. It also owns game-specific API requests, response contracts and
  validation, and client request/recovery handling.
- The server package owns game rules and parameters, authoritative randomness,
  wager admission and limits, outcomes, payout calculation, bonus eligibility,
  feature progression, round persistence, retries, idempotency and historical
  replay. It owns orchestration of its rounds and wallet settlement through
  generic platform services.
- The host supplies generic authentication/session access, account and wallet
  services, storage infrastructure, auditing, navigation/navbar, lobby metadata,
  package loading and routing. It may inject authenticated transport and other
  neutral ports. Wallet balances and ledger execution remain platform services;
  do not create a separate wallet or ledger for each game.
- Game-specific API adapters, wire-format translations, symbol/reel/cascade
  validation, wager rules and recovery decisions belong in the game package.
  A host adapter that interprets a particular game's commands or outcomes is
  game implementation, even if it is called a bridge or integration layer.
- Host routes may select and mount a game or forward authenticated requests to
  its server package. They must not implement its screen, controller, gameplay,
  response validation or feature lifecycle. A manifest/assets package that
  calls host `renderSlot(manifest)`, `SlotsPage`, or a host game controller is
  an incomplete migration. A generic navbar/shell callback is allowed.
- The generic host slot page, controller and API runtime have been deliberately
  retired. Do not restore them, add a generic fallback, or copy the entire
  multi-game controller into each package to hide missing implementations.
  Implement each game's actual client and expose missing clients as migration
  failures. `npm run verify:slot-client-entries` lists remaining entry gaps;
  do not remove games from the inventory or weaken the check to obtain a pass.

## Browser and server authority

- Treat all browser input as untrusted, whether the code lives in a game
  package or the main website. Moving code between those browser locations
  does not change the trust boundary.
- The browser sends player intent, such as a wager or permitted reel choice,
  and presents server results. It may validate replies, perform display-only
  calculations, animate balances and keep transient/recovery state. These
  checks and displays never authorize a wager, payout, bonus or wallet change.
- The server must independently authenticate the player, validate choices and
  wagers against persisted state, generate outcomes, calculate awards, persist
  progression and settle through the platform wallet. Never trust a
  browser-supplied win, payout, balance, bonus entitlement or progression value
  as authoritative. Cosmetic randomness must not influence paid outcomes.

## Shared code and deployment

- Reuse neutral, versioned libraries for generic components, drawing, animation,
  algorithms, data contracts and infrastructure. Shared code must not contain
  particular games' identifiers, assets, math parameters, rules or feature
  branches. Each game declares its dependencies and owns its configuration.
- A game package must not import another game's implementation or reach into
  host source for its implementation. Keep one authoritative owner for each
  game-specific behavior and contract.
- Independent packaging does not mean one server per game. Preserve the
  existing single API service and selected per-game backend packages; do not
  provision extra servers or hosting projects to satisfy this requirement.
- A game-specific change belongs in that game's package. Generic host changes
  must remain neutral. Preserve paid behavior, stable protocol/storage IDs,
  settlement, retry safety and historical rounds during migration; architecture
  work does not authorize changes to odds, payouts or gameplay.

## Evidence required before claiming completion

- Verify the actual frontend route and release entry reach the package-owned
  game screen/controller/API client, and the actual paid backend path reaches
  the package-owned admission, engine, round lifecycle and settlement
  orchestration. Trace execution rather than counting directories or exports.
- Verify the package builds and its game behavior can run with generic host
  ports, without host-owned game implementation or another game's source.
  Use the production paid implementation and isolated test infrastructure as
  required by the inherited local-testing rules.
- Package inventories, file existence, import checks, successful builds,
  catalogue counts and independently uploaded assets do not by themselves
  establish standalone ownership. Verification gates must distinguish their
  actual scope; passing a weaker gate cannot certify the full architecture.
- Report implementation ownership, host integration, verification and deployed
  version separately. Any remaining game-specific host implementation or
  unverified execution path keeps that game incomplete or unverified. Do not
  report an all-games completion total unless every game in the current
  catalogue has the required evidence; identify omissions and remaining work.
- Existing host-owned implementations and conflicting historical migration
  notes are migration debt, not permission to repeat that structure. Make new
  game behavior package-owned and do not label a partial move complete.
- These rules do not authorize a deployment. Follow the user's current release
  instructions; the current work remains local until deployment is requested.
