# Game analytics and audit handoff

Snapshot: 2026-10-26. Repository: `C:/Users/Lctiv/source/repos/FortuneForge-consolidated`, shared local `main`, base HEAD `b1f75a8f64ccdde70d22fc5e460c42a623bedfb7`. The tree contains substantial pre-existing work. **The analytics, evidence, risk controls and admin screens described below are local changes, not pushed or deployed.** This document describes implementation and explicit gaps, not a production attestation.

## Architecture and trust

ASP.NET Core host (`FortuneForge.Server`, .NET 10), shared game engines/packages in `games/`, React/TypeScript host in `fortuneforge.client`, Google Cloud Firestore through the server SDK. Firestore rules deny direct browser access. Browser clients send commands to authenticated server endpoints; paid settlement and wallet changes are server transactions. Account-backed versus bot classification comes from server state. Clients cannot create analytics settlement receipts, risk incidents or private evidence directly.

Existing data includes `users`, `userBalances`, `balanceTransactions`, game-specific current-round/table documents, card/game results, payment/withdrawal records and account sessions. These are separate collections, not relational SQL tables. User association is by `userId`/actor ID; a table ID can remain a historical reference after closure but must not permit new gameplay. Hold'em lifecycle guards reject closed/expired tables and clean stale active pointers without erasing history.

New authoritative paid sources write an analytics outbox in the SAME Firestore transaction as settlement. Multi-player sources write one receipt per actual human participant. Bots do not inflate human counts. Practice/free engines use a separate sink; synchronous in-memory engines have a bounded owned queue and do not provide crash-durable delivery. Do not use practice scores as money.

## New storage and relationships

* `gameAnalyticsSettlementOutbox/{hash}`: stable hash of game + mode + round + user. `schemaVersion`, `gameId`, `roundId`, `roundKey`, `userId`, `mode` (`paid`, `free`, `practice`), `isHuman`, server `startedAt`/`completedAt`, nullable `wagerCents`/`payoutCents`, `sourceCollection`/`sourceDocumentId`, `pending`. Projection quarantines malformed records. Amounts are integer cents, gross wager and returned amount; profit/loss is derived, not a balance delta.
* `gameAnalyticsParticipations/{hash}` and `gameAnalyticsRounds/{roundKey}`: idempotency receipts. Prevent retried/concurrent projections from counting twice; unique shared rounds differ from participant rounds.
* `gameAnalyticsBuckets/{scope}:{period}`: global or hashed-user scope, UTC day/week/month/all-time, totals plus per-game map. Contains human/unique/paid/free/practice/known-money/unknown-money rounds, wager/return/net-win/net-loss cents and observed active milliseconds. Week starts Monday. Reads are bounded document reads, not financial-ledger scans. An idle projector polls every 30 seconds, switches to five seconds while busy, and immediately drains full batches; totals are eventually consistent. `gameAnalyticsMetadata/tracking` records start; no invented historical backfill.
* `gamePlayerPresence/{hash(userId)}`: one per account; canonical `gameId`, foreground client session/lease identifier and server expiry. Client heartbeat ~25s, lease 90s. Observed intervals are durably buffered on the presence record, then projected into period totals roughly every five minutes or on leave/game switch; a due-only worker recovers pending intervals after a restart. Distinct active foreground accounts, not table seats. Time is observed foreground occupancy, not proof of focused human play. Stale tabs cannot clear a newer lease. Expired leases are excluded from counts.
* `gameRoundAudits/{hash(gameId+roundId)}`: compact immutable settlement identity and summary plus mutable audit/retention metadata. `id`, `gameId`, `roundId`, `tableId`, `roundNumber`, start/end UTC, `auditStatus` (`passed`, `flagged`, `incomplete`), findings `{code,severity,message}`, participants `{actorId,displayName,isAccountBacked,wagerCents,payoutCents,netCents}`, `auditorVersion`, `evidenceKind`, SHA-256, evidence presence/expiry/retention state. Hash IDs prevent a new hand overwriting the previous hand.
* `gameRoundEvidence/{same id}`: private `payloadJson`, SHA-256, capture time, schema/kind metadata. Created atomically, never overwritten on next deal. The large JSON and unqueried summary arrays are excluded from Firestore indexing in local index configuration. Only admin evidence endpoints return payloads.
* `playerRiskIncidents/{hash(user+sourceKind+sourceIncidentId)}`: explicit attributed actor, game/round, severity (`possible`, `high`, `critical`), source identity, rule/version, attribution basis, evidence IDs, created-by/time, status, resolution actor/time/outcome/reason and prior review history. Rechecking the same incident does not add strikes. Evidence must exist and identify that human participant. The `contributesToRestrictions` query field excludes cleared cases from current review queries. Admin review can explicitly correct severity or reverse an earlier decision on the same incident; prior/new severity, outcomes, reviewers and reasons remain in `Reviews`. Upholding a finding does not automatically promote its severity.
* `playerRiskStates/{hash(userId)}`: per-game severity counts and derived account/withdrawal/game restrictions; version and policy version. Updated atomically with incident creation/review and evidence holds. User-linked incident queries keep evidence attached to the account logically without putting large payloads in `users` documents.

Illustrative redacted receipt:

```json
{"gameId":"texas-holdem","roundId":"table-redacted-hand-4","userId":"user-redacted","mode":"paid","isHuman":true,"wagerCents":500,"payoutCents":1200,"sourceCollection":"gameRoundAudits","sourceDocumentId":"audit-hash-redacted","pending":true}
```

Illustrative evidence summary:

```json
{"gameId":"texas-holdem","roundId":"table-redacted-hand-4","auditStatus":"passed","auditorVersion":"holdem-integrity-v1","evidenceKind":"holdem-hand-v1","evidencePresent":true,"participants":[{"actorId":"user-redacted","isAccountBacked":true,"wagerCents":500,"payoutCents":1200,"netCents":700}],"findings":[{"code":"audit-scope","severity":"info","message":"Integrity checks do not determine intent or collusion."}]}
```

## Reconstruction coverage

Texas Hold'em: capture before table rotation includes complete stored deck and `DealSeed`, hole cards, community/burn position, dealer/seat mapping, table rule ID, player starting/ending stacks, committed amounts, awards, cash/account payouts, escrow tickets, reveal status, leaving actors, accounting totals and ordered action log. Actions use sequence/hand/seat identifiers; inspect the engine action type for exact timing availability. Existing raw evidence is unavailable for hands overwritten before this change. The retained seed reproduces the implemented engine shuffle; it is NOT a signed RNG attestation or cryptographic fairness commitment.

Blackjack/Three Card Poker: final private card/bet/payout snapshots and per-human settlement, now with round start metadata. Earlier decisions were not fully retained, so these captures explicitly remain `incomplete`; no claim of full historical action replay. The older single-player `/api/cards/blackjack/games` path also captures natural deals and terminal actions atomically, using total committed wager (including double) and gross payout. Its private snapshot includes the full deck, resulting player/dealer cards and terminal action, but not the earlier complete command timeline, so it remains incomplete too.

Single-player paid adapters: Baccarat, House Hold'em, Video Poker, Casino War, Keno, Sic Bo, Dragon Tiger, Andar Bahar, Hi-Lo, Mines and the shared slot settlement path capture source result/private state plus exact financial receipt. Payload depth varies. Hi-Lo retains guesses/history, Mines retains private round board/state, House Hold'em retains steps. Slots capture result, outcome schema version, free-spin/feature metadata and settled return including energy multiplier; they do not capture a complete pre-spin guard/configuration snapshot. Not all engines retain a replay seed, PRNG version, intermediate command timing or full per-action log. `SinglePlayerRoundRecord` defaults to an incomplete receipt, not a passed audit. Baccarat, Dragon Tiger, Andar Bahar and Mines now pass only after their named independent outcome checks below; the other generic receipts remain incomplete and held.

Hearts/Solitaire paid settlement and all worker completion paths emit analytics receipts; 2048/Drop Merge/Craps/Roulette/Liar's Dice and free Hearts emit explicit no-credit completion events. Their analytics integration does not imply new full private action-log evidence for every game. Their unit of completion differs: financial match for Hearts/Solitaire, resolved wager round for Craps/Roulette, completed run for puzzles. Presence covers all 40 catalogue routes.

## Checks already implemented locally

`RoundAudit/HoldemRoundAudit.cs`: deal-seed/deck consistency, valid unique dealt cards, hole-card deal order, community/burn sequence, action sequence/seat/hand IDs, allowed action amounts, wager-action reconciliation, stack/pot conservation, independently recomputed eligible side-pot/hand awards and account totals. **Not yet full street-by-street legal action replay, intent, collusion, multi-account or bot detection.**

Named single-player outcome checks are run before the same settlement transaction captures the receipt. They compare the actual gross settlement amounts to separate calculations, rather than calling the production payout functions again. A mismatch flags retained evidence; these checks do not alter the game's payout or accuse an actor.

* `mines-outcome-v1`: exact rational survival and accepted payout-policy arithmetic, including the capped linear tail and mine consolation; valid board/reveal sequence, terminal status, command version count and actual terminal wallet credit. It supports accepted legacy schedules and the current R1–R5/50× policy. It verifies recorded outcomes, not whether RNG generation was unbiased or a client knew the private board beforehand.
* `dragon-tiger-outcome-v1`: card validity/rank comparison, even-cent wager accounting, 1:1 wins, half-loss returns on ties, 8:1 tie bets and shoe-position bounds. Same rank/suit cards can legitimately repeat in the eight-deck shoe. This does not reconstruct the original shuffled shoe.
* `andar-bahar-outcome-v1`: distinct standard cards in the visible deal, numbered alternating sides, first rank match, winner, separate Andar/Bahar return rates and recorded settlement balance delta. The remaining hidden deck is not retained in this snapshot, so this is a dealt-sequence audit rather than full shuffle reconstruction.
* `baccarat-outcome-v1`: consumed-card order, natural/third-card tableau, final totals/outcome, main wager disposition, rounded banker commission and independent pair bets. This validates the dealt cards and payment; it does not prove the ordering of the entire live shoe.

Focused checks have covered all 169 Dragon/Tiger rank pairs; all 49 possible Andar/Bahar first-match positions for both bets; 1,000 Baccarat initial-total/third-card combinations for all three main wagers; and every Mines terminal path across all 24 mine counts and five accepted policy/wager combinations. Concurrent Baccarat and Mines emulator tests verify one audit/evidence/outbox with the exact gross wallet amounts under retries. These checks are proportional outcome/settlement tests, not a production certification of every game.

`RoundAudit/GameRoundAuditStore.cs`: second-pass fingerprint + participant accounting validation, held incomplete/flagged evidence, bounded transactional deletion eligibility with final recheck. SHA-256 detects accidental mutation relative to the stored fingerprint; an administrator/server credential can rewrite both. This is not append-only/WORM or externally signed tamper-proof storage.

`Risk/`: attribution, stable incident deduplication, severity aggregation, atomic evidence holds and reversible admin decisions. Raw integrity findings NEVER directly accuse a player. An attributed rule detector or explicit admin review must create a risk incident. Critical is labelled pending review, not automatically confirmed guilt.

Policy: 3 possible in one game block that game; any high blocks withdrawals; 2 high in one game block that game; 2 high in EACH of 2 different games restrict the account; critical restricts account. Account restriction allows only authentication/logout/minimal review status, with no balances or game access. High flags are disclosed only on withdrawal attempt. Server middleware enforces API access; withdrawal reservation checks state in its financial transaction. Enforcement changes do not cancel already-authorized provider webhooks or erase history.

## Retention decisions authorized by owner

Clean, completed audit: detailed evidence eligible seven days after processing. Unresolved flags/incomplete audits: indefinite hold. Admin-cleared incident: 30 days from resolution, with maximum deadline if shared across cases. Admin-confirmed cheating: preserve indefinitely linked to account. Another unresolved/upheld case overrides deletion. Summaries, financial ledgers, statistics, incident reasons and decision histories remain. Ordinary browser users cannot see private decks/seeds.

The deletion worker removes only the dedicated `gameRoundEvidence` payload. It does NOT yet purge all old game-specific operational round/result documents; those may still be required for recovery/idempotency. Therefore this is not a guarantee that every copy of raw card details has been removed or that existing storage cost is eliminated. No production TTL policy has been enabled.

## Missing inputs for broader cheating analysis

No new IP, device fingerprint, geo, cross-account device graph or durable session/network identity is captured in these evidence records. Server auth/session and request-rate infrastructure exists separately; do not assume those logs are retained or joined. Presence session IDs are browser-provided and unsuitable as strong identity evidence. Per-command server timestamps, rejected attempts, request-id correlation, rules/build/PRNG revisions and seeds are not uniform across games. No universal replay stream, statistically validated collusion detector, or independent RNG provenance system is implemented. A detector needs evidence of actor-controlled behavior before requesting punitive incidents; anomalous payouts/decks can indicate a platform bug.

## Code entry points

* `games/shared/FortuneForge.Games.Abstractions/GameAnalyticsSettlement.cs`, `RoundAuditModels.cs`, `SinglePlayerRoundRecord.cs` — versioned shared receipts/capture.
* `FortuneForge.Server/Activity/GameAnalyticsContracts.cs`, `FirestoreGameAnalyticsStore.cs`, `GameAnalyticsWorker.cs`, `GamePresenceController.cs` — aggregates/presence.
* `FortuneForge.Server/RoundAudit/{HoldemRoundAudit,TableRoundAudit,SinglePlayerRoundRecording,GameRoundAuditStore}.cs` — audits/evidence/retention.
* `FortuneForge.Server/Risk/` — policy, storage, access enforcement/review API.
* `FortuneForge.Server/Admin/GameAnalytics/` — authenticated/admin-only statistics, private round evidence, incident creation/review.
* `fortuneforge.client/src/pages/admin/portal/{AdminGameAnalyticsPanel,AdminRoundReviewsPanel}.tsx` — admin views; player profile embeds per-user metrics/incidents.
* `FortuneForge.Server.Tests/{Activity,RoundAudit,Risk}/` and existing paid-game emulator suites — tests; run against local emulator only.
* `firestore.indexes.json`, `firestore.rules` — local query indexes, large-payload exemptions, denied direct client database access.

Automated checks cover analytics, restrictions, admin access, outcome verification, retention and client workflows. Browser visual checks could not initialize: both supported computer-use runtimes failed with a missing kernel-assets path. No visual QA is claimed. Final integration: 95 tests passed in the combined run; its four failures were corrected, and all 11 affected tests then passed (including two additional retry/reset cases). Separate focused outcome and client checks also passed, with TypeScript clean. Casino War completed-opening reload and post-decision opening retry were corrected without changing payouts. The feature is local and not deployed; complete replay auditing is not available for every game.
