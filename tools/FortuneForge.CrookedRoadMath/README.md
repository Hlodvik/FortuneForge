# Crooked Road qualification

The game-owned canonical rules are `games/CrookedRoad/src/FortuneForge.Games.CrookedRoad/Data/crooked-road-v1.json`.
The client contract mirror is generated from that file. The harness calls the production domain engine; it does not provide a playable demo or a second set of game rules.

```powershell
dotnet run --project tools/FortuneForge.CrookedRoadMath --configuration Release -- --cycles=1000000 --seed=20261123 --wagers=100,1000,5000 --output=output/artwork/irish-crooked-road-2026-11-22/qualification/math-v1-seed-20261123-1m.json
dotnet test games/CrookedRoad/tests/FortuneForge.Games.CrookedRoad.Tests
```

The analytic qualification enumerates independent five-symbol paylines exactly, prices the Fort's finite held-symbol recurrence exactly, and computes each Well origin's separate wild-ripple expectation. Symmetric hidden Hill doors have the same expectation for every allowed choice. Every destination has the configured expected multiplier before downward rounding. Combining base awards with every funded and scatter ticket gives the reported rational upper bound. Road rearrangement can postpone redemption; it cannot create tickets or increase awards. The Monte Carlo policy immediately redeems all funded tickets to measure this generous upper-return policy. Its report includes batch confidence intervals and is not a certification of RTP.

The paid persistence tests require an explicitly configured loopback `FIRESTORE_EMULATOR_HOST`; they skip without it and cannot connect to production. A test-only browser host is available under `games/CrookedRoad/tests/FortuneForge.Games.CrookedRoad.FixtureHost`. It uses the exact production paid endpoints, admission, random source, state and payout rules. Only authentication and the isolated wallet transport are test substitutes. No game product registers that host.
