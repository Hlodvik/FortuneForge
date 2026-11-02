# Non-slot catalogue title-card production

Current source status (2026-11-02): the Home redesign selects runtime artwork through
`fortuneforge.client/src/features/catalog/cataloguePresentationArt.ts`. The original
files-only production brief below is retained as history. Unselected sibling versions
remain source artwork and do not change those explicit runtime imports.

Owner request: create attractive original catalogue images for all 20 currently listed non-slot
games, using three separate chats working sequentially, one asset at a time per chat. Parent chat
owns this tracker and final review. The owner subsequently requested **artwork files only** because
another chat is redesigning the catalogue. Do not integrate, upload, commit or deploy these images.
Do not alter gameplay, listing defaults or slots.

## Shared production brief

- Use the imagegen skill and built-in image generator, one call for one game. Read its instructions
  yourself. Inspect relevant current game art and approved shared card/chip styling first.
- Produce polished, professionally illustrated game key art, not screenshots, CSS icons, crude
  clip art or photorealistic piles of money. Distinct motifs/composition for each title, with a
  coherent premium collection feel and restrained lighting/depth. Use each game's existing theme.
- Wide **2:1** landscape composition. Exact game title must be prominent and readable at thumbnail
  size, with no extra slogans, payout claims, logos, watermarks, buttons or fake UI. Keep title and
  focal subject within the central 80% width / 70% height for responsive cropping.
- Card/table games may use the approved emerald felt; Dragon Tiger retains its red/gold Asian
  motif and Andar Bahar its palace identity. Mines stays dark slate with green gems, not felt.
  Puzzle games use their real number-tile identity. No unsupported cultural/religious additions.
- Approved card courts are **simple single-ink icons**, not portraits: pointed king crown, arched
  queen crown, vertical jack sword and jester hat in
  `fortuneforge.client/src/games/cards/shared/CourtCardSymbol.tsx`. Retired court portrait images
  are NOT references for current cards. Current chip assets may be used as visual references.
- Save runtime art as `fortuneforge.client/src/assets/catalogue/non-slots/<id>-title-v1.webp`,
  **768×384**, ideally under **120 KB**, hard target under **180 KB**. Keep large generated masters
  out of runtime imports. `scripts/prepare-non-slot-title-card.mjs` packages a generated master;
  it uses the already bundled image library and installs nothing.
- Inspect every image at full size and thumbnail size. Fix spelling, crop, incorrect game props
  or illegible text before marking ready. If a new version is needed, retain the old version and
  use `-v2`, etc.; never overwrite another worker's files.
- For each game write `<id>.artwork.json` beside the WebP with id, title, file (basename), width,
  height, bytes, exact generation prompt, generator=`built-in imagegen`, source master path,
  reference paths, visualCheck notes, and status=`ready-for-parent-review`. Use apply_patch for
  text/source files. Only your assigned assets/metadata are yours to edit.
- Do NOT edit this tracker, catalogue components, shared CSS, other game assets, package files,
  gameplay or deployment settings. No commit/push/deploy. Parent reviews and integrates the cards.
- Finish a game completely before starting the next assigned game. A completed queue does not
  authorize extra variants or unrelated work. Report progress through your per-game metadata and
  chat commentary; parent will monitor the chats.

## Progress

**20 / 20 generated and reviewed.** All selected files are 768×384 WebP, each under 96 KB,
821,470 bytes total. No artwork is integrated: the parent removed its local wiring after the owner
reserved integration for the separate redesign chat. Queues were sequential within each chat.

| Game ID | Exact title | Worker | Selected file | Status |
| --- | --- | --- | --- | --- |
| blackjack | Fortune Blackjack | A | blackjack-title-v2.webp | Reviewed |
| texas-holdem | Texas Hold’em | A | texas-holdem-title-v2.webp | Reviewed |
| three-card-poker | Three Card Poker | A | three-card-poker-title-v2.webp | Reviewed |
| house-holdem | House Hold’em | A | house-holdem-title-v2.webp | Reviewed |
| baccarat | Baccarat | A | baccarat-title-v1.webp | Reviewed |
| roulette | Roulette | A | roulette-title-v3.webp | Reviewed |
| craps | Craps | A | craps-title-v1.webp | Reviewed |
| dragon-tiger | Dragon Tiger | B | dragon-tiger-title-v1.webp | Reviewed |
| andar-bahar | Andar Bahar | B | andar-bahar-title-v1.webp | Reviewed |
| hi-lo | Hi-Lo | B | hi-lo-title-v1.webp | Reviewed |
| casino-war | Casino War | B | casino-war-title-v1.webp | Reviewed |
| video-poker | Video Poker | B | video-poker-title-v1.webp | Reviewed |
| keno | Keno | B | keno-title-v1.webp | Reviewed |
| sic-bo | Sic Bo | B | sic-bo-title-v1.webp | Reviewed |
| mines | Mines | C | mines-title-v2.webp | Reviewed |
| solitaire | Competitive Solitaire | C | solitaire-title-v2.webp | Reviewed |
| hearts | Hearts | C | hearts-title-v2.webp | Reviewed |
| liars-dice | Liar’s Dice | C | liars-dice-title-v3.webp | Reviewed |
| 2048 | 2048 | C | 2048-title-v1.webp | Reviewed |
| drop-merge | Drop Merge | C | drop-merge-title-v1.webp | Reviewed |

Excluded: all 20 slots and the four currently unlisted arcade games (Horse Flight, Snake,
Flappy and Asteroid Blaster). No catalogue visibility changes are part of this work.

## Chat assignments

- A — **Catalogue art — classic table games** (`01a17671-679d-7b33-a055-a25c53ef2395`).
- B — **Catalogue art — themed casino games** (`01a17671-8584-7c91-9b7d-6f21787bd2f0`).
- C — **Catalogue art — cards, gems and puzzles** (`01a17671-9ed2-7423-91e8-a25495b25e79`).

All three non-slot queues completed. Selected files and their generation prompts/provenance are in
`fortuneforge.client/src/assets/catalogue/non-slots/`. Read each `<id>.artwork.json` to identify
the selected version; unselected sibling versions must not be imported. These files are not used
by runtime catalogue source. Recoverable integration drafts are in ignored
`.tmp/catalogue-art/integration-draft/`, not in runtime imports.

The same three chats have now been assigned slots; see `docs/SLOT_CATALOGUE_ART.md`.

## Separate hero-banner request

The owner rejected the jester-mask direction, then selected the complete dark/violet banner with
two aces, red/black chips and an emerald gem (`exec-894311cc-9e01-4629-bf81-2e5672061355.png`).
The optimized **owner-selected, not implemented** handoff is
`output/artwork/hero/fortuneforge-hero-selected-v1.webp` (1600×640, 40,068 bytes), with the exact
prompt and approval in its adjacent `.artwork.json`. The separate redesign chat owns integration.
Other hero/mask concepts remain unapproved; workers must not add banners to the catalogue set.
