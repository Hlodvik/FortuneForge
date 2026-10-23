# Hi-Lo verification

Verified on October 20, 2026, in the existing main working tree. Package ownership is limited to `games/HiLo`; central integration is documented in `INTEGRATION.md`. No root dependency install, branch/worktree operation, commit, push, deployment, live configuration change or production wager was performed.

## Automated checks

- **Engine: 35 passed.** Every rank × direction × 52-card next draw is checked. Tests verify ties lose, impossible ace/king choices, independent replacement odds, 1% conditional edge, cap-aware exact returns, cent flooring, 52 skips, 100 wins, terminal actions and JSON resume. Two thousand varied winning paths are compared against an independent `BigInteger` rational payout calculation, rather than duplicating decimal engine arithmetic. The 300-credit wager with recurring cap multiplier pays exactly 100000 credits.
- **Server: 20 passed, zero skipped.** Six tests use a real private Firestore emulator. Coverage includes atomic wallet debit/credit/journal/event updates, duplicate and concurrent actions across store instances, stale revisions, insufficient funds, exact response replay after restart, private account/round ownership, fractional wallet preservation, auto-cap settlement, equal-rank loss, corruption rejection, CSRF/origin/authentication, sanitized retry errors and maintenance cashout. Practice file tests exercise durable reload, exclusive locking and atomic state/operation storage. Detailed local TRX: `tests/FortuneForge.Games.HiLo.Server.Tests/TestResults/hi-lo-server.trx`.
- **Client: 64 passed, zero skipped.** Final machine-readable results are in `evidence/client-tests.json`. Typecheck and package build also pass. Tests cover lost start and cashout responses followed by refresh, identical-key retry, rejected/stale requests, invalid/insufficient wagers, account/mode isolation, late responses from a prior account, rules focus, mute and maintenance. A caught scope-reconciliation race and a maintenance recovery trap were fixed with regression coverage. Sound regressions verify that muting during an outstanding request suppresses the later reveal cue, immediately pauses active clips, and releases finished or rejected media.
- **Browser: passed against the real standalone practice API.** `evidence/browser-verification.json` records all viewports and nine verified flows. No browser errors occurred. The full browser harness is `client/FortuneForge.Games.HiLo.Client/src/HiLoGame.e2e.mjs`.

## Browser evidence

| Viewport | Result | Screenshot |
| --- | --- | --- |
| 1440 × 900 desktop | No document scroll or horizontal overflow; primary controls inside viewport | [Active round](evidence/desktop-active.png) |
| 320 × 640 portrait | Full play surface and primary controls visible | [Active round](evidence/phone-320-active.png) |
| 360 × 740 portrait | Full play surface and primary controls visible | [Active round](evidence/phone-360-active.png) |
| 390 × 844 portrait | Full play surface and primary controls visible | [Active round](evidence/phone-390-active.png) |
| 430 × 932 portrait | Full play surface and primary controls visible | [Active round](evidence/phone-430-active.png) |
| 844 × 390 landscape | Compact table, history and cashout fit; primary touch targets at least 44px | [Active round](evidence/landscape-active.png) |

Further screenshots show [collected return](evidence/phone-390-collected.png) and a [lost round](evidence/phone-390-lost.png). Screenshots come from actual server-generated practice rounds. There is no client deal override or screenshot-only fake outcome.

The browser test verifies a starting debit, refresh of the same round, free skip, a correct call with the exact offered return, and cashout. It deliberately drops an already-committed cashout response, reloads the page, verifies the identical operation body is replayed and confirms no second credit. Duplicate cashout and changed-payload conflicts leave the wallet unchanged. It also exercises real losses and repeat wagers, keyboard Enter, touch, Escape/dialog focus restoration, mute and reduced motion.

Audio control assertions use an instrumented media stub for repeatability. A separate browser context also invokes the native media playback implementation: shuffle, card-place and chip-collect all decode/play successfully. See `realAudio` in the browser report. Sounds use approved shared CC0 assets, with quiet gain and persisted mute.

## Original art and performance

The original imagegen indigo cloth and final prompt are documented in `VISUAL_REFERENCE.md`. Runtime WebP is 413022 bytes; the lossless generated source is preserved. The built reusable package emits 122550 bytes of JavaScript and 638247 bytes of CSS (approximately 66.53 KB and 471.11 KB gzip, respectively). It embeds only the runtime material and required font/audio resources. No provider assets were extracted.

## Remaining release work

The isolated package is ready for owner hands-on review and coordinated integration. The central authenticated route, workspace/solution references, catalogue entry, shared smoke inventory and production feature configuration are intentionally pending the parent integration chat. Follow `INTEGRATION.md`, validate the integrated route against isolated emulator accounts, and obtain owner review before release. No owner approval or deployment is claimed by these checks.
