# Neon Nights backend package

This project owns Neon Nights' definitions, reel generation, matching, payout
rounding, feature awards, paid Arcade/Disco transitions, and cascade refills.
It builds without the Fortune Forge host and without another game package.

Its only project dependency is `FortuneForge.Games.Slots.Contracts`, which holds
data structures and random/configuration ports rather than gameplay algorithms.

`new NeonNightsEngine(random)` uses the package's versioned definitions. The
definition-source overload supports captured-rule replay and deterministic
fixtures. The host maps `NeonSpinOutcome` into its existing API/audit envelope;
authentication, ledger transactions and atomic persistence remain host services.

Build and verify from the repository root:

```powershell
dotnet build games/NeonNights/src/FortuneForge.Games.NeonNights
dotnet test games/NeonNights/tests/FortuneForge.Games.NeonNights.Tests
```
