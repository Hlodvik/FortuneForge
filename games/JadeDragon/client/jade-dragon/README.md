# Jade Dragon presentation

## Route and ownership

`/slots/jade-dragon` hosts Jade Dragon with the Fortune Forge navbar and authenticated session. Game ID `empty-slots-v1`, the selected engine version, symbol IDs, persistence and settlement paths are authoritative. The independent vanilla/Three.js presentation is built from `games/JadeDragon/client/jade-dragon` into `fortuneforge.client/public/games/jade-dragon`; the generated bundle is ignored. The host client's `prebuild` builds it automatically.

The embedded frame contains no account token, balance persistence, RNG-based outcomes or payout evaluator. It sends allowlisted requests through a same-origin parent bridge. The parent uses the existing `fetchWithAccountSession`, rejects overlapping spins and caches request IDs to prevent the same message from submitting another wager. Financial requests are sent once; ambiguous failures disable play and require reloading server state. Every frame instance uses a new request namespace.

The host reads balance values in Rand and converts them to integer display cents. Bets and payouts use native points at 25 cents per point. New paid wagers are capped at 400 points/R100 in both the presentation adapter and server admission policy. Existing free-spin entitlements retain the server's locked historical wager. Public demo behavior remains nonfinancial and retains its native wager range.

## Presentation and backend behavior

The art, scenery, synthesized sound, Auto control and image-based bonus reveals come from Tian's original Jade Dragon prototype. The cabinet renders the 5×4 cascade grid and the legacy 5×4 or 5×6 grids. Named cascade symbols use the Jade models, dragon and pearl art, and Tian's phoenix and mystic knot vectors.

The presentation endpoint supplies the active rules and paytable. A cascade spin settles once and supplies its initial board, winning cells, cleared positions, replacement boards, drop distances, stage awards, and final total. The cabinet replays those stages and can reveal the settled final board immediately. Legacy spins use their original paylines and single-board playback.

`GET /api/slots/jade-dragon/presentation` returns the engine's display rules and role/paytable metadata without exposing reel strips or account state. Paid spins continue through the existing admission and Firestore settlement transaction. No Firebase project, wallet, payment provider, or production account was created or changed.

For emulator verification, the host's FirebaseApp credential factory now allows a non-production credential only when all three conditions hold: Development environment, `demo-*` project ID, and explicit validated loopback Firestore emulator host. Production retains ADC. Six regression cases cover this boundary.

## Ownership boundary

The frame owns rendering, audio, controls, and animation. It never owns account
tokens, balances, randomness, payout evaluation, or persistence. Backend changes
belong in `games/JadeDragon/src/FortuneForge.Games.JadeDragon`; host authentication,
wallet admission, and Firebase settlement stay in the Fortune Forge platform layer.
