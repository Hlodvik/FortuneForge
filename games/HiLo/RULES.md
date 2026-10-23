# FortuneForge Hi-Lo rules and payout mathematics

This is FortuneForge's **strict higher/lower, independent-draw card variant**. It is single player versus the house. The development sample uses clearly labelled practice credits; these rules do not authorize production wagering.

## Rules provenance and variant decisions

The primary rules source is [BGaming's official Hi-Lo Switch rules](https://bgaming.com/wp-content/uploads/2025/04/63ee59f76989134d47bcdf3d_hiloswitch.pdf), read October 20, 2026. It specifies A as lowest, K as highest, equal ranks losing, and choice-dependent payouts. FortuneForge adopts those comparison rules. Its three-deck layout, 96% RTP, switching restrictions and maximum win are **not** FortuneForge's rules.

[Stake's official digital Hilo page](https://stake.com/casino/games/hilo), read October 20, 2026, corroborates streak play, cash-out and a 52-skip limit. Its stated equal-card treatment differs from this variant. FortuneForge does not copy that tie behavior or represent its rules as identical to Stake's.

Deck sampling, limits and the 1% conditional choice edge below are explicit FortuneForge design decisions, rather than a claim that all casino Hi-Lo products share one standard.

## Cards and round flow

- A standard deck has 52 equally likely cards: 13 ranks, four suits. A = 1, 2–10 use their values, J = 11, Q = 12, K = 13. Suits never affect a comparison.
- Every draw is independent **with replacement**. All 52 cards remain possible on every draw, including a card identical to a previously shown card. This is not a finite shoe; revealed history does not alter the next probabilities.
- The server accepts a wager of 0.10–1000.00 credits in whole cents, debits it once and reveals the starting card. The round begins at 1.00× and a streak of zero. A player may immediately cash out for the original wager.
- Choose **Higher** to win only if the next rank is strictly larger, or **Lower** to win only if it is strictly smaller. Equal rank loses for either direction, even when the suits differ. A failed guess ends the round with zero return, including the accumulated streak return.
- Lower at A and Higher at K are impossible and rejected before a draw. No new wager or separate debit occurs for a valid choice; the round's current return is risked.
- A correct choice increases the cumulative multiplier and streak. The player can risk that return again or cash out. Cash-out ends the round and credits the complete return, including the initial wager.
- **Skip** replaces the current card with one independent draw, preserving the multiplier, current return and streak. It neither wins nor loses. A round permits 52 skips, after which the player must choose or cash out. Skips are recorded in revealed history.
- A successful choice that reaches the payout limit or the 100-success safety limit automatically cashes out. A finished round cannot accept another choice, skip or cash-out.

## Derived probabilities and multipliers

Let the current rank be `r` (1–13), and `k` the number of winning ranks. Since all suits have the same effect and draws use replacement:

```text
Higher: k = 13 - r       Lower: k = r - 1
Probability of success p = (4 × k) / 52 = k / 13
Probability of equal rank = 4 / 52 = 1 / 13
Uncapped successful-choice factor f = 0.99 / p = 12.87 / k
Next cumulative multiplier M' = M × f
```

`k = 0` disables the choice. The two available choices do not imply a 50/50 coin flip: at 7, each wins with `6/13` probability and both lose on the `1/13` tie probability.

| Winning ranks k | Win chance (display approximation) | Uncapped factor |
| --- | ---: | ---: |
| 0 | 0% | Unavailable |
| 1 | 7.692308% | 12.87× |
| 2 | 15.384615% | 6.435× |
| 3 | 23.076923% | 4.29× |
| 4 | 30.769231% | 3.2175× |
| 5 | 38.461538% | 2.574× |
| 6 | 46.153846% | 2.145× |
| 7 | 53.846154% | 1.838571428571…× |
| 8 | 61.538462% | 1.60875× |
| 9 | 69.230769% | 1.43× |
| 10 | 76.923077% | 1.287× |
| 11 | 84.615385% | 1.17× |
| 12 | 92.307692% | 1.0725× |

At 7, a successful higher choice returns `2.145×`; `(6/13) × 2.145 = 0.99`. At A, Higher wins on 12 ranks and returns `1.0725×`; `(12/13) × 1.0725 = 0.99`. These examples include stake in the return, not merely profit.

The edge is **1% of the amount risked on each uncapped choice**, before cent rounding. This is not a flat 99% round RTP. For a fixed run of `n` risked choices without caps or rounding, expected gross return relative to the initial wager is `0.99^n`; for two choices it is 98.01%. Skips add no risked choice. The displayed chance and factor are based on the current revealed rank, not a client prediction.

## Return limits and cent rounding

```text
L = min(1000, 100000 / initialWager)
M' = min(M × f, L)
Cash-out credits = floor(initialWager × M' × 100) / 100
```

The maximum gross return is both **1000× the initial wager** and **100,000.00 credits**. The smaller limit applies. Limits are gross return, not profit. For example, a 300-credit wager has a multiplier limit of `333.333333…×`, returning exactly 100,000.00 credits at the cap. The implementation explicitly preserves the exact cap even when its multiplier is a recurring decimal.

If a choice would cross the limit, `HiLoRules.Quote` reports the capped cumulative multiplier and actual capped gross return **before** the guess. Its effective step factor is `cappedNextMultiplier / currentMultiplier`. Such a choice has a conditional return below 99% and therefore an edge greater than 1%; the cap is not silently presented as the uncapped factor. A winning capped choice settles automatically.

The engine uses .NET `decimal` arithmetic (28–29 significant decimal digits), calculates `12.87 / k` directly instead of dividing by a rounded probability, and retains full decimal precision between choices. Only the final gross credit payout is floored to whole cents. Display rounding never feeds back into the engine. Cent flooring can lower realized return by less than one cent relative to the internal gross amount. For example, 0.10 credits following factors `2.145 × 1.0725 × 1.0725` cashes out at 0.24 credits; intermediate cent-rounded returns are not reinvested.

## Server contract and privacy

`HiLoEngine` is deterministic: tests and the authoritative host inject the single newly revealed `PlayingCard`. The engine does not select random cards, accept client outcomes, store a future deck or serialize future cards. `HiLoRound` is an immutable serializable record containing only the current card, revealed history, wager, multiplier, streak, skip count, status, payout and version.

The host must authenticate account ownership, reject stale or duplicate commands transactionally, call `EnsureCanGuess`/`EnsureCanSkip` before drawing, and draw uniformly with a server CSPRNG. One valid mapping is `RandomNumberGenerator.GetInt32(52)`, where `rank = index % 13 + 1` and `suit = index / 13`. Starting debits and terminal credits must be atomic with durable state and idempotency records. Retrying a command must return its saved result rather than draw again or settle twice. A resumed round is reconstructed from persisted revealed state; the client cannot provide its own round or randomness.

## Verification

Run the isolated engine suite without editing the shared solution:

```powershell
dotnet test games/HiLo/tests/FortuneForge.Games.HiLo.Engine.Tests/FortuneForge.Games.HiLo.Engine.Tests.csproj --configuration Release
```

The suite exhausts every current rank, direction and standard next card; checks suit-independent ties, impossible actions, all probability-derived factors, exact cent flooring against a `BigInteger` rational reference over long streaks, both payout caps, auto-settlement, skip bounds, terminal-action rejection, immutable inputs and JSON refresh/resume. Transactional wallet, duplicate/concurrent command and account privacy tests belong to the server package.
