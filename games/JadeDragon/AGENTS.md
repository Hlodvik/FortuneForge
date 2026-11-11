# Jade Dragon package guide

Jade Dragon: The Golden Pearl is the finished identity of this package. The former
Empty Slots scaffold has been retired. Game ID: `empty-slots-v1`. Engine version:
`jade-dragon-cascade-v2` for new rounds. `empty-slots-engine-v1` remains the
legacy model for previously awarded free games and replay.

## Start here

New cascade rules and random-draw replay live in `JadeDragonCascadeRules.cs`
and `JadeDragonCascadeEngine.cs`. The host owns the admission and wallet
transaction. V2 wagers use 25-cent points and v2 payouts use whole cents.
The notes and sample below describe the legacy engine.

1. Read `games/JadeDragon/SYMBOLS.md` and the embedded
   `games/JadeDragon/src/FortuneForge.Games.JadeDragon/Data/jade-dragon.json`.
2. In `games/JadeDragon/src/FortuneForge.Games.JadeDragon`, read
   `JadeDragonEngine.cs`, `JadeDragonSymbols.cs`, and `Engine/Models/SpinModels.cs`.
3. Use `JadeDragonEngine.SymbolMetadata` and `Paytable` for the exact roles and
   payouts. Treat `S01`–`S23` as stable protocol IDs. Give them names and artwork
   only in the new client mapping.
4. Work on the standalone presentation in `games/JadeDragon/client/jade-dragon`.
   `fortuneforge.client/src/app/routes/JadeDragonRoute.tsx` is only the authenticated
   host bridge and shared navbar.

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
- `SettleCollections` preserves the separate nonfinancial demo progression.
- An awarded collection feature mode is `copy-expand-wild`; ordinary scatter
  spins have no feature mode. Persist the admitted mode and locked bonus wager.
- The current native point value is 25 cents. New paid wagers are capped at 400
  points (R100) by the Jade host admission policy; special-point boosts cost 8 points.
- Keep Firebase identity, wallet admission and Firestore settlement in the
  application's existing platform layer. The new game should not import any
  other game's rules.

The FortuneForge host bridge is in `FortuneForge.Server/Slots/Games/JadeDragon`.
Its shared HTTP envelope has legacy property names; the independent package
above uses neutral names such as `PatternPayoutPoints`, `CollectionProgress`
and `CollectionSymbolsAwarded`. Preserve that translation at the host boundary.

The existing paid endpoints are `GET /api/slots/state?gameId=empty-slots-v1`
and `POST /api/slots/spins` with `{gameId,wagerPoints,useFreeSpin,useSpecialBoost}`.
They use the host's existing authentication/session flow. The game module does
not create a separate Firebase project or deployment.

Presentation changes belong in the Jade client. Changes to reels, probabilities,
payouts, or feature rules belong in this backend package and should increment
the engine version. Do not couple this game to another game's implementation.
