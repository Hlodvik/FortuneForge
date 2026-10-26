# Per-game loading screens — follow-on work

Owner request: after all three catalogue chats finish, have those same chats create a loading
screen for each game to replace the plain game-loading text. Scope: the 40 catalogue games
(20 slots + 20 non-slots). Do not relist the four hidden arcade games.

## Gate and dispatch ledger

Gate: **all-three-reported-complete; parent-audit-complete**. Verified on October 26, 2026: all three
catalogue turns completed, all 20 non-slot plus 20 slot selected records/images exist, and each
selected file matches its metadata size and 768×384 dimensions. The catalogue layout remains unchanged;
the owner separately approved implementing these images in the new landing/home pages.

Automatic follow-up: `game-loading-screens-after-catalogue` (thread heartbeat, active). It waits
for this gate, dispatches the three assignments once, and follows the work through local review.

| Chat | ID | Loading dispatch |
| --- | --- | --- |
| A | 01a17671-679d-7b33-a055-a25c53ef2395 | Sent October 26, 2026 at 05:25 UTC |
| B | 01a17671-8584-7c91-9b7d-6f21787bd2f0 | Sent October 26, 2026 at 05:25 UTC |
| C | 01a17671-9ed2-7423-91e8-a25495b25e79 | Sent October 26, 2026 at 05:25 UTC |

Record each successful dispatch here immediately so the follow-up never sends it twice. If an
outcome is unknown, read that chat's recent messages before retrying. Preserve existing worker
model/settings. The human owner authorized these messages in this chat.

### Status checked October 26, 2026 at 07:30 UTC

- A reported the shared component and all 14 A presentations complete: 23 tests, TypeScript,
  lint, 36 layout checks and missing-art/reduced-motion checks passed. Parent review pending.
- C reported its 12 presentations complete: 38 tests, TypeScript and 48 viewport checks passed.
  Parent review pending.
- B prepared all 14 mappings but wrote no manifest/tests. Its approval review rejected writes
  twice because it considered the earlier artwork-only instruction binding. The parent asked
  the owner directly whether B may now create local loading mappings/tests using existing art,
  without catalogue changes or deployment. The owner then directly replied **“Yes, finish the
  local loading-screen work.”** The parent successfully sent this explicit approval and the
  original assignment back to B on October 26, 2026. **Resumption sent once; do not duplicate.**
  B may create only its local manifest/tests using existing art; no catalogue edits or deployment.
- Latest compact cursors: A `720f1e63-914d-40cc-9a27-8f92b2180604:10`;
  B `4a2d474b-462d-41df-8323-b812ce897e9e:8`;
  C `26bc2b9a-8799-4b08-b8b3-49e7e4477c9e:9`.
- B has now completed its 14 mappings and tests after owner approval. It reported all 112 shared
  loading tests, TypeScript/lint and phone/desktop/edge-layout checks passing. Latest B cursor:
  `4a2d474b-462d-41df-8323-b812ce897e9e:15`. Do not send another assignment or approval message.
- Parent audit completed October 26, 2026. AppRoutes, AuthenticatedRouteState and the three
  previously blank card-account branches now share the loading presentation. Five initial table
  waits (Blackjack, Three Card Poker, House Hold'em, Texas Hold'em and Solitaire) use it too.
  Error/retry, navigation, matchmaking, and active Solitaire free-play priority are unchanged.
- All 40 selected images are already committed optimized 768×384 WebPs (2,512,428 bytes total).
  All 62 presentation aliases resolve without importing game engines or adding routes. Mines'
  prepared presentation does not enable its separately unfinished production integration.
- Final combined loading/home verification: 147 tests in 11 files passed. Separately, the five
  legacy-table and route integration batches passed, including 53 Hold'em/Solitaire tests and 40
  Blackjack/Three Card Poker/House Hold'em tests. Targeted lint and whitespace checks passed.
  A sandbox cache rename restriction occurred before execution; the authorized outside-sandbox
  retry passed. Client TypeScript and production build passed after final changes.
- Parent visually checked actual route/shell CSS at phone, tablet, desktop and short-landscape
  sizes. Artwork remained uncropped, status readable and layouts free of scrolling. Missing-art
  fallback at 320×568 passed; shared reduced-motion tests and worker browser checks passed.
  Local evidence: `output/game-loading-review/parent/holdem-phone.png` and
  `output/game-loading-review/parent/solitaire-landscape.png`. No layout repairs were necessary.
- Separate peer review found no loading/home blockers or gameplay changes. The owner now
  explicitly requests **push to main and deploy after the audit**. This supersedes the original
  no-release restriction for the audited loading work and the requested home-page copy/section
  removals only. Unrelated Mines, server, felt and Craps changes remain local and out of this release.
- Release preparation is in progress; this ledger does not yet claim deployment success.

## Shared design and implementation brief

- Build 40 game-specific presentations on one polished shared loading-screen component, not 40
  duplicated full-page applications. Use each game's selected artwork/title and matching dark
  background/accent. Reuse the new optimized catalogue art instead of regenerating the same
  picture or adding enormous loading-only images. Do not modify the catalogue itself.
- Read `<id>.artwork.json` in `src/assets/catalogue/{non-slots,slots}` for the selected version.
  Other game art, gameplay, wallet calls, matchmaking and timers stay unchanged.
- Show a full-viewport/in-shell game identity with title art kept legible and uncropped on phones,
  a restrained animated loading indicator, a screen-reader status, and reduced-motion support.
  The screen replaces plain loading text; it is not a button, splash click-through or promotion.
- Display only during genuine pending work. No invented percentage, fake progress, minimum
  display duration, artificial delay or user action to enter the game. Loading artwork must not
  eagerly import full game packages. Dark non-flashing fallback if an image is unavailable.
- No new dependencies, autoplay sound, authentication changes, purchase actions or external writes.
  Preserve existing error/retry/return controls exactly. No commit, push or deployment.
- Own new files only under `fortuneforge.client/src/features/game-loading/`, as assigned below.
  Parent owns existing route integration after reviewing all three chats' work. This avoids
  overlapping the separate landing/home/catalogue redesign chat.

## Contract

Chat A owns `types.ts` defining `GameLoadingEntry` with `id`, `title`, `routes` (readonly strings),
`image` (static imported URL), `background`, `accent`, and optional `objectPosition`. Each worker
exports a readonly entry array from their own manifest. Routes must match existing route registry;
include existing play/demo aliases without exposing or relisting any route. No fabricated URLs.

Chat A also owns `GameLoadingScreen.tsx`, `gameLoadingScreen.css`, shared tests and
`loadingPresentationsA.ts`. Props: `{ entry?: GameLoadingEntry }`; missing entry gives a tasteful
generic accessible pending state. Keep progress semantics indeterminate. Do not use window pathname
as hidden global state. Use the current shared app font and existing assets.

Chat B owns `loadingPresentationsB.ts` and its tests. Chat C owns `loadingPresentationsC.ts` and
its tests. They may prepare their manifests before A's component is ready, then verify with the
shared component. Do not edit each other's files. Inspect every assigned presentation at thumbnail
and phone/desktop dimensions; report broken crops or incorrect titles rather than marking done.

## Assigned presentations (one at a time per chat)

A (14): blackjack, texas-holdem, three-card-poker, house-holdem, baccarat, roulette, craps;
wukong-journey-to-the-west, rainbow-realm, pirates-fortune, gods-of-olympus, reel-riches,
high-noon-fortune, royal-draw.

B (14): dragon-tiger, andar-bahar, hi-lo, casino-war, video-poker, keno, sic-bo;
arcane-archives, cosmic-fortune, dino-dominion, neon-nights, jungle-jackpot, ocean-odyssey,
samurai-fortune.

C (12): mines, solitaire, hearts, liars-dice, 2048, drop-merge; candy-carnival, phantom-manor,
nordic-legends, desert-treasures, robot-revolution, dragon-hoard.

## Parent integration after all three finish

1. Review the shared design, all 40 unique entries/assets, route aliases, payload sizes and worker
   checks. Merge the three arrays in a small route resolver; preserve unknown-route behavior.
2. The safe seams found by the read-only audit are the `AppRoutes.tsx` Suspense fallback and only
   the `error === null` branch of `AuthenticatedRouteState.tsx`. Current route identity is already
   available before lazy game imports; use a small context or explicit props, not full manifests.
   `SlotGameRoute.tsx` already funnels manifest/account waits through AuthenticatedRouteState.
3. Blackjack, Three Card Poker and House Hold'em currently return null during account pending;
   route those real waits through the same loading view. Do not change availability/error branches.
4. Keep non-game page loaders, login redirects, paid-request recovery, in-game state transitions,
   matchmaking and all existing error/retry behavior intact. Do not change landing, home or catalogue.
5. Targeted tests: all 40 unique mappings and assets, slot/demo aliases, missing artwork/unknown
   route, pending chunk/account, immediate successful transition, existing retry/error behavior,
   small-screen no overflow and reduced motion. Build the client. Use the computer-use skill for
   browser QA if needed. Do not run unrelated full-game audits or deploy.
6. Mark reviewed local work complete here; notify owner with previews and clearly say not deployed.

The owner separately approved the landing/home mockups (with a new headline and no How it works).
Those pages were implemented and deployed from main `893e556` on October 26, 2026. That release
excluded all game-loading work. Do not alter those pages or the separate catalogue layout here.
