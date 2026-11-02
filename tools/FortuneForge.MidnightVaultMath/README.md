# Midnight Vault calibration harness

This tool exercises only `dragon-hoard-v1` through the real `SpinService.SpinAdmitted`, reel generator, evaluator, payout calculator, and pure `MidnightVaultBonusRounds` helper. It never starts a server or touches accounts, wallets, Firestore, or other games. Synthetic accepted admissions exercise engine entitlement pricing; they do not establish wallet or authorization parity.

The repository's existing headless policy is the effective game target (currently 90% RTP and 30% resolved-spin hit rate), ±2 percentage points, RTP below 100%, and a majority of resampled 100-bet bankroll trials depleted within 1,000 paid cycles. The earlier canonical tool uses 250,000 paid cycles at 100 points and seed 20260720. Its old aggregate bonus state is not proof of the new FIFO-round semantics. The current candidate uses an integer native table at scalar1; historical fractional-scalar reports remain historical evidence.

```powershell
dotnet tools/FortuneForge.MidnightVaultMath/bin/Release/net10.0/FortuneForge.MidnightVaultMath.dll --cycles 250000 --seeds 20260720 --wagers 2,100,2000 --boost auto --out output/maintenance-2026-11-02/midnight-vault-integer-math/validation.json --enforce
```

Use `--boost both` to compare immediate spending at the eight-point POWER cost with no spending, and `--wagers mixed` for a legal cash-wager schedule. Existing common boost symbols 2/3/4 are halved; 8 is protected like 5/6/7. Each comparison restarts the same seeded RNG and carry state. Every paid cycle resolves all awarded rounds and retriggers before the next paid wager; exact gem wager totals, pity and special points persist across cycles. Residual gem/POWER balances are reported, not silently cashed out. Each round locks its own wager, mode and math model. Both new triggers award10 spins; existing v1 entitlements retain their original model and6/7-spin retrigger rules.

`--integer-search` reprices all complete evaluator alternatives in memory, before candidate choice and geometry deduplication, with both the real calculator and an independent integer coefficient oracle. The predeclared search starts at the original integer table (J-five2) and changes one five-match rule by ±1 or ±2 within1..18. It preserves every positive-paying symbol/length and all reel/feature/RNG settings. It records the complete alternative-stream digest, candidate rates, payout, hit rate, volatility, batch intervals and changed native selections. Candidate search is descriptive; validate any selected source table on independent calibration and holdout seeds. Fractional multiplier overrides are rejected because the queue pins each model's scalar.

The tool checks per-match positive half-up payout arithmetic for every native rule at every tested wager, collection completion/overflow, exact POWER geometry/deduplication, boost stop counts and POWER conservation. It reports visible counts for all fifteen active symbols and payout contribution by symbol.

The letter-weighted revision also records visible frequencies by paid/free feature mode and POWER-boost state. The overall four most frequent displayed symbol IDs must be 2/3/4/5 (Q/J/K/A). Conditional boost/feature results are reported separately; this aggregate check does not assert identical symbol ranks within every bonus mode.

Intervals use contiguous batch means, so state correlation within each batch is preserved. They are approximate Monte Carlo intervals, not an exact RTP proof. The resampled bankroll diagnostic follows the older audit's empirical-cycle method and is not a stateful account-bankroll simulation.

`--enforce` applies the repository's point-estimate/hit/ruin/frequency policy. `--enforce-intervals` additionally requires the entire approximate RTP batch interval to lie inside the target ±2 percentage points. Use both for the final fixed-wager holdouts. Confidence intervals remain approximate, even when the check passes.

Mixed stakes use the same pure queue helper as paid settlement: exact contributing gem-wager totals, a single AwayFromZero average at each completion, and separate locked rounds. Odd derived bonus wagers are admitted via `SpinAdmitted`; cash stakes remain2..2000 in steps of2. This establishes pure-helper/engine behavior, not Firestore transaction, admission-idempotency or wallet correctness.

`--integer-proof` checks all1000 admitted cash stakes and all1999 integer derived bonus wagers against literal integer arithmetic and selected geometry, including4/6-row smooth777 patterns. It also checks weighted FIFO transitions and couples complete seeded cycles at2/100/2000 with boost off/on. Seeded initial liabilities are used only in coupling tests, never in RTP samples. Run the tool only against the coordinated current Release server DLL; compare DLL and source hashes before treating evidence as final.

After the owner sets the final multiplier and rebuilds the server, export the effective definition directly:

```powershell
dotnet tools/FortuneForge.MidnightVaultMath/bin/Release/net10.0/FortuneForge.MidnightVaultMath.dll --contract-only --contract output/maintenance-2026-11-02/midnight-vault-integer-math/effective-contract.json
```

That contract contains all fifteen active IDs, dedicated resource IDs, actual reels/paytable/profile, examples at legal wagers, rounding/feature data and source hashes. It always reflects the actual provider scalar, not a calibration-only multiplier override. Source hashes before and after each measured run must match before treating results as final.

The approved candidate is scalar1, J-five2 and A-five7, with all other native rates unchanged. The independent literal oracle pins that table. The optional calibration sweep retains its original J-five2/A-five6 baseline so the predeclared search and historical reports are not silently redefined.

After the coordinated final build, use `--require-server-sha` with the exact handed-off server DLL hash. Planned final checks are:

```powershell
dotnet tools/FortuneForge.MidnightVaultMath/bin/Release/net10.0/FortuneForge.MidnightVaultMath.dll --cycles 250000 --seeds 20261104,20261105 --wagers 2,100,2000 --boost auto --enforce --enforce-intervals --require-server-sha <final-server-sha256> --out output/maintenance-2026-11-02/midnight-vault-integer-math/final-holdout.json
dotnet tools/FortuneForge.MidnightVaultMath/bin/Release/net10.0/FortuneForge.MidnightVaultMath.dll --integer-proof --coupled-cycles 2000 --seeds 20261106,20261107 --require-server-sha <final-server-sha256> --out output/maintenance-2026-11-02/midnight-vault-integer-math/integer-proof.json
dotnet tools/FortuneForge.MidnightVaultMath/bin/Release/net10.0/FortuneForge.MidnightVaultMath.dll --cycles 50000 --seeds 20261108 --wagers mixed --boost both --require-server-sha <final-server-sha256> --out output/maintenance-2026-11-02/midnight-vault-integer-math/mixed-diagnostic.json
```

These are planned commands, not evidence that the runs have passed. The mixed schedule is 2,6,20,100,2000,100,20,6 points; its weighted bonus history has different semantics from fixed-wager homogeneity and is reported separately.
