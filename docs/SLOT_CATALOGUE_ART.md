# Slot catalogue title-card production

Current source status (2026-11-02): the Home redesign selects runtime artwork through
`fortuneforge.client/src/features/catalog/cataloguePresentationArt.ts`. The original
files-only production brief below is retained as history. Unselected sibling versions
remain source artwork and do not change those explicit runtime imports.
Gold Foundry uses `gold-foundry-title-v1.webp` and its own artwork metadata.

Owner request: use the same three artwork chats to make original catalogue title cards for all
20 slots, one game at a time within each chat. This is catalogue artwork, not a reel/theme rewrite.
Parent owns review and this tracker. **Files only:** the owner reserved integration for another
chat doing a catalogue redesign. Do not upload or wire images into the game. No gameplay, RTP,
visibility or deployment changes.

## Production brief

- Use the imagegen skill and built-in image generator. One generated asset per game/call; finish
  and inspect each game before starting the next in your assigned queue. No multi-game collages.
- Inspect that game's existing assets, definition/catalogue and theme first. Keep its recognizable
  identity while producing polished, original illustrated key art. Do not reuse another game's image.
- Premium cohesive catalogue collection, with distinct compositions and each game's own palette.
  Exact full title below is prominent and legible at 256×128; no extra slogans, payout promises,
  fake interface, spin buttons, other brands, logos or watermarks. No religious/cultural additions
  unrelated to the existing game. Keep main title/subject in central 80% width / 70% height.
- Wide 2:1 composition, runtime **768×384 WebP**, ideally under **120 KB**, hard limit **180 KB**.
  Use `node scripts/prepare-slot-title-card.mjs <id> <source-path> [version]` to package the master.
  It uses the installed bundled image library; do not install dependencies or place originals in src.
- Save `fortuneforge.client/src/assets/catalogue/slots/<id>-title-v1.webp`, or a new version if a
  revision is needed; never overwrite. Save adjacent `<id>.artwork.json` containing id, title,
  selected file basename, width, height, bytes, exact `prompt`, generator=`built-in imagegen`,
  sourceMasterPath, referencePaths, visualCheck notes (full size, 256×128, crop, compression), and
  status=`ready-for-parent-review`. Use apply_patch for text files.
- Only edit assigned assets and metadata. Do not edit this tracker, runtime catalogue/source,
  shared CSS, existing game artwork, other workers' files or settings. No commit/push/deploy.
- Parent will review and hand off to the redesign chat, preserving files outside runtime imports.

## Progress

**20 / 20 generated and worker-reviewed; awaiting parent visual review.** All three queues are
complete. On October 26, 2026 the parent verified all selected files exist and match their
metadata sizes/dimensions (768×384). Integration remains explicitly out of scope. Read each
adjacent `<id>.artwork.json` for the selected version, prompt and worker visual checks. The
loading-screen follow-on was dispatched to all three chats; see `docs/GAME_LOADING_SCREENS.md`.

| Game ID | Exact title | Worker | Status |
| --- | --- | --- | --- |
| wukong-journey-to-the-west | Wukong's Journey to the West | A | Queued |
| rainbow-realm | Rainbow Realm | A | Queued |
| pirates-fortune | Pirates' Fortune | A | Queued |
| gods-of-olympus | Gods of Olympus | A | Queued |
| reel-riches | Reel Riches | A | Queued |
| high-noon-fortune | High Noon Fortune | A | Queued |
| royal-draw | Royal Draw | A | Queued |
| arcane-archives | Arcane Archives | B | Queued |
| cosmic-fortune | Cosmic Fortune | B | Queued |
| neon-nights | Neon Nights | B | Queued |
| jungle-jackpot | Jungle Jackpot | B | Queued |
| ocean-odyssey | Ocean Odyssey | B | Queued |
| samurai-fortune | Samurai Fortune | B | Queued |
| candy-carnival | Candy Carnival | C | Queued |
| phantom-manor | Phantom Manor | C | Queued |
| nordic-legends | Nordic Legends | C | Queued |
| desert-treasures | Desert Treasures | C | Queued |
| robot-revolution | Robot Revolution | C | Queued |
| dragon-hoard | Dragon Hoard | C | Queued |

Chat A: `01a17671-679d-7b33-a055-a25c53ef2395`.
Chat B: `01a17671-8584-7c91-9b7d-6f21787bd2f0`.
Chat C: `01a17671-9ed2-7423-91e8-a25495b25e79`.
