# Mines rules and payout mathematics

FortuneForge Mines is a single-player, manual-selection game against the house. The board contains
25 tiles in five rows and five columns. Choose 1–24 mines and a wager of 10–10,000 whole cents
($0.10–$100.00) before starting. Starting fixes the wager, mine count, and complete board. A safe
selection reveals a gem. After at least one gem, either choose another tile or cash out. Selecting
a mine immediately ends the round with zero return. Finding every safe tile automatically pays
the final return. Cashing out ends the round and reveals the board. A new wager starts a new board.

These mechanics follow [Stake's official Mines rules](https://stake.com/casino/games/mines?game=mines)
(5×5 grid, 1–24 mines, gem selections, cash out, loss on a mine, nominal 99% RTP) and
[DraftKings' published Mines rules, version Dis.A_1.0, January 18, 2024](https://gaming-us-pa.draftkings.com/rgs/static/games/game-rules/latest/assets/gameRules/Mines/Mines_Rules_Dis.A_1.0.docx.pdf)
(wager before play, cash out after a safe selection, automatic payment at all safe tiles).
Reviewed October 20, 2026. FortuneForge's wager limits and exact cent rounding below are its own
explicit configuration; DraftKings' separate cap/consolation feature is not used.

## Exact curve

Let `m` be the mine count, `s = 25 − m`, `k` the distinct safe selections, and `w` the integer wager
in cents. Every board is a uniform subset of `m` of the 25 positions. The probability of surviving
`k` selections, with no foreknowledge of hidden tiles, is:

```text
p(m,k) = C(25 − m, k) / C(25, k)
       = product(i = 0..k−1, (25 − m − i) / (25 − i))
```

For `1 ≤ k ≤ s`, the gross multiplier, including the original stake, is exactly:

```text
M(m,k) = (99 / 100) / p(m,k)
gross return cents = floor(w × 99 × C(25,k) / (100 × C(25−m,k)))
net profit cents   = gross return cents − w
```

`M × p = 0.99` before currency rounding: the nominal house edge is 1%. The code uses `BigInteger`
to compute the rational and floors exactly once at final settlement. It never multiplies the wager
by a displayed or rounded multiplier. Whole-cent truncation adds less than one cent of loss on
each winning return, so realized RTP for a fixed stopping position is at most 99%, with an
additional expected rounding loss strictly below `p` cents per round. The UI should show that
returns round down to whole cents. For example, at three mines and one gem, the exact multiplier
is `1.125×`: a 100-cent wager returns 112 cents, even when the multiplier display reads `1.13×`.

Before any gem, the informational multiplier is `1×`; cash out is unavailable and the cash-out
amount is zero. Lost rounds display `0×` and pay zero. Next multiplier is available only for an
active round. No zero-selection refund/cash-out, cap, consolation award, or post-selection change
to mines exists.

## Maximum win

At all safe tiles the multiplier is `0.99 × C(25,m)`. The largest binomial coefficients occur at
`m = 12` and `m = 13`, where `C(25,12) = C(25,13) = 5,200,300`. Therefore:

```text
maximum multiplier = 0.99 × 5,200,300 = 5,148,297×
maximum gross return = 10,000 × 5,148,297 = 51,482,970,000 cents ($514,829,700.00)
maximum net profit = 51,482,960,000 cents ($514,829,600.00)
```

All 300 valid `(m,k)` points are tested against an independent falling-factorial calculation,
including boundary wagers, one-cent flooring, monotonic multipliers, and the global maximum.
These large possible returns require host liquidity/exposure review before production enablement;
the provided sample uses explicitly labeled practice credits.

## Randomness, privacy, and persistence

`SecureMinesBoardGenerator` uses partial Fisher–Yates: at step `i`, select a uniform index from
`[i,25)` using `RandomNumberGenerator.GetInt32`, then swap it into the selected prefix. Each ordered
selection of `m` distinct positions has probability `1/(25×24×…×(26−m))`; discarding ordering gives
each mine subset the same probability `1/C(25,m)`. Sorting the selected mine positions is only a
canonical storage format. All positions are drawn once, before any reveal, and are never changed.

The private serializable `MinesRoundState` contains the fixed board, the ordered reveal history,
and immutable wager/mine count. It is a server storage type and must never be returned directly
from an API. `MinesEngine.Project` returns only gems actually selected while active; every other
tile remains `Hidden`. Two boards consistent with the same public history yield identical active
views. Terminal projection discloses all positions. The engine validates restored state and
exact expected payout before every transition. No client seed, outcome, mine placement, or random
value is accepted by the engine API.

The host is responsible for authenticated ownership, atomic durable wallet/round updates,
idempotency, concurrent command checks, insufficient funds, and recovery. It must accept/debit
the wager in the same transaction that persists the generated board; failed transactions must not
publish or make boards selectable. A view and history entry must stay private to its owner.
