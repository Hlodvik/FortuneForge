# Verification — October 20, 2026

The results below record the isolated package verification on October 20. On October 24, local main-site integration was added at `/games/andar-bahar`, with shared navigation, catalogue and account-history support; this is not a deployment. Current integration acceptance is described in [INTEGRATION.md](INTEGRATION.md).

## October 24 implementation verification

- **Latest approved court design:** the detailed portrait experiment below has
  been superseded by three shared inline SVG symbols: upright Jack sword with
  curved guard gap, closed arched Queen crown, and pointed King crown. Their
  geometry matches the approved preview; colour inherits from the normal suit
  styles. Number cards and backs are unchanged, and no portrait images are
  requested or included in the production asset output. The **51/51 focused
  host card/route/Hold'em checks and main production build passed**. Desktop and
  **390x844** practice-board inspection confirmed readable icons, zero inner
  borders and no horizontal overflow. No commit, push, or deployment was made.

- Client strict type check and **85/85 package unit/component tests passed**. Coverage includes bounded requests, ignored-abort adapters, same-key timeout/retry, account changes, late responses, disabled-table restoration, duplicate settlement callbacks, utility clicks and mute/audio limits, compact header/help and shared faces restricted to revealed cards.
- The game and its playable monorepo preview reuse the real shared navbar. Duplicate site branding, navbar title/practice action, table-mode label and board limits were removed; connected limits are available in the **?** panel. The Joker, both visible hands and result-popup cards use a host face renderer; the game-specific back is preserved. **22/22 focused route/shared-shell checks passed**, and both package and main-client production builds passed. Desktop, phone and landscape checks found no horizontal overflow; short phones retain inner scrolling.
- Andar Bahar uses the canonical host `PlayingCard` component and `playingCards.css`, exactly as Blackjack and Dragon Tiger do. Its connected route tests compare court and number markup directly with the shared component; the standalone preview injects that same component too. The rejected SVG alternative and its image renderer were removed from active sources. Hold'em's separate court-face styling was also removed so its variants retain the canonical face design, without changing table geometry or backs. Focused checks passed: 11 Andar-route/shared-card tests, 6 Andar component tests, and 28 Hold'em/shared-card tests (the groups overlap on the 2 shared-card checks). No gameplay rules or server code changed in this face-consistency pass.
- A subsequent shared court-art pass replaces the inset J/Q/K letter panels with traditional mirrored human figures, simplified to **black ink for clubs/spades and red ink for hearts/diamonds**, with no inner frame or decorative colour fills. All twelve portraits are shared assets (about 330 kB total); number cards and backs are unchanged. **51/51 focused host renderer/Andar/Dragon Tiger/Hi-Lo/Hold'em checks passed**, plus **49/49 Dragon Tiger and 37/37 Hi-Lo package checks**, both package strict checks/builds, and the main production build. Fallback CSS is isolated so it cannot restore a frame on host-rendered cards. The local Andar preview was visually checked on desktop and at **390×844**: all visible portraits loaded, computed inner borders were zero, and no horizontal overflow occurred. Evidence: `.tmp/andar-bahar-monochrome-courts-desktop.png` and `.tmp/andar-bahar-monochrome-courts-mobile.png`. This remains local, not deployed.
- **58/58 focused server/practice/HTTP/local-Firestore tests passed**, including atomic shared history, linked ledger entries, legacy recovery, history corruption rejection and the disabled-new-bet/exact-replay distinction.
- **13/13 focused host history/admin checks passed**, including an actual emulator settlement followed by player/admin history reads, exact fractional-cent totals and seen/unseen replay preservation.
- Host registration, authenticated routing, practice isolation, catalogue/history mappings and the release route inventory received additional focused checks; package and main web production builds passed. No unrelated game test sweep was requested or run.
- The new palace artwork was visually checked at **1440×900**, **390×844**, **320×568** and **844×390** through the local practice preview, with no document overflow or browser errors. A winning R0.50 Andar round returned exactly **R0.95**, adding **R0.45** to its no-cash-value wallet.
- The built main client was also checked under its real shared navbar using an ephemeral read-only localhost fixture, a synthetic layout-test profile and the force-practice sample API. At **320×568**, the inner game area scrolls safely to reveal the complete wallet/footer; at **1440×900**, it fits without inner scrolling. No horizontal overflow was present. The wrapper preserves the table's intrinsic minimum track height rather than clipping fixed rows. Account refreshes reject stale responses, including two out-of-order refreshes for the same account.
- This pass did **not** run the historical raw Playwright script, use production funds, push a commit or deploy. Current manual browser evidence is in `.tmp/andar-bahar-client-audit/palace-desktop.png`, `palace-shared-shell-desktop.png` and `palace-shared-shell-phone.png`; current artwork, exact generation prompt and sound provenance are documented in [THEME.md](THEME.md).

The October 20 results below are historical, not a claim that its entire browser suite was rerun for this pass.

## Results

| Check | Result |
| --- | --- |
| .NET Release engine + HTTP + persistence + Firestore suite | **79 passed**, zero skipped |
| Engine-first gate, before visual research | **36 passed**, including exhaustive 20,825 subsets and all 52 × 49 Joker/first-match cases |
| HTTP gateway unit tests | **57 passed** |
| Client recovery and identity regression tests | **4 passed**; **61 client tests total** |
| TypeScript strict type check | Passed |
| Reusable client library production build + declaration emit | Passed; CSS 482.21 kB / gzip 349.09 kB; JS 121.58 kB / gzip 69.02 kB |
| npm package dry run | Passed; component, declarations, stylesheet, README and audio license included |
| Practice API Release build | Passed, zero warnings/errors |
| Browser play/recovery/responsive suite | Passed; [machine-readable report](evidence/browser-verification.json) |
| Real HTTP practice API restart/replay | Passed: original round/key, unchanged settled wallet, no duplicate debit |

The full .NET suite includes **13 actual local Firestore emulator tests**, rather than only in-memory wallet tests. They cover atomic paid debit/credit/event/latest writes; same-key replay across server instances; body conflicts; competing-key insufficient funds; current-wallet balance on old-round replay; ownership privacy; no hidden deck tail; corrupt/missing ledger/event/record rejection; and preservation of existing fractional cents. The sample uses the durable file practice store and cannot contact a production wallet.

## Browser coverage and screenshots

The local browser suite asserts its API mode is `development-practice-andar-first` before every real wallet check. It uses only the sample's no-cash-value credit.

Tested dimensions: **1440×1000**, **320×844**, **360×844**, **375×844**, **390×844**, **430×844**, **844×390**, and **568×320**. A manual Chrome inspection additionally checked **320×640** and the normal desktop viewport. The primary play controls remain in the first viewport; portrait and short-landscape result dialogs retain a visible Continue action. No horizontal overflow or browser page errors were observed.

The tests exercise a normal wager, actual alternating card prefix and payout mathematics, individually paced card appearance, skip without a new request/settlement, result feedback, another round, duplicate key replay, changed-body conflict, separate cookie wallets, cross-account private result reads, a committed response deliberately lost before reload, recovery with the original key, refresh without a new POST, confirmed insufficient-funds rejection, reconnect, keyboard activation, actual touch taps, reduced motion, rules and persistent mute. All three OGG cues are checked for media loading/play and decoding to non-silent PCM samples.

Pending wagers use **tab-scoped sessionStorage** so another tab cannot erase their recovery key; a refresh of the same tab retains it. Settled funds and outcomes are always durable on the server. Rejection-triggered balance refreshes are guarded by wager generation so an older response cannot replace a newer settled balance. The prepared host route also guards async account refreshes against account switches and scopes game identity to the authenticated user.

Four focused hook regressions verify a delayed rejection-status response after a later settlement; matching-key cleanup and tab-scoped storage; an unknown response followed by remount with the same key/body; and an old account's response after player identity changes. The browser suite also opens two real tabs sharing one practice cookie, settles distinct concurrent wagers, verifies one tab cannot delete the other's key, and reloads the second tab to recover its own wager without a second debit.

Curated evidence is under `evidence/`: desktop table/chips/dealing/result, 320px and 390px portrait, mobile result, 568px landscape and compact result, and connection recovery. The original generated material and prompt are recorded in [ASSETS.md](ASSETS.md); professional screen selection is documented in [REFERENCE_REVIEW.md](REFERENCE_REVIEW.md).

## Reproduce

With the existing repository dependencies, run the commands in [README.md](README.md). Do not run a root dependency install or use production funds. The local preview uses **5182**, and its API uses **5202**. Sandboxed browser loopback access may require launching these two game processes with ordinary local execution permission. No unrelated server was stopped.

In this Windows environment, Vitest uses a writable package `.test-temp` via `TEMP`/`TMP`. Playwright uses the existing browser cache at `C:/Users/Lctiv/AppData/Local/ms-playwright`. No package installation, branch/worktree operation, commit, push, deployment, live setting, shared manifest/lockfile, central route/catalogue, Startup, solution or release-script change was made by this package.
