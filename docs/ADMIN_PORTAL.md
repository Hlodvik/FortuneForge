# Admin portal, first version

The authenticated `/admin` route is restricted to the existing server-managed
administrator role. The shared account menu links to it only for admins, but that
UI check is not the security boundary: every private API independently resolves
the session and checks the role. No new users or admin grants are created.

## Included

- Existing operations overview: explicitly supported game revenue sources,
  funding, integrity and queue/match telemetry. Real and simulated funding are
  separated. This is not catalogue-wide financial accounting.
- Player search by immutable ID, exact email or name prefix; current balances;
  purchases, recorded gaming cashflow, and supported match histories. Records are
  paginated, sanitized, and deliberately label unknown values and incomplete
  coverage. See `ADMIN_PLAYER_HISTORY.md` for exact semantics.
- Catalogue hide/unhide for the release-owned game allowlist. Hiding changes
  discovery, not access control: direct links and existing matches remain usable.
  Horse Flight remains hidden by default. Unhiding does not certify a game ready.
  All-games, card/slot libraries, demos and home-page promotions use the same
  public visibility snapshot. Open catalogues refresh on focus, every 30 seconds
  while visible, and immediately on an admin change in the same browser.
- A master bot-admission switch plus per-game switches. Turning off blocks new
  bot reservations atomically, but does not terminate matches or interrupt active
  bot turns, leases or settlement. Practice modes requiring bots can report
  unavailable; paid multiplayer waits for the required humans.
- Administrative confirmations require a reason and are persisted atomically with
  the setting, actor hash, before/after values, timestamp and version. Writes use
  idempotency receipts and optimistic version checks. Retries after an uncertain
  response reuse the same key and payload.

## Configuration and deployment

This portal work is separate from the Hearts release. Do not publish the dirty
development tree; use the immutable-commit release workflow in `RELEASE_SAFETY.md`.

- Player reads use `Features:AdminOperationsEnabled` and the existing
  `AdminOperations:CursorSigningKey` (at least 32 characters).
- Management reads/writes additionally require
  `Features:AdminManagementEnabled`, default **false**.
- `AdminManagement:CursorSigningKey` must contain at least 32 characters when
  management is enabled. If omitted, it falls back to the configured operations key.
- Cookie-authenticated writes require JSON, `X-FortuneForge-Admin: 1`, a bounded
  `Idempotency-Key`, and an accepted Origin. The Firebase origins and true same
  origin are accepted. Configure explicit local frontend origins for development
  proxies; never weaken production origins to a wildcard.
- Public visibility and persisted bot-admission settings remain effective even
  while the management interface is disabled. No runtime switch changes are made
  merely by deploying the portal.

The management state is `adminRuntimeConfiguration/current`; immutable audit rows
are in `adminManagementAudit`; request receipts in `adminManagementIdempotency`.
There are no balance, refund, payout, permission-grant or account-deletion actions
in this version. Existing invoice tools remain linked separately.

## Management API

| Method and route | Purpose |
| --- | --- |
| `GET /api/admin/management` | Current revision, master bot switch and allowlisted game settings. |
| `POST /api/admin/management/bots` | Change the master bot-admission switch. |
| `POST /api/admin/management/games/{id}/bots` | Change one supported game's bot-admission switch. |
| `POST /api/admin/management/games/{id}/visibility` | Hide or unhide one catalogue entry. |
| `GET /api/admin/management/audit` | Paginated setting-change history with opaque actor IDs. |
| `GET /api/catalog/visibility` | Public game IDs, routes and visibility only; no bot settings or audit data. |

Bot writes send `{enabled, expectedVersion, reason}`; visibility writes send
`{visible, expectedVersion, reason}`. The reason must contain 10–500 characters
without control characters. Each write requires an `Idempotency-Key` of 16–128
ASCII letters, digits, dots, colons, dashes or underscores. Keep the same key and
payload when retrying an uncertain response. A successful write returns the
management state; a stale revision or mismatched reused key returns HTTP 409.
Refresh settings before making a different change.

The audit endpoint defaults to 25 rows and accepts `limit` from 1–100. Pass its
signed `nextCursor` unchanged to retrieve the next page. Reads and writes use
`Cache-Control: no-store`; writes have a separate limit of 30 per minute and do
not consume the payment-write budget.

The release allowlist contains 40 existing games, including the default-hidden
Horse Flight. Runtime writes cannot add routes, assign bots to unsupported games,
or change payouts. Global bot-off preserves individual game preferences for when
the master switch is turned back on. If disabled during a reservation batch, the
whole incomplete supply is canceled. With the optional provisional protocol enabled,
its provisional leases are released. Completed
reservation batches may finish their arrival. Cleanup cannot remove leases already
committed by another caller for the same table. Active opponents continue their
normal turns and can renew or release their leases. Without new bots, Hold’em
waits for its existing three-player minimum, paid Hearts waits for four players,
and a new practice Hearts or Liar’s Dice table reports HTTP 503 rather than
opening an incomplete game.

### Reservation-protocol release gate

`ManagedPlayers:ProvisionalReservationBatchesEnabled` defaults to `false`.
Compatibility mode uses direct reservations and creates no provisional leases.
If admissions stop mid-batch, it returns no opponents but does not delete potentially
shared same-table leases: unused partial reservations expire within their original
30-minute lease. This bounded compatibility limitation avoids disrupting an active
cohort in another request.

The opt-in provisional batch protocol is not safe alongside older server binaries that
reserve same-table leases without committing the new metadata. Before a future
admin release, stop new matchmaking and drain all reservation-capable requests
and older workers/revisions before setting the flag to `true`. A bot-off switch
alone does not drain an old revision that does not implement that switch. Verify
that no older revision can receive requests before restoring matchmaking. Do not
perform an overlapping rolling deployment or roll back while provisional leases
exist. Existing pre-protocol lease documents remain readable and are treated as
committed, so active opponents can continue on the new binary.

## Local sample preview

With the frontend Vite development server running, open `/admin-preview.html`.
It imports development-only sample fixtures and never calls private production
APIs. It is not an authentication bypass or a production build entry. The banner
always identifies the sample-data environment. Real player data and runtime
switches must be tested through the authenticated `/admin` route with local
emulator data before a separate admin release.

## Deliberate follow-ups

Before a production admin release, verify the active Firestore client security
policy also denies normal authenticated clients direct access to administrative
configuration, audit/idempotency data and private player collections. Read-only
preflight found no Firebase Rules releases and confirmed HTTP 403 for
unauthenticated reads of those collections; it did **not** prove authenticated
client-write denial. No client writes, rule deployments, permission grants or
production control changes were attempted during this implementation.

Normalized settled results across every game, chronological cross-source histories,
per-player withdrawal history, financial exports and configurable read/write roles
require additional data work. Do not call the existing bounded wallet cashflow a
lifetime loss total, and do not sum match payouts with wallet payouts: they often
represent the same money. Legacy missing data is not fabricated or backfilled by
this portal.
