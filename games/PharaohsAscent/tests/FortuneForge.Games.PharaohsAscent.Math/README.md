# Pharaoh's Ascent RTP analysis

This test-only console measures the current game without changing its odds or contacting a wallet, database, browser, or deployed service. It references the native game server package directly.

## Current v3 rules

The game-owned `../../math-v3.json` contains the current integer paytable, payout scale `1`, fresh funding requirement of at least three red scarabs, and overall target RTP `0.94`. The target describes optimal long-run play; it is not a promised return from a session or every room-choice policy. The wagering contract remains `../../wagering.json`: R0.50 minimum, R3 default, R75 maximum in R0.50 steps, at 25 cents per point.

Six normally paid qualifying spins fund one Sealed Tomb ticket at their funded average wager. Tomb prizes are unchanged: secured 5× entry value, six torch actions, and exact optimal conditional mean 26.35× its funded stake. Existing partially earned two-scarab funding buckets finish under their historical requirement before moving to the new requirement. V3 funded averages round down instead of granting value through rounding up.

Sunfire records the wagers that funded accepted meter points. Both improved seal appearance and the eventual bonus are capped by that funding at the current wager; the bonus uses exact integer arithmetic with division only at the final award. Energy-assisted collection completion uses the funded average. New pity counters and earned boost credits are kept by wager, with finite historical benefits preserved. Earned spins use a FIFO queue: each scatter or guardian batch retains its own funded wager and mode, so a retrigger cannot reprice earlier spins.

Historical recorded engines retain their frozen definitions, 0.856 payout scale and replay behavior. An existing free-spin carry without the new queue continues through frozen v2 progression until it drains. New v3 runs use the native version, energy, pity and boost-availability helpers described below.

## Reproduce

Build in Release so the running Debug review harness stays untouched:

```powershell
dotnet build games/PharaohsAscent/tests/FortuneForge.Games.PharaohsAscent.Math/FortuneForge.Games.PharaohsAscent.Math.csproj -c Release -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false
```

Run a 10,000-paid-spin benchmark at the minimum, default and maximum legal flat stakes. A point is 25 cents, so these are R0.50, R3.00 and R75.00. Current admission bounds come from the game-owned `../../wagering.json` contract:

```powershell
$env:DOTNET_PROCESSOR_COUNT = '2'
$mathArguments = @(
  '--paid-spins=10000',
  '--stakes=2,12,300',
  '--batch-size=10000',
  '--parallel=2',
  '--seed=20261122',
  '--strategies=coins,keys,random,optimal',
  '--output=output/analysis/pharaohs-ascent/rtp-smoke.json'
)
dotnet games/PharaohsAscent/tests/FortuneForge.Games.PharaohsAscent.Math/bin/Release/net10.0/FortuneForge.Games.PharaohsAscent.Math.dll @mathArguments
```

For the sustained measurement, change `--paid-spins` to `1000000` and the output filename to `rtp-million.json`. This gives 100 consecutive 10,000-paid-spin blocks per stake. `--paid-spins` is the number of paid spins **per stake**, not per strategy. Maximum parallelism is limited to two stake streams.

The seed controls test-only SplitMix64 streams with rejection sampling for unbiased bounded draws. Seeds are derived separately for each stake, reel stream, tomb outcome strategy and random decision policy. Identical arguments reproduce the financial totals. Generated timestamps and elapsed timing naturally differ.

## Production policy fidelity

- Each stake stream has its own version-specific `PharaohsAscentEngine` instances, player, and continuous `ClassicSlotRoundState`.
- Every spin uses the native `EngineVersionForSpin`, `EnergyBeforeSpin` and `PityBeforeSpin` helpers, the locked funded wager and feature mode. This preserves wager-funded appearance benefits and wager-specific pity instead of passing raw counters as replacement rules. Progress calls `PharaohsAscentRoundPolicy.Advance` and `SpendsLegacy` directly.
- All earned spins, including retriggers, are played before the next paid spin. The merged carry, its changing wager/mode, collections, energy settlement and per-match rounding retain their production behavior.
- Each paid cycle then redeems every funded tomb ticket through `SealedTombRules.Act`, including entry, room choices, finale, collection and dismissal. Tomb reward calculations are never reimplemented by the simulator.
- All financial amounts use the production `PharaohsAscentRoundPolicy.ToCents` rounding. Boosts are off by default, matching the current game UI. Explicit `--use-earned-boosts=true` spends a boost on the next normally paid or earned spin only when native `BoostPointsAvailable` covers the production activation cost for that wager; native progression subtracts that cost. This exercises the public API capability without granting extra points or charging another cash wager.
- No extra spins are generated to clear unfinished sigils, collections or energy. Their final quantities and funding totals are reported explicitly. At the final boundary there are no unplayed earned spins or unredeemed completed tickets.

By default the stakes do **not** share a reel trace: payout rounding can affect state progression. Explicit `--paired-stakes=true` derives reel, tomb-prize and decision streams without the stake factor, allowing coupled flat-stake proportionality checks through the same native rules. It does not change prizes, probabilities or draw bounds. Stake profiles then intentionally share random draws; they are not independent replications of one another. Strategies within one stake may share its reel/funding trace because a completed tomb decision changes neither reel rules nor funding. Each strategy executes real tomb actions on a copy of the same earned ticket with its own outcome RNG. The persisted comparison branch is `coins`; it is fully completed and dismissed before reel play continues.

## Tomb strategies

| CLI value | Decision policy | Exact conditional mean per funded ticket |
| --- | --- | --- |
| `coins` | Choose six coin rooms. | 20.9 times its funded stake |
| `keys` | Choose three coin rooms, then three key rooms, then open the sarcophagus. | 26.35 times its funded stake |
| `random` | Uniformly choose among the three currently offered room types. Open an unlocked sarcophagus. | Measured from production actions |
| `optimal` | Exact finite-horizon dynamic programming using the current visible relic offer and accumulated public state. | 527/20 = 26.35 times its funded stake |

`keys` is deliberately the **late-key** policy, not three immediate key rooms. The latter would have a lower 18.4-times conditional mean. The optimal helper only selects actions; production rules still generate every outcome. Its static checks exercise the production coin/finale thresholds, relic combinations and final-room key precedence.

Conditional ticket means are not the game's total RTP. The measured tomb contribution divides actual expedition awards by actual paid wagers, so it includes the observed ticket frequency and funding.

## Reading the report

`observedRtp` is a ratio: `0.94` means 94%. The total is the sum of paid reel, earned reel and strategy-specific tomb RTP. Paid and earned reel totals already contain their energy multipliers; the separately reported energy extras explain those totals and must not be added again. The report includes a frozen pricing snapshot so candidate tables can be identified independently of later source edits.

The report includes paid/earned payout points and cents, paid wager totals, paid spins that actually added a funded sigil under the production policy, paid pity triggers, total generation attempts, scatter/collection awards, energy multipliers, expedition frequencies, mean prizes and unused terminal resources.

`payoutContributions` groups the selected paying matches by symbol, actual match length, owning paytable rule length, multiplier and payout scale. It separates normally paid and earned matches, before-energy and settled points, and their contributions to the actual paid-wager denominator. Four-symbol edge runs can use the three-symbol paytable rule; scarab groups are reported separately from ordinary paytable matches. All contribution totals must reconcile exactly to the reel totals or the runner stops.

`selectedMatchLinearRtpPerMultiplier` gives the wager-weighted exposure of the currently selected matches, including their energy events. It is useful for candidate integer-table calibration, but changing a table can change wild candidate selection and overlap deduplication. Rounding can also affect it. Always measure candidate changes through the production engine rather than treating this diagnostic as an exact replacement engine.

## Flat-stake proportionality and regression checks

For fresh fixed wagers on the current even-point grid, integer base prizes are even and the 1.5× energy award is exact. Every funded average stays equal to that fixed wager. Multiplying the wager therefore scales all financial returns while preserving selected outcomes and progression. This proof does not cover mixed averages or permit passing raw energy/pity counters around the native funding helpers.

A short paired run can check the whole legal grid through production flow without running a million spins separately at every stake:

```powershell
$grid = ((1..150 | ForEach-Object { $_ * 2 }) -join ',')
dotnet games/PharaohsAscent/tests/FortuneForge.Games.PharaohsAscent.Math/bin/Release/net10.0/FortuneForge.Games.PharaohsAscent.Math.dll --paid-spins=1000 "--stakes=$grid" --paired-stakes=true --wager-policy=fixed --batch-size=10000 --parallel=2 --seed=20261125 --strategies=coins,keys,random,optimal --output=output/analysis/pharaohs-ascent/rtp-v3-paired-grid.json
& games/PharaohsAscent/tests/FortuneForge.Games.PharaohsAscent.Math/Verify-PairedStakes.ps1 -ReportPath output/analysis/pharaohs-ascent/rtp-v3-paired-grid.json
```

The verifier checks complete wager coverage, exact cross-multiplied financial totals, outcome/progression counters, every reported tomb strategy, and scaled terminal funding. The checked artifact `rtp-v3-paired-grid-validation.json` passed all 150 stakes × 1,000 paid cycles. This is a proportionality regression check, not an RTP estimate; its partial-block confidence interval is intentionally null.

After the funded boost-pool and appearance-energy helpers were added, 10,000-paid-spin reruns at wager 12 reproduced the first financial block of each earlier million-spin report exactly. `rtp-v3-equivalence-normal.json` uses seed 20261123, matching `rtp-v3-final.json`; `rtp-v3-equivalence-boosts.json` uses seed 20261124, matching `rtp-v3-earned-boosts.json`. Keep `--paired-stakes=false` for this comparison, preserve each report's strategies and boost setting, and compare paid turnover, paid/earned reel returns and each strategy's tomb return. Fixed-stake helper behavior is mathematically unchanged, while switching policies must be tested on the latest native helpers.

## Bet-switching checks

The default `--wager-policy=fixed` keeps the selected stake for normally paid spins. Optional test-only policies make legal wager choices from saved public progression without modifying any game rule:

- `sunfire-switch`: minimum paid wager below a full Sunfire meter; selected high wager while it is full.
- `sunfire-anticipate`: minimum paid wager below 90 Sunfire; selected high wager at 90 or above.
- `pity-switch`: minimum paid wager until the last observed full-five miss counter reaches the production threshold, then selected high wager. The native wager-specific counter still determines whether that high wager has earned a redraw.
- `progression-switch`: selected high wager when either the Sunfire meter is full or the full-five pity threshold is reached; minimum otherwise.
- `alternating`: alternate minimum and selected high paid wagers to exercise mixed funded averages.

For example, use `--stakes=300 --wager-policy=sunfire-switch --paid-spins=100000 --output=output/analysis/pharaohs-ascent/rtp-sunfire-switch.json`. `stakePoints` identifies the selected high wager in these policies; `totals.paidWagerPoints` and `paidWagerCents` contain the actual changing paid turnover. Every earned spin still uses its saved wager, every complete ticket uses its actual funded average, and the ratio confidence interval weights actual turnover. The reported tomb mean is funded-wager-weighted. `oddFundedEarnedSpins` reports when mixed averages leave the even-point paid wager grid.

These checks can detect benefits from charging persistent meters at low stakes and redeeming them at high stakes. They do not force energy, triggers, reel stops, bonuses, or outcomes, and they do not replace variable-bet analysis with a flat-stake proportionality claim.

`rtp-v3-switch-final.json` exercises 500,000 paid cycles with `--stakes=300 --wager-policy=progression-switch --use-earned-boosts=true --seed=20261126`. Its independent accounting check, `rtp-v3-switch-final-validation.json`, reconciles actual low/high turnover, funded boost conservation, paid/earned energy, ticket funding, tomb components, block sums and per-symbol returns. A tested policy is not an exhaustive search over all possible adaptive choices.

State remains continuous across block boundaries. The approximate 95% interval uses the Student-t block-ratio estimate, retaining the serial dependence inside each long block:

`r = sum(P_b) / sum(W_b)`

`SE = sqrt(B / (B - 1) * sum((P_b - r * W_b)^2)) / sum(W_b)`

Here each block payout includes all earned spins and completed tomb tickets following its paid cycles. The report also gives lag-one block-return correlation. A single smoke block has `blockConfidence95: null`; individual reel spins or room actions are not treated as independent observations.

The sample starts with empty progress and reports the remaining partial resources without assigning them invented cash value. These are finite-sample measurements with approximate uncertainty, not an exact theoretical total RTP or a deployment qualification.

The initial three-stake 10,000-paid-spin benchmark completed in 6.71 seconds on the development computer with two workers. That historical report used the former 2,000-point/R500 maximum; its artifacts remain unchanged. New invocations enforce the current 300-point/R75 admission cap. Previously funded carries, tomb tickets and frozen paid rounds retain their historical terms. Use the JSON's wagering snapshot and timings for the actual contract, machine and run.
