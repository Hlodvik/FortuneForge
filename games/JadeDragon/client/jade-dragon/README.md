# Jade Dragon presentation

## Route and ownership

`/slots/jade-dragon` hosts Jade Dragon with the Fortune Forge navbar and authenticated session. Game ID `empty-slots-v1`, S01–S23 symbol IDs, engine version, persistence and settlement paths are authoritative. The independent vanilla/Three.js presentation is built from `games/JadeDragon/client/jade-dragon` into `fortuneforge.client/public/games/jade-dragon`; the generated bundle is ignored. The host client's `prebuild` builds it automatically.

The embedded frame contains no account token, balance persistence, RNG-based outcomes or payout evaluator. It sends allowlisted requests through a same-origin parent bridge. The parent uses the existing `fetchWithAccountSession`, rejects overlapping spins and caches request IDs to prevent the same message from submitting another wager. Financial requests are sent once; ambiguous failures disable play and require reloading server state. Every frame instance uses a new request namespace.

The host reads balance values in Rand and converts them to integer display cents. Bets and payouts use native points at 25 cents per point. New paid wagers are capped at 400 points/R100 in both the presentation adapter and server admission policy. Existing free-spin entitlements retain the server's locked historical wager. Public demo behavior remains nonfinancial and retains its native wager range.

## Presentation and backend behavior

The art, scenery, synthesized sound, Auto control and image-based bonus reveals come from Tian's original Jade Dragon prototype. Reels accept the backend's 5×4 base grid and 5×6 collection feature. Wild, scatter and collection symbols use the detailed generated artwork. Other tiles use the procedural 3D models with labels assigned by the stable backend symbol mapping.

**Rules come from the Jade Dragon backend, not the standalone prototype.** The backend has 23 paylines, six scatter free spins, and four collection tracks that trigger eight copy/expand/wild spins at 22. There are no client-side cascades, three-way 20/10/5 bonus choices, or pick-a-pearl gift payout. Those would require a separately versioned backend rules/entitlement redesign; they are not shown as playable options here. The standalone prototype's simulated payout model was not transplanted into the wallet-backed game.

`GET /api/slots/jade-dragon/presentation` returns the engine's display rules and role/paytable metadata without exposing reel strips or account state. Paid spins continue through the existing admission and Firestore settlement transaction. No Firebase project, wallet, payment provider, or production account was created or changed.

For emulator verification, the host's FirebaseApp credential factory now allows a non-production credential only when all three conditions hold: Development environment, `demo-*` project ID, and explicit validated loopback Firestore emulator host. Production retains ADC. Six regression cases cover this boundary.

## Ownership boundary

The frame owns rendering, audio, controls, and animation. It never owns account
tokens, balances, randomness, payout evaluation, or persistence. Backend changes
belong in `games/JadeDragon/src/FortuneForge.Games.JadeDragon`; host authentication,
wallet admission, and Firebase settlement stay in the Fortune Forge platform layer.
