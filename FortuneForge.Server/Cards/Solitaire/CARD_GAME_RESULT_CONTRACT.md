# Solitaire card-game result contract

Competitive Solitaire writes one deterministic document per player and match to
`cardGameResults/{sha256("solitaire\n{matchId}\n{userId}")}` when that player's run becomes
terminal. Free single-player runs are stored separately in `solitaireFreeRuns` and never
write this result record or affect balances.

An unclaimed record has this game-owned shape:

```text
resultId, game="solitaire", mode="competitive", matchId, userId,
currencyId="slotsCredits", claimStatus="unclaimed",
settlementStatus="pending" | "claimable", playerStatus,
score, moves, elapsedMilliseconds, buyInCents, payoutCents,
completedAt, claimableAt?, schemaVersion=1
```

`score` includes a server-calculated speed bonus only when the deck is completed. The bonus
falls by one point for each full three seconds of active play, from 200 to zero over ten
minutes. Unfinished and forfeited runs receive no speed bonus. Ranked play excludes
server-recorded pauses from active time.

The shared history surface may treat `settlementStatus="claimable"` and
`claimStatus="unclaimed"` as a completed item awaiting its explicit claim. Opening that item
should call:

```http
POST /api/solitaire/matches/{matchId}/claim
Idempotency-Key: <16-128 ASCII letters, digits, hyphens, or underscores>
```

The endpoint credits `payoutCents` exactly once, sets `claimStatus="completed"`, and adds
`claimedAt` and `claimIdempotencyKey`. Replaying the same idempotency key returns the current
session without another credit. A different key after completion receives a conflict. Only
The match-history query also includes active matches and claimable, unclaimed results.
Vacant seats remain visible there as "Waiting for players...".

A paid run starts immediately in an eligible open match, or creates one when none has
room. Entrants share a seeded deck but each gets a separate ten-minute gameplay clock
starting on admission. Admission remains open for one hour per configured seat; while
seats remain, at most one managed player joins each hour. A completed player may start
another run before the earlier match settles. Settlement waits until all admitted
runs finish and the match fills or admission closes. If nobody joins the sole human,
their buy-in is returned in full with no platform fee.

New competitive matches use `financialModel="house-funded-bot-pool-v1"`. Every seat adds one
buy-in to the prize pool: a human entry is debited from that human's balance, while a managed
player's equal entry is funded by the house and recorded in `solitaireHouseFunding/{matchId}`.
The winner is ranked across **all** players on the same seeded deal. The prize is 90% of the
full pool and may go to a human or a managed player. A human winner claims the prize through
the endpoint above. A managed-player win is recorded as house revenue; no managed-player
wallet is credited and its result is already completed, not claimable. The settlement writes
`solitaireMatchRevenue/{matchId}` with human and house-funded entries, the human payout or
managed-player prize, the platform fee, and the signed net to the house. Matches created
under the older financial model keep their original settlement rules.
