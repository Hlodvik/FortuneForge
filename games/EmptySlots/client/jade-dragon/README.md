# Jade Dragon / EmptySlots integration

## Route and ownership

`/slots/empty-slots-game` now hosts Jade Dragon with the Fortune Forge navbar and authenticated session. The original game ID `empty-slots-v1`, S01–S23 IDs, engine version, persistence and settlement paths remain authoritative. The independent vanilla/Three.js presentation is built from `games/EmptySlots/client/jade-dragon` into `fortuneforge.client/public/games/jade-dragon`; the generated bundle is ignored. The host client's `prebuild` builds it automatically.

The embedded frame contains no account token, balance persistence, RNG-based outcomes or payout evaluator. It sends allowlisted requests through a same-origin parent bridge. The parent uses the existing `fetchWithAccountSession`, rejects overlapping spins and caches request IDs to prevent the same message from submitting another wager. Financial requests are sent once; ambiguous failures disable play and require reloading server state. Every frame instance uses a new request namespace.

The host reads balance values in Rand and converts them to integer display cents. Bets and payouts use native points at 25 cents per point. New paid wagers are capped at 400 points/R100 in both the presentation adapter and server admission policy. Existing free-spin entitlements retain the server's locked historical wager. Public demo behavior remains nonfinancial and retains its native wager range.

## Presentation and backend behavior

The art, scenery, synthesized sound, Auto control and image-based bonus reveals come from Tian's original Jade Dragon prototype. Reels accept the backend's 5×4 base grid and 5×6 collection feature. Wild, scatter and collection symbols use the detailed generated artwork. Other tiles use the procedural 3D models with labels assigned by the stable backend symbol mapping.

**Rules are the EmptySlots rules, not the standalone prototype rules.** The backend has 23 paylines, six scatter free spins, and four collection tracks that trigger eight copy/expand/wild spins at 22. There are no client-side cascades, three-way 20/10/5 bonus choices, or pick-a-pearl gift payout. Those would require a separately versioned backend rules/entitlement redesign; they are not shown as playable options here. The standalone prototype's highly generous simulated payout model was not transplanted into the wallet-backed game.

`GET /api/slots/empty-slots/presentation` returns the engine's display rules and role/paytable metadata without exposing reel strips or account state. Paid spins continue through the existing admission and Firestore settlement transaction. No Firebase project, wallet, payment provider, or production account was created or changed.

For emulator verification, the host's FirebaseApp credential factory now allows a non-production credential only when all three conditions hold: Development environment, `demo-*` project ID, and explicit validated loopback Firestore emulator host. Production retains ADC. Six regression cases cover this boundary.

## Validation

- Jade API and route tests: 13 passing.
- Server presentation/cap/emulator-boundary tests: 8 passing.
- Complete client suite first run: 1,426 passed, 3 failures. Two failures were old blank-catalog expectations; they were updated to verify the new named card. The third was a shared demo-controller evidence fingerprint: the unnecessary demo-cap edit was removed, preserving that controller's original bytes. All three affected suites then passed (29 tests).
- Host TypeScript, Vite production bundle, catalogue verification, navbar boundary and source ownership checks pass. Windows SSH rejects npm directory junction traversal, so verification used materialized generated dependencies and an uncommitted real-path Vite resolver; original production bundle-isolation guards remained enabled.
- Browser test against the remote Development host and `demo-fortuneforge-jade` emulator project: one spin produced one POST; resulting balance matched stake/payout conversion and recovered on reload; server rejected an R100.50 wager; rules loaded; mobile had no horizontal overflow.
- Feature display fixture used a real nonfinancial demo outcome to verify six-row rendering, visible free-games count, Auto stop, and recovery of the actual emulator account state. Its legacy demo `isFreeSpin` response flag was normalized only inside the browser fixture to match the paid contract. It never credited the account.
- Dedicated broad server run under the repository's isolated Firestore test-emulator configuration: 1,528 passed, 3 failed. The failures concern unchanged Jungle Jackpot game inventory/legacy activity expectations (`GameAnalyticsFirestoreTests` twice and `PlayerGameExperienceFirestoreTests` once). No test assertions were removed or waived. These broader failures remain unresolved and prevent calling the whole release suite green.
- No live wallet wagers were used in verification. No production publication has been performed.

## Remote workspace

Branch: `codex/jade-dragon-empty-slots`.
Worktree: `C:/Users/Lctiv/source/repos/FortuneForge-consolidated/.tmp/jade-dragon`.
Development preview uses a separate emulator-only backend and a separate Vite port. Release must follow the repository's existing clean-main release guard and verification process; do not deploy the dirty main worktree or copy generated frontend files onto an older serving backend.

The SSH account `tian` can work in this new worktree. Git refuses access to the existing release-main worktree because it belongs to `Lctiv` (`detected dubious ownership`). No Git trust exception, ACL change, or release-guard bypass was applied. The change is committed on its feature branch, not merged into main or published.

Tian confirmed retaining the existing EmptySlots server rules for the final integration. The prototype's cascade/bonus-choice/gift rules are therefore deliberately excluded.
