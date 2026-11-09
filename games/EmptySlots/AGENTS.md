# Instructions for Tian's agents

Build Tian's new presentation on this backend. The game is named **Empty Slots
Game** until Tian supplies a name. Game ID: `empty-slots-v1`. Engine version:
`empty-slots-engine-v1`.

## Start here

1. Read `games/EmptySlots/SYMBOLS.md` and the embedded
   `games/EmptySlots/src/FortuneForge.Games.EmptySlots/Data/empty-slots.json`.
2. In `games/EmptySlots/src/FortuneForge.Games.EmptySlots`, read
   `EmptySlotsEngine.cs`, `EmptySlotsSymbols.cs`, and `Engine/Models/SpinModels.cs`.
3. Use `EmptySlotsEngine.SymbolMetadata` and `Paytable` for the exact roles and
   payouts. Treat `S01`–`S23` as stable protocol IDs. Give them names and artwork
   only in the new client mapping.
4. Start the new frontend from `fortuneforge.client/src/app/routes/EmptySlotsGameRoute.tsx`.
   It reuses the host's shared navbar; the game surface is empty.

## Backend API

```csharp
using FortuneForge.Games.EmptySlots;

var engine = new EmptySlotsEngine();
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

- `EmptySlotsCollections.AdvancePaid` accepts exact collection counts and wager
  totals in native points. It returns next counts/totals, awarded spins, feature
  mode, and the bonus wager. Call it within the existing settlement transaction.
- `SettleCollections` preserves the separate nonfinancial demo progression.
- An awarded collection feature mode is `copy-expand-wild`; ordinary scatter
  spins have no feature mode. Persist the admitted mode and locked bonus wager.
- The current native point value is 25 cents. Paid wagers are 2–2000 points in
  steps of 2; special-point boosts cost 8 points. Existing mixed-wager legacy
  bonus behavior has not been redesigned by this starter.
- Keep Firebase identity, wallet admission and Firestore settlement in the
  application's existing platform layer. The new game should not import any
  other game's rules.

The FortuneForge host bridge is in `FortuneForge.Server/Slots/Games/EmptySlots`.
Its shared HTTP envelope has legacy property names; the independent package
above uses neutral names such as `PatternPayoutPoints`, `CollectionProgress`
and `CollectionSymbolsAwarded`. Preserve that translation at the host boundary.

The existing paid endpoints are `GET /api/slots/state?gameId=empty-slots-v1`
and `POST /api/slots/spins` with `{gameId,wagerPoints,useFreeSpin,useSpecialBoost}`.
They use the host's existing authentication/session flow. The game module does
not create a separate Firebase project or deployment.

Presentation changes belong in the new client. Changes to reels, probabilities,
payouts, or feature rules belong in this backend package and should increment
the engine version. Do not couple this game to another game's implementation.
