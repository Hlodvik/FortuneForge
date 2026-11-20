# Security findings and follow-up

Updated 2026-11-19. Findings below come from the isolated local API, its exported
OpenAPI schema, and disposable scanner fixtures unless a live-site result is
explicitly identified. No finding here establishes a production compromise.

## Changes already applied

- [x] Correct the disposable scanner fixture's unsupported-method handling.
  `tools/security/tests/fixture_server.py` now returns 405 with `Allow` instead of
  Python's default 501. Four contract tests passed; Schemathesis tested all three
  fixture operations with **512 generated cases passing**. Fortune Forge itself
  already returned 405 for TRACE on `/api/accounts/me`; no application TRACE fix
  was required.
- [x] Apply stake range validation before cents arithmetic in Baccarat, Sic Bo,
  and Casino War. Source and regression-test changes were already applied before
  the request to switch to documenting findings. Their internal settlement
  conversions retain checked arithmetic.
- [x] Verify those three applied stake-validation changes.
  Before the change, an oversized numeric stake returned HTTP 500 in all three
  games. The disposable wallet remained R10,000.00 and had zero financial ledger
  entries. Twelve regression theory cases were added for extreme decimals,
  including Baccarat pair stakes and Casino War tie stakes. All **34 focused
  contract tests pass**, including the twelve new cases. Local HTTP rechecks now
  return **400 for all three games**; wallet remains R10,000.00 with zero ledger
  entries. Changes are not deployed.

## Confirmed findings left open

- [ ] Reject oversized Keno wagers as validation errors, before numeric
  conversion. Local `/api/games/keno/rounds` returned HTTP 500 for an oversized
  amount. Owner: `FortuneForge.Server/Numbers/Keno/KenoModels.cs`,
  `KenoMoney.ToWagerCents`. No fix applied.
- [ ] Reject oversized Blackjack wagers before numeric conversion. Local
  `/api/cards/blackjack/games` returned HTTP 500 for an oversized amount. Owner:
  `FortuneForge.Server/Cards/Blackjack/BlackjackModels.cs`,
  `BlackjackMoney.ToWagerCents`; also review callers in table games. No fix applied.
  After all five oversized-request reproductions, the emulator wallet remained
  R10,000.00 and `balanceTransactions` remained empty. This demonstrates input
  handling failures, not balance corruption.
- [ ] Correct incomplete OpenAPI response declarations. The first application
  Schemathesis run found **10 undocumented-status failures** for legitimate
  400/401/415 responses while the schema declared only 200. These are contract
  findings; rejecting an invalid/unauthorized request is expected behavior.
  Affected operations:
  - `POST /api/accounts/login`
  - `POST /api/admin/game-reports/{id}/status`
  - `POST /api/admin/management/bots`
  - `POST /api/admin/management/games/{gameId}/bots`
  - `POST /api/admin/management/games/{gameId}/visibility`
  - `POST /api/admin/risk-incidents/{id}/resolution`
  - `POST /api/admin/security-audit/investigations/pair-analysis/{id}/resolution`
  - `POST /api/admin/security-audit/investigations/{id}/resolution`
  - `POST /api/cards/blackjack/games`
  - `POST /api/cards/blackjack/games/{gameId}/actions`

## Candidates requiring reproduction

- [ ] Check Three Card Poker's incoming wager conversion for the same overflow
  pattern. Owner: `Cards/ThreeCardPoker/Table/ThreeCardPokerMoney.cs`.
  This is source-review evidence only; an HTTP failure has not been reproduced.
- [ ] Check the restricted-login success response against its declared schema:
  it can intentionally return `account: null` with restriction details.
- [ ] Verify whether GET/HEAD requests to POST-only API routes return the SPA
  fallback rather than a method error. No authorization bypass has been shown.

## Test isolation and coverage

- [ ] Ensure local signup/verification tests use a stub or emulator-aware
  Firebase email client. Current email-verification code has direct Google URLs;
  a Firestore emulator does not reroute those calls. Those operations were
  excluded from the local scan.
- [ ] Expand the application scan beyond its first failure limit. The exported
  API has 235 operations; 14 purchase/auth-session operations were excluded.
  The first run tested **21 of 221 selected operations**, generated 39 cases,
  and stopped at 10 contract failures before fuzzing. This is not full API or
  wallet coverage.
- [ ] Account for expected rate limiting when scheduling additional batches.
  HTTP 429 is protective behavior and can limit useful scan coverage.
- [ ] Add separate multi-user authorization, wallet conservation, concurrent
  wager, and settlement-replay checks. Generic scanner success does not verify
  these financial invariants.
- [ ] Run a production-equivalent container/dependency assessment; the completed
  Trivy source scan reported no HIGH/CRITICAL findings but did not cover a built
  production image or all .NET runtime dependencies.

## Explicitly deferred

- [ ] Buying credits, checkout, purchase invoice, mock checkout, and payment
  webhook work: deferred by the owner. Do not implement fixes in these flows as
  part of this task. Wallet balances, wager deductions, payouts, and duplicate
  transaction handling are distinct concerns, now recorded for follow-up.

## Evidence

- Corrected fixture: `artifacts/security/schemathesis-20261119-000304-9b3a8752/`
- First application scan: `artifacts/security/schemathesis-20261119-000505-75e8e63e/`
- Local API schemas, exclusions, and pre-fix reproduction:
  `artifacts/security/fortuneforge-20261119-000052/`.
- Trivy source baseline: `artifacts/security/trivy-20261118-233813-2af49081/`

Reports and scanner state are Git-ignored and may contain request/response data.
