# Andar Bahar rules engine

This package implements the fixed **Andar first** single-deck variant. It uses the shared
`FortuneForge.Games.Cards` primitives and contains no wallet, transport, client randomness,
or persistent storage. The server supplies a fresh cryptographically shuffled deck for every
round, locks the wager, and atomically records settlement.

## Rule provenance

The primary rule reference is Pragmatic Play's **Andar Bahar Game Help, EN V1.2**, published
by the operator at [Andar Bahar game help](https://yesplay.bet/assets/documents/pragmatic-play-andar-bahar-rules.pdf).
The provider document's indexed **How to Play**, **Card Values**, and **Main Bets** sections
were checked on October 20, 2026. Direct CDN retrieval was unavailable in this environment.

The selected rules are:

- One standard 52-card deck, without printed jokers.
- A central card called the Joker supplies the target rank.
- Deal alternately to Andar, then Bahar, always starting Andar regardless of Joker color.
- Stop immediately when a dealt card matches the Joker's rank. Suits do not affect this bet.
- Andar wins pay **0.9:1 profit**; Bahar wins pay **1:1 profit**, plus the returned stake.
- Shuffle a complete deck for each new round. No continuation shoe or side bets.

The provider procedure reveals the Joker before betting closes. A host controls the betting
window and must document when it reveals the central card. That timing does not change the
conditional side probabilities because they are identical for all 52 possible central cards.

The reference's introductory "50/50" phrase is an approximation; the implementation uses
the exact probabilities below. No provider visuals, marks, or assets are included here.

## Exact payout mathematics

After removing any particular Joker, exactly three matching-rank cards remain in 51
positions. The possible three-position subsets are equally likely, and there are
`C(51,3) = 20,825` of them. If the first match is in position `k`, the other two matches
can occupy `C(51-k,2)` later-position subsets. The valid range is **1 through 49**:
only 48 cards can fail to match the Joker, so the next must match. Positions 50 and 51
cannot be the *first* match. There is exactly one winning side and no tie or push.

Odd positions belong to Andar; even positions belong to Bahar. Summing these counts gives:

| Side | Winning subsets | Exact win probability | Profit payout | Return incl. stake | Exact RTP | House edge |
| --- | ---: | --- | --- | --- | --- | --- |
| Andar | 10,725 | 429/833 = 51.5006002401% | 9/10 | 19/10 | 8151/8330 = 97.8511404562% | 179/8330 = 2.1488595438% |
| Bahar | 10,100 | 404/833 = 48.4993997599% | 1 | 2 | 808/833 = 96.9987995198% | 25/833 = 3.0012004802% |

RTP is `win probability × total return multiple`. A losing wager returns zero. These
figures are derived independently from the rules, rather than copied rounded paytable values.
The appropriate rounded UI figures are **97.85%** and **97.00%**.

All monetary amounts are `long` integer minor units named `Cents`. Both sides require
nonnegative multiples of **10 cents** so the Andar 9/10 payout has no rounding policy.
The host sets minimum/maximum limits and rejects an empty wager before creating a paid round.
For example:

| Wager | Winner | Stake | Returned | Net profit/loss |
| --- | --- | ---: | ---: | ---: |
| Andar | Andar | 250 | 475 | +225 |
| Bahar | Bahar | 250 | 500 | +250 |
| Andar | Bahar | 250 | 0 | -250 |

Every aggregate and payout uses checked arithmetic. A payout whose exact result exceeds
`long.MaxValue` is rejected. Division occurs before the 9/10 multiplication, allowing every
representable exact return without intermediate overflow.

## Engine contract

`AndarBaharRules.Deal(fullDeck)` accepts exactly 52 unique standard cards in **top-card-first**
order. It validates the entire deck, including cards beyond the winning match. It snapshots
input and returns an immutable `AndarBaharRound` with `Joker`, ordered `Deals`, `Winner`,
`WinningCard`, and `CardsDealt`. Each `AndarBaharDeal` has one-based `Number`, `Side`, and `Card`.
The round excludes all unrevealed tail cards.

`Settle(side, stakeCents, round)` returns `AndarBaharSettlement` with `StakeCents`, `Disposition`
(`Win`, `Loss`, or `NotPlaced`), `TotalReturnCents`, and `ProfitCents`. A zero stake is
`NotPlaced`; an invalid side is rejected even at zero. `Settle(wagers, round)` validates both
stakes and aggregate overflow, then returns both settlements. It supports both sides without
changing either side's payout. Wagers are locked before evaluation; a host must never accept
client-supplied outcomes or use this public result as permission to debit a wallet.

Animation pace, skip, replay, and refresh are presentation operations. They must never call
for a new shuffle or change the durable outcome or settlement.

## Verification

From the repository root, without any central solution changes:

```powershell
dotnet test games/AndarBahar/tests/FortuneForge.Games.AndarBahar.Tests/FortuneForge.Games.AndarBahar.Tests.csproj --configuration Release --filter FullyQualifiedName~AndarBaharEngineTests
```

The engine suite exhaustively tests all **20,825** matching-position subsets and all
**52 × 49** Joker/first-match-position pairs. It also verifies exact probability and RTP
fractions, fixed starting side, chronological order and stop, the 49-card bound, no tie,
input/result immutability, invalid/duplicate cards even in the hidden tail, exact cents,
stake increments, checked limits, returned stakes, losing wagers, zero wagers, and module routes.
