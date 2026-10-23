# Player administration data contract

The admin player portal is read-only. Register `AddAdminPlayers(configuration)` after
`AddAdminOperations(configuration)`. It shares `Features:AdminOperationsEnabled`,
the server-resolved administrator role, the existing read rate limit, the validated
`AdminOperations:CursorSigningKey`, and sanitized access audit. No payment-provider
requests, financial reconciliation, ledger edits, refunds, or balance changes occur.
Responses are `Cache-Control: no-store`. Never cache player detail across accounts.

## API

- `GET /api/admin/players?query=&limit=25&cursor=`: exact immutable user ID,
  exact case-normalized email, or case-insensitive player-name prefix. Bots are
  explicitly identified from the shared `profileTags` convention. Legacy profiles
  missing `normalizedPlayerName` are not covered by prefix search.
- `GET /api/admin/players/{userId}`: selected immutable identity, current balances,
  bounded purchase and gaming cash-flow summary, and source coverage limitations.
- `GET /api/admin/players/{userId}/purchases?limit=25&cursor=`: sanitized local
  invoice snapshots, including their mock indicator and recorded status.
- `GET /api/admin/players/{userId}/gaming?limit=25&cursor=`: posted Rand wallet
  events. Funding, withdrawals, and non-Rand currencies are excluded. Legacy events
  without a match ID retain `matchId: null`; links are never guessed from request IDs.
- `GET /api/admin/players/{userId}/matches?limit=25&cursor=`: safe match summaries
  from `cardGameResults`, followed by `heartsPaidHistory`. The latter exposes only
  player names/identities, entry/payout amounts, status and timestamps, never hands,
  decisions, seeds, state JSON, or command guards.

All pages accept limits 1–100. Cursors are HMAC-signed and bound to the endpoint,
user ID and search query, not transferable between players or history types.
The DTO definitions are in `FortuneForge.Server/Admin/Players/AdminPlayersModels.cs`.

## Ordering and boundedness

Histories use `userId == selectedUser` and immutable document-ID ordering, which
uses Firestore's existing single-field indexes and requires no new composite index.
It is **not chronological ordering**. Timestamps are shown when recorded. Match
pages exhaust `cardGameResults` before `heartsPaidHistory`. A wallet page can be empty
while `nextCursor` is non-null because funding or non-Rand records were filtered out.

The summary reads at most `min(AdminOperations:MaximumDocumentsPerCollection, 1000)`
documents per source, plus one truncation sentinel. `recordsRead` and `scope:
"bounded-records"` describe that scan. A truncated scan is never labelled a lifetime
total. The maximum is a hard read-cost bound, not evidence of how many games exist.

## Money and completeness

- Prefer authoritative integer `amountCents`/`balanceAfterCents` where present.
  Legacy `amount`/`balanceAfter` values are Rand, **not cents**; reject subcent or
  invalid numeric values rather than truncating them.
- Current Rand balance is `available + availableFractionalCents / 100`. Missing or
  invalid balances stay unknown, not zero. Non-money counters use their own units.
- `purchaseCredits` counts completed **non-mock** purchases; `mockPurchaseCredits`
  separately identifies test credits. These are credits funded, not gateway fees,
  booked revenue, or a verified total of external cash received.
  Missing or invalid `isMock` classification is unknown, excluded from both totals,
  and identified by the history row's `mockClassificationKnown: false` and coverage.
- Gaming net is signed posted wallet cash flow. Open stakes can make it negative
  before a hand settles. Refunds affect net but are separately identified by
  `gamingRefundedCredits`; they are not winnings. This is **not settled player loss
  or house profit**. Deposit and withdrawal events are not gaming.
- Unknown ledger types/malformed money remain visible but are excluded from gaming
  totals, with `complete: false`. No totals are extrapolated from one displayed page.
- Generic card results currently cover Blackjack, Texas Hold'em, Three Card Poker,
  House Hold'em and Solitaire. Hearts has a separate paid history. Other game results,
  older data, free play, and in-memory games are not a complete match history. The
  coverage response explicitly reports this. Do not add match totals to wallet totals:
  the two views often describe the same funds.

The existing operations overview also covers only its explicitly supported revenue
collections. Its `complete` flag does not establish catalogue-wide or per-player
financial completeness. Normalized cross-game settled-result indexing is a separate
future migration, not something this read-only portal silently manufactures.
Its completed real funding totals exclude simulated payments; optional
`completedMockPurchaseCredits`/`completedMockPurchases` and
`completedMockWithdrawalCredits`/`completedMockWithdrawals` report those separately.
