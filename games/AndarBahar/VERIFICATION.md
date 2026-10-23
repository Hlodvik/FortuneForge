# Verification — October 20, 2026

The package is ready for owner review as an isolated playable table. It is not integrated into the central catalogue or deployed. Production feature activation and the composed authenticated route require the parent batch integration and owner acceptance described in [INTEGRATION.md](INTEGRATION.md).

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
