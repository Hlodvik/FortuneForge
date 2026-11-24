# Jade Dragon package guide

Jade Dragon: The Golden Pearl is the finished identity of this package. The former
Empty Slots scaffold has been retired. Game ID: `empty-slots-v1`. Engine version:
`jade-dragon-cascade-v2` for new rounds. `empty-slots-engine-v1` remains the
legacy model for previously awarded free games and replay.

Follow the required slot structure and server-authority rules in the repository
root `AGENTS.md`. Existing Jade-specific code in host routes, API adapters and
server bridges is unfinished migration work, not the intended ownership boundary.

## Start here

New cascade rules and random-draw replay live in `JadeDragonCascadeRules.cs`
and `JadeDragonCascadeEngine.cs`. Jade's server package must own game-specific
admission, round state and settlement orchestration. The platform supplies
generic authentication, wallet/ledger and storage services. V2 wagers use
25-cent points and v2 payouts use whole cents.
The notes and sample below describe the legacy engine.

1. Read `games/JadeDragon/SYMBOLS.md` and the embedded
   `games/JadeDragon/src/FortuneForge.Games.JadeDragon/Data/jade-dragon.json`.
2. In `games/JadeDragon/src/FortuneForge.Games.JadeDragon`, read
   `JadeDragonEngine.cs`, `JadeDragonSymbols.cs`, and `Engine/Models/SpinModels.cs`.
3. Use `JadeDragonEngine.SymbolMetadata` and `Paytable` for the exact roles and
   payouts. Treat `S01`–`S23` as stable protocol IDs. Give them names and artwork
   only in the new client mapping.
4. Work on the game client in `games/JadeDragon/client/jade-dragon`. Its screen,
   controller, game-specific API contract/validation and request recovery must
   be package-owned. The current host `JadeDragonRoute.tsx` and `jadeDragonApi.ts`
   contain Jade-specific behavior that still needs moving into the game package;
   the host should retain only generic authentication, mounting and the navbar.

## Backend API

```csharp
using FortuneForge.Games.JadeDragon;

var engine = new JadeDragonEngine();
var rules = engine.Definition;
var symbols = engine.SymbolMetadata;
engine.ValidateWager(2);
var outcome = engine.Spin(
    wagerPoints: 2,
    playerId: authenticatedPlayerId,
    specialBoostApplied: admittedBoost,
    pityMisses: persistedPityMisses,
    freeSpinFeatureMode: admittedFeatureMode,
    isFreeSpin: admittedFreeSpin);
```

Production defaults use cryptographic randomness. The engine's only injected
infrastructure port is `IGameRandomSource`; balances and database access are not
part of this library. Pass the admitted state from the server, and persist the
returned state with settlement.

`Reels[reel][row]` is column-major, with zero-based indexes. `ReelStops` are
indexes into the effective playable reel strips. Base layout is 5 columns × 4
rows. The `copy-expand-wild` feature expands to 6 rows. Read returned dimensions
instead of fixing the renderer to one height.

`Payout.TotalPoints` is the authoritative evaluated total. Payline match
positions identify the cells to highlight; use the supplied breakdown rather
than calculating a payout in the browser. `ConsecutiveFiveMisses` is the next
pity count. `FreeSpinsAwarded`, `SpecialPointsAwarded` and
`CollectionSymbolsAwarded` feed server-side progression.

## Progression and integration

- `JadeDragonCollections.AdvancePaid` accepts exact collection counts and wager
  totals in native points. It returns next counts/totals, awarded spins, feature
  mode, and the bonus wager. Call it within the existing settlement transaction.
- Do not expose or reinstate public demo/practice gameplay. Historical demo
  helpers are not an approved local-testing implementation; use the production
  paid flow with isolated test accounts, funds and infrastructure.
- An awarded collection feature mode is `copy-expand-wild`; ordinary scatter
  spins have no feature mode. Persist the admitted mode and locked bonus wager.
- The current native point value is 25 cents. New paid wagers are capped at 400
  points (R100) under the current Jade admission policy; special-point boosts
  cost 8 points. These game-specific limits belong in Jade's server package and
  must be validated there regardless of browser checks.
- Keep account authentication, wallet ledger execution and storage
  infrastructure in the platform. Jade owns game-specific admission,
  progression, persistence/recovery and wallet-settlement orchestration through
  generic ports. Do not import another game's rules or duplicate the wallet.

The existing host bridge is in `FortuneForge.Server/Slots/Games/JadeDragon`.
Its shared HTTP envelope has legacy property names; the engine uses names such
as `PatternPayoutPoints`, `CollectionProgress` and `CollectionSymbolsAwarded`.
Preserve public contracts and historical replay while moving Jade-specific
translations and validation into Jade's package. Their current host location
does not satisfy standalone ownership.

The existing paid endpoints are `GET /api/slots/state?gameId=empty-slots-v1`
and `POST /api/slots/spins` with `{gameId,wagerPoints,useFreeSpin,useSpecialBoost}`.
They use the host's existing authentication/session flow. The game module does
not create a separate Firebase project or deployment.

Presentation changes belong in the Jade client. Changes to reels, probabilities,
payouts, or feature rules belong in this backend package and should increment
the engine version. Do not couple this game to another game's implementation.
