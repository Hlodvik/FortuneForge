# Pharaoh's Ascent — calibrated RTP

Verified: 2026-11-22T20:42:39.1949064-06:00 | Computer: Vinnabok

Current engine: pharaohs-ascent-sealed-tomb-v3. Intended optimal-play return: 94%. Bets remain R0.50–R75 in R0.50 steps, starting at R3. No deployment was performed.

| Production policy | Paid spins | Measured optimal-tomb RTP | Approximate 95% interval |
| --- | ---: | ---: | --- |
| Normal paid game | 1,000,000 | 93.9072% | 93.6222%–94.1922% |
| Spend earned boosts | 1,000,000 | 93.9473% | 93.6477%–94.2468% |
| Switch bets using progression cues, with funded boosts | 500,000 | 90.5629% | 89.2695%–91.8564% |

Both million-spin fixed-bet intervals contain 94%. The normal-game result comprises 63.5064 percentage points from paid reels, 13.5773 from earned reels and 16.8235 from The Sealed Tomb. All earned spins and funded tickets were played; only unfinished partial resources remain at the end.

All 150 legal bet sizes passed an exact paired production-flow check: 1,000 paid cycles per stake, identical outcome draws, proportional payouts/funding and matching progression. This 150,000-cycle grid is a proportionality proof, not another RTP estimate. The former minimum-bet rounding advantage is removed.

The Sealed Tomb keeps its prizes: 5× funded stake guaranteed on entry and an exact 26.35× conditional mean under optimal room choices. Three coin rooms followed by three keys and the sarcophagus is one optimal policy. Fresh tickets now require six paid-spin sigils earned from three or more red scarabs. Existing partial two-scarab funding batches finish under their original rule.

The versioned paytable uses whole multipliers with no fractional payout scale. Sunfire bonus value and improved seal appearance retain the wager funding that built the meter. Match safety counters and newly earned boost credits are separated by wager. Earned spins retain individual wager/mode batches. New funded averages and fractional bonus points round down. Historical prepared outcomes, saved tickets and legacy funded benefits retain their applicable rules.

The switching stress test used 460,086 minimum bets and 39,914 maximum bets, spending 1,455 earned boosts and playing 47,175 odd-funded earned spins. Its 90.5629% return did not recover the previous low-bet/high-bet overpayment. This is evidence for the tested policy, not an exhaustive proof of every possible betting strategy.

All financial accounting checks passed: actual charged turnover, energy included exactly once, earned-spin counts, ticket funding, tomb payout components, boost-credit conservation, and block sums. The 103 native backend tests and 61 client tests passed. Standalone production build and browser checks passed; the local preview uses the same compiled game/server/runtime assemblies as the simulator.

Method: test-only seeded unbiased random streams driving the production engine and native settlement/progression. Paid wagers alone form the denominator. Confidence intervals use continuous 10,000-paid-cycle blocks with Student-t ratio errors. The final credit/appearance funding guards are financially identical for fixed stakes; exact 10,000-cycle same-seed trace comparisons and native tests verify compatibility with the million-spin samples. All 49 financial source/config fingerprints remained stable during final verification. Estimates remain finite-sample measurements.

Raw measurements: rtp-v3-final.json, rtp-v3-earned-boosts.json, rtp-v3-switch-final.json. Complete validation: rtp-v3-validation.json. Full-grid proof: rtp-v3-paired-grid-validation.json. Exact policy comparisons: rtp-v3-equivalence-normal.json and rtp-v3-equivalence-boosts.json. Reproduction: games/CosmicFortune/tests/FortuneForge.Games.CosmicFortune.Math/README.md.
