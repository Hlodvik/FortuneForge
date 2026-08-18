# Solitaire card-game result contract

Competitive Solitaire writes one deterministic document per real player and match to
`cardGameResults/{sha256("solitaire\n{matchId}\n{userId}")}` when that player's run becomes
terminal. Free single-player games are client-only and never write this record.

An unclaimed record has this game-owned shape:

```text
resultId, game="solitaire", mode="competitive", matchId, userId,
currencyId="slotsCredits", claimStatus="unclaimed",
settlementStatus="pending" | "claimable", playerStatus,
score, moves, elapsedMilliseconds, buyInCents, payoutCents,
completedAt, claimableAt?, schemaVersion=1
```

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
records with `claimStatus="completed"` appear in Solitaire's completed-history query.
