# Mines rules and payout mathematics

FortuneForge Mines is a single-player 5×5 game against the house. Choose **1–24 mines** and a
**R1–R5 wager** before starting. The server fixes the complete board when the wager is accepted.
A safe pick reveals a gem. After at least one gem, continue or cash out. Every safe tile can be
played: there is **no early automatic cash-out**. Clearing all safe tiles settles the round
automatically. A mine ends the round, with the return disclosed before that pick: normally zero,
but the bounded late-game curve can provide a consolation return.

New rounds use payout policy **2**, **90% nominal RTP** and a **50× gross-return ceiling**.
The ceiling includes the original wager: the most any new R5 round can return is **R250**.
All payments round down to whole cents exactly once. With R1–R5 wagers the rounding-adjusted
expected RTP is strictly above 89% and at most 90%, not a guarantee about an individual session.

The 5×5 layout and manual safe-pick/cash-out mechanics were reviewed against
[Stake's official Mines page](https://stake.com/casino/games/mines?game=mines) and
[DraftKings' published Mines rules](https://gaming-us-pa.draftkings.com/rgs/static/games/game-rules/latest/assets/gameRules/Mines/Mines_Rules_Dis.A_1.0.docx.pdf).
FortuneForge's versioned capped curve, wager limits and precise return schedule below are its own
configuration. These references do not imply identical payouts or provider affiliation.

## Exact capped curve

Let `m` be the number of mines, `s = 25 − m`, `k` the number of safe picks, and `w` the wager
in whole cents. With no foreknowledge of unrevealed positions, survival probability is:

```text
p(m,k) = C(s,k) / C(25,k)
U_k    = (9/10) / p(m,k)
```

If the uncapped full-clear value `U_s ≤ 50`, the safe-return multiplier remains `V_k = U_k`.
This applies to 1 and 24 mines, whose full-clear return is 22.5×.

Otherwise, let `j` be the last depth whose uncapped value `U_j ≤ 50`.
Keep the original curve through that depth, then spread its remaining growth over all remaining
safe tiles so that the final tile reaches exactly 50×:

```text
V_k = U_k                                      for k ≤ j
V_k = U_j + (50 − U_j) × (k − j) / (s − j)      for j < k ≤ s
safe cash-out / full-clear cents = floor(w × V_k)
```

The curve is strictly increasing through every safe pick, bounded by 50×. It does not shorten the
board or stop the round when the original uncapped curve would have crossed 50×.

An early low-risk cash-out can return less than the stake. For example, one mine and one gem is
0.9375×: a R1 wager returns R0.93. This is not clamped to 1×. Before the first gem the UI displays
the initial stake as 1×, but cash-out is unavailable and the mathematical initial value is 0.90×.

## Mine returns and 90% expected RTP

Simply compressing the safe-pick multipliers would reduce the expected return for later picks.
Policy 2 instead provides a precisely calculated terminal consolation when the next pick hits a
mine in the compressed portion. Let:

```text
q_k = (s − k) / (25 − k)                       probability the next pick is safe
L_k = (V_k − q_k × V_(k+1)) / (1 − q_k)        multiplier paid if that pick hits a mine
mine-return cents = floor(w × L_k)
```

Here `V_0 = 0.90` internally. Before compression, `L_k = 0`; later it is nonnegative and less
than the current safe cash-out value. A late mine return can be substantial, and may exceed the
original stake. It is not an additional payment: **each round has exactly one terminal payment**.
The active API supplies the exact `nextMinePayoutCents` so the UI can show “If you hit a mine”
before a pick. It discloses a return for that stage, never where any mine is.

For every stage:

```text
q_k × V_(k+1) + (1 − q_k) × L_k = V_k
```

Therefore every bounded stopping strategy that uses only public information has 90% expected
gross RTP before cent rounding. This includes cashing out at any safe depth or attempting the
entire board. Each possible path settles once, so flooring loses less than one cent on each path
and less than one cent in expectation. At wagers of at least 100 cents this gives strictly above
89% and at most 90% rounded expected RTP. The code uses exact `BigInteger` rational arithmetic;
displayed decimal multipliers never determine wallet credits.

Example: with two mines, R5 wager and 21 gems, the safe cash-out is R225; the next safe multiplier
is 47.5×, and hitting a mine at that stage returns R212.50. The full 23-gem clear returns R250.

## Accepted-round compatibility

The private state freezes both `HouseEdgeBasisPoints` and `PayoutPolicyVersion` at acceptance.
New rounds use edge 1000 and policy 2. Old JSON without a policy defaults to **policy 1**, never to
the new capped curve. Missing historical edge defaults to the original 100 basis points (99%).
Accepted policy-1 rounds preserve their exact uncapped 99% or 90% schedule, original R0.10–R100
wager limits, zero return on a mine, and original payout liability. They must not be silently capped
or repriced. The historical R100 maximums were R514,829,700 at 99% and R468,027,000 at 90%.

Immutable command receipts also retain their original defaults and values. Fresh session metadata
describes policy 2 for the next wager; a resumed old round separately identifies its frozen policy.
After replay, the client reads the current session before allowing input.

## Randomness, privacy and persistence

`SecureMinesBoardGenerator` uses unbiased cryptographic partial Fisher–Yates selection without
replacement. Each subset of `m` positions has probability `1 / C(25,m)`. Sorting those positions
only canonicalizes storage. The board is fixed once, before the first reveal, and never moves.

The private `MinesRoundState` contains unrevealed positions and is never a player API type.
Active projections include only selected gems; every other position is hidden. Terminal views
reveal the board. Ownership comes from the host account cookie, never a submitted player ID.

The account host atomically commits the wallet change, private state, latest-round pointer,
immutable command receipt, financial ledger and terminal history. Replay and competing requests
cannot pay twice. No real wallet is implicitly funded. The root Firestore rules deny every direct
browser read/write; the server uses a separately IAM-controlled SDK. A local emulator privacy
proof tests anonymous, authenticated owner, other-player and forged-admin browser requests.
Live rules/IAM release verification remains a deployment prerequisite.

## Verification

The engine suite independently checks all 300 safe-return positions and all 300 terminal cash-out
strategies, including exact conditional EV, full-board completion, 50× bounds, one-cent rounding,
zero/positive mine returns and unchanged legacy schedules. Server tests cover single payment,
concurrency, historical receipts, authorization, privacy and terminal history. See
[HOST_INTEGRATION.md](HOST_INTEGRATION.md) for source wiring and release status.
