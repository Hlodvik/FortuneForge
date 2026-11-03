# Midnight Vault calibration harness

This tool exercises only `dragon-hoard-v1` through the real `SpinService.SpinAdmitted`, reel generator, evaluator, payout calculator, and pure `MidnightVaultBonusRounds` helper. It never starts a server or touches accounts, wallets, Firestore, or other games. Synthetic accepted admissions exercise engine entitlement pricing; they do not establish wallet or authorization parity.

The active v3 model uses the accepted approximately38.5% visible-letter mix, integer native payouts and scalar1. Its targets are90% RTP and21% resolved-spin hit rate, each ±2 percentage points, plus39% visible letters ±2 points. The original30% hit criterion was explicitly changed for this fixed mix; changing metadata does not repair RTP. Historical v1/v2 game hit targets remain30%, with their original null feature-profile target. The inherited bankroll diagnostic and RTP-below100% checks remain in place.

```powershell
dotnet tools/FortuneForge.MidnightVaultMath/bin/Release/net10.0/FortuneForge.MidnightVaultMath.dll --cycles 250000 --seeds 20261114,20261115 --wagers 100 --boost auto --out output/midnight-vault-review/holdout.json --enforce --enforce-intervals --require-server-sha <final-server-sha256>
```

Use `--boost both` to compare immediate spending at the eight-point POWER cost with no spending, and `--wagers mixed` for a legal cash-wager schedule. Existing common boost symbols 2/3/4 are halved; 8 is protected like 5/6/7. Each comparison restarts the same seeded RNG and carry state. Every paid cycle resolves all awarded rounds and retriggers before the next paid wager; exact gem wager totals, pity and special points persist across cycles. Residual gem/POWER balances are reported, not silently cashed out. Each round locks its own wager, mode and math model. Both new triggers award10 spins; existing v1 entitlements retain their original model and6/7-spin retrigger rules.

`--integer-search` reprices every complete evaluator alternative before winner selection and geometry deduplication, using the production calculator and independent coefficient oracle. The v3 family multiplies each frozen v2 native3/5 rate by exact decimal factors0.25..0.75, step0.01, rounds once AwayFromZero and floors at1. Identical tables are deduplicated with factor aliases; smooth777 remains3x. Selection requires both predeclared calibration seeds20261112/13 at50,000 complete paid cycles,100points and auto boost. It chooses the paid-amount-weighted pooled RTP nearest90% within88–92%, then fewer changed rules, then ordinal candidate ID. Every candidate and failure is retained. Independent holdouts validate the selected production table; fractional scalar overrides are rejected.

The tool checks per-match positive half-up payout arithmetic for every native rule at every tested wager, collection completion/overflow, exact POWER geometry/deduplication, boost stop counts and POWER conservation. It reports visible counts for all fifteen active symbols and payout contribution by symbol.

Visible frequencies are recorded by paid/free feature mode and POWER-boost state. The earlier requirement that letters individually rank first was superseded by the user's accepted38.5% aggregate mix. The top-four metric is descriptive only; conditional bonus/boost shares are reported separately.

Intervals use contiguous batch means, so state correlation within each batch is preserved. They are approximate Monte Carlo intervals, not an exact RTP proof. The resampled bankroll diagnostic follows the older audit's empirical-cycle method and is not a stateful account-bankroll simulation.

`--enforce` applies the repository's point-estimate/hit/ruin/frequency policy. `--enforce-intervals` additionally requires the entire approximate RTP batch interval to lie inside the target ±2 percentage points. Use both for the final fixed-wager holdouts. Confidence intervals remain approximate, even when the check passes.

Mixed stakes use the same pure queue helper as paid settlement: exact contributing gem-wager totals, a single AwayFromZero average at each completion, and separate locked rounds. Odd derived bonus wagers are admitted via `SpinAdmitted`; cash stakes remain2..2000 in steps of2. This establishes pure-helper/engine behavior, not Firestore transaction, admission-idempotency or wallet correctness.

`--integer-proof` checks all1000 admitted cash stakes and all1999 integer derived bonus wagers against literal integer arithmetic and selected geometry, including4/6-row smooth777 patterns. It also checks weighted FIFO transitions and couples complete seeded cycles at2/100/2000 with boost off/on. Seeded initial liabilities are used only in coupling tests, never in RTP samples. Run the tool only against the coordinated current Release server DLL; compare DLL and source hashes before treating evidence as final.

After the owner sets the integer table and rebuilds the server, export the effective definition directly into a fresh output directory:

```powershell
dotnet tools/FortuneForge.MidnightVaultMath/bin/Release/net10.0/FortuneForge.MidnightVaultMath.dll --contract-only --contract output/midnight-vault-review/effective-contract.json
```

That contract contains all fifteen active IDs, dedicated resource IDs, actual reels/paytable/profile, examples at legal wagers, rounding/feature data and source hashes. It always reflects the actual provider scalar, not a calibration-only multiplier override. Source hashes before and after each measured run must match before treating results as final.

The selected table is native-f054 (equivalent factors0.54/0.55/0.56), pinned independently by the literal oracle: J1/1,Q1/2,K1/4,A1/4,chips1/4,ruby ring2/6,cash briefcase2/8,vault wild3/10,diamond1/2 for3/5 matches. Frozen v1/v2 tables and reels remain available for earned bonuses. Earlier integer-table evidence remains historical and is not relabeled as v3 validation.

After the coordinated final build, use `--require-server-sha` with the exact handed-off server DLL hash. Planned final checks are:

```powershell
dotnet tools/FortuneForge.MidnightVaultMath/bin/Release/net10.0/FortuneForge.MidnightVaultMath.dll --integer-proof --coupled-cycles 2000 --seeds 20261114,20261115 --require-server-sha <final-server-sha256> --out output/midnight-vault-review/integer-proof.json
dotnet tools/FortuneForge.MidnightVaultMath/bin/Release/net10.0/FortuneForge.MidnightVaultMath.dll --cycles 100000 --seeds 20261116 --wagers mixed --boost both --require-server-sha <final-server-sha256> --out output/midnight-vault-review/mixed-initial.json
dotnet tools/FortuneForge.MidnightVaultMath/bin/Release/net10.0/FortuneForge.MidnightVaultMath.dll --cycles 250000 --seeds 20261117,20261118 --wagers mixed --boost both --require-server-sha <final-server-sha256> --out output/midnight-vault-review/mixed-confirmation.json
```

Commands alone are not pass evidence. The mixed schedule is2,6,20,100,2000,100,20,6 points. Its initial100k intervals were too wide; the approved confirmation retains that sample and pools all600k cycles per policy by actual paid amount, combining independent seed batch standard errors. Off and auto remain separate. See `output/maintenance-2026-11-02/midnight-vault-frequency-rebalance/README.md` for actual results, preserved failures and scope limits. Never overwrite the historical evidence when rerunning the tool.
