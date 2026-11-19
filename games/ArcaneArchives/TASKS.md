# Arcane Tasks

Running kanban for Arcane Archives fixes and changes. New requests start in To Do; move items as their status changes.

<!-- Edited: 2026-11-18T15:10:21-06:00 | Computer: Vinnabok -->
<!-- Edited: 2026-11-18T15:15:50-06:00 | Computer: Vinnabok -->
<!-- Edited: 2026-11-18T15:33:49-06:00 | Computer: Vinnabok -->
<!-- Edited: 2026-11-18T16:58:33-06:00 | Computer: Vinnabok -->
<!-- Edited: 2026-11-18T18:10:32-06:00 | Computer: Vinnabok -->
<!-- Edited: 2026-11-18T18:10:58-06:00 | Computer: Vinnabok -->
<!-- Edited: 2026-11-18T18:12:31-06:00 | Computer: Vinnabok -->
<!-- Edited: 2026-11-18T18:12:57-06:00 | Computer: Vinnabok -->
<!-- Edited: 2026-11-18T18:13:29-06:00 | Computer: Vinnabok -->
<!-- Edited: 2026-11-18T18:15:11-06:00 | Computer: Vinnabok -->
<!-- Edited: 2026-11-18T18:17:06-06:00 | Computer: Vinnabok -->
<!-- Edited: 2026-11-18T18:23:17-06:00 | Computer: Vinnabok -->
<!-- Edited: 2026-11-18T18:24:13-06:00 | Computer: Vinnabok -->
<!-- Edited: 2026-11-18T18:32:23-06:00 | Computer: Vinnabok -->
<!-- Edited: 2026-11-18T18:38:42-06:00 | Computer: Vinnabok -->
<!-- Edited: 2026-11-18T18:49:01-06:00 | Computer: Vinnabok -->
<!-- Edited: 2026-11-18T18:54:26-06:00 | Computer: Vinnabok -->
<!-- Edited: 2026-11-18T19:03:49-06:00 | Computer: Vinnabok -->
<!-- Edited: 2026-11-18T19:10:38-06:00 | Computer: Vinnabok -->
<!-- Edited: 2026-11-18T19:13:32-06:00 | Computer: Vinnabok -->

## To Do

None.

## In Progress

None.

## Done

- [x] **ARC-001: Create a game introduction**
  - Owner: Arcane Buddy. Completion and verification reported.
  - [x] Explain the game and how to win, including payline and wand payouts.
  - [x] Explain potion brewing and activation.
  - [x] Explain the spell book, including the five-spin Tome/Grimoire behavior.
  - [x] Automatically show the introduction on first open without reopening it automatically on reload.
  - [x] Reopen the same Arcane-owned introduction through **How to Win** and the **?** shortcut.
  - [x] Keep the introduction scoped to Arcane Archives and preserve other games' help display. Wukong retained its original help in the comparison check.
  - Verification reported by Arcane Buddy: Playwright on the paid route with mocked account/state passed first open, reload, manual reopening, Escape, focus return, M-to-mute inside the dialog, and touchscreen open/close. At 1440×900 the full guide and action button fit; at 390×844 it scrolls vertically without horizontal overflow and both close controls are 44 px. Runtime/Arcane builds, host typecheck, 30 focused tests, and web lint passed.

- [x] **ARC-002: Finish cauldron animations**
  - Owner: Arcane Buddy. Completion and verification reported.
  - [x] Create the cauldron artwork.
  - [x] Show settled reel ingredients moving into the cauldron. Arcane Buddy reports passing browser checks with mocked spins at 1920×1080, 768×1024, 390×844, and reduced-motion 360×780: each visible ingredient flies once, duplicate Tome landings show ×2, desktop flights end at the cauldron opening, and reload does not replay them.
  - [x] Animate stirring the pot when ingredients are added, synchronized with their arrival in the cauldron.
  - [x] Verify desktop and phone behavior, reduced motion, and no replay on reload, while preserving the ingredient-flight and bottling sequence.
  - Completion verification reported by Arcane Buddy: 1920×1080 and 390×844 passed ingredient flights → stirring → decanting → bottle transfer, including Tome ×2 landings and a regular collection that stirs without bottling. Reduced-motion 360×780 uses a brief static cue without movement or layout shift; reload does not replay the sequence. Typecheck, Arcane build, package boundary verification, and git diff --check passed.

- [x] **ARC-005: Add user-supplied sound effects**
  - Owner: Arcane Buddy. Completion and verification reported.
  - Play `MagicalShimmer_S08CT.105.wav` when clicking the gear/settings icon, **How to Win** button, wager-amount button that opens the slider, or symbol-value button.
  - Keep the wager slider's existing sound unchanged; the new sound applies to the button that opens it.
  - Button-click sound source supplied by the user: `C:\Users\Lctiv\Documents\Splice\Samples\packs\Cartoon Land\MagicalShimmer_S08CT.105.wav`.
  - Play `ESM_QG_Cinematic_FX_magic_crystal_swoosh_poof_collect_01.wav` when the spin button is pressed.
  - Spin-button sound source supplied by the user: `C:\Users\Lctiv\Documents\Splice\Samples\packs\Quest Game\Epic_Stock_Media_-_Quest_Game\one_shots\Magic\ESM_QG_Cinematic_FX_magic_crystal_swoosh_poof_collect_01.wav`.
  - Play `ESM_QG_Cinematic_FX_magic_loot_crate_open.wav` for a normal payline win.
  - Normal payline win sound source supplied by the user: `C:\Users\Lctiv\Documents\Splice\Samples\packs\Quest Game\Epic_Stock_Media_-_Quest_Game\one_shots\Magic\ESM_QG_Cinematic_FX_magic_loot_crate_open.wav`.
  - Play `ESM_QG_Cinematic_FX_magic_gems_glimmer_award_spell.wav` for wand wins and constellation wins.
  - This sound overrides the normal payline win sound. When a wand or constellation win occurs alongside a normal payline win, suppress the normal payline win sound.
  - Wand and constellation win sound source supplied by the user: `C:\Users\Lctiv\Documents\Splice\Samples\packs\Quest Game\Epic_Stock_Media_-_Quest_Game\one_shots\Magic\ESM_QG_Cinematic_FX_magic_gems_glimmer_award_spell.wav`.
  - Use both `ESM_QG_Cinematic_FX_magic_instant_dark_cast_spell_01.wav` and `ESM_QG_Cinematic_FX_magic_soft_airy_whoosh.wav` when potions are being bottled, alongside the bottling animation in ARC-004.
  - Potion-bottling sound sources supplied by the user:
    - `C:\Users\Lctiv\Documents\Splice\Samples\packs\Quest Game\Epic_Stock_Media_-_Quest_Game\one_shots\Magic\ESM_QG_Cinematic_FX_magic_instant_dark_cast_spell_01.wav`.
    - `C:\Users\Lctiv\Documents\Splice\Samples\packs\Quest Game\Epic_Stock_Media_-_Quest_Game\one_shots\Magic\ESM_QG_Cinematic_FX_magic_soft_airy_whoosh.wav`.
  - Use both `ESM_Magic_Game_Spell_Cast_4_Fantasy_Cast_Craft_Mobile_App_Special_Touch_Pickup.wav` and `ESM_QG_Cinematic_FX_magic_win_level_positive_up_02.wav` when a potion is used, alongside the potion-use animation in ARC-004.
  - Potion-use sound sources supplied by the user:
    - `C:\Users\Lctiv\Documents\Splice\Samples\packs\Phantom Magic\Phantom_Magic\Magic_Spells_and_Dust\ESM_Magic_Game_Spell_Cast_4_Fantasy_Cast_Craft_Mobile_App_Special_Touch_Pickup.wav`.
    - `C:\Users\Lctiv\Documents\Splice\Samples\packs\Quest Game\Epic_Stock_Media_-_Quest_Game\one_shots\Magic\ESM_QG_Cinematic_FX_magic_win_level_positive_up_02.wav`.
  - The user already has the other sound effects. The implementing agent must request any remaining sound files from the user before adding them to the game.
  - Verify event timing, special-win sound priority, existing audio settings, unchanged wager-slider sound, and isolation from other games on desktop and phone.
  - Completion verification reported by Arcane Buddy: all eight supplied WAVs are packaged as Arcane-owned, decode-checked Ogg assets. Instrumented Chromium playback passed at 1920×1080, 390×844 touch, and reduced-motion 360×780 touch: all mapped controls/events, unchanged slider cue, one manual-spin cue, normal win cue, special-win precedence, both bottling/use sounds synchronized to effects, silence on rejected activation, results-only audio settings, M/reload behavior, and no replay after reload. Candy Carnival retained its existing click. Arcane/runtime builds, host typecheck, and 13 focused audio tests passed.

- [x] **ARC-006: Create a mobile panel for the lectern, cauldron, and potion shelf**
  - Owner: Arcane Buddy. Completion and verification reported.
  - On cell phone screens, provide a panel that lets players open the lectern, cauldron, and potion shelf.
  - Verify touch access to all three sections, open/close and focus behavior, scrolling without horizontal overflow, and unobstructed game controls on phone screens.
  - Completion verification reported by Arcane Buddy: Playwright passed all three views with current state at 390×844, 360×780, landscape 844×390 touch, and 1920×1080 desktop. Arcane-only dialog controls are 44 px, Close stays visible while scrolling, and landscape triggers remain reachable without resizing the cabinet. Vertical scrolling, no horizontal clipping, Escape/focus return, M while focused, return to an accepted spin, and desktop stir-to-decant parity passed. Arcane build and package verification passed.

- [x] **ARC-007: Show mobile potion options, cauldron status, and the active tome page**
  - Owner: Arcane Buddy. Completion and verification reported.
  - On cell phone screens, show potion options and cauldron status outside the panel from ARC-006.
  - Also show which tome page is currently active outside that panel.
  - Keep this information visible without requiring players to open the panel.
  - Verify empty and populated potion states, brewing progress, active/inactive Tome states, live updates, and phone portrait/landscape layout with the panel closed.
  - Completion verification reported by Arcane Buddy: with dialogs closed, phones display ready potion names/duplicate counts or an empty state, cauldron progress updated at ingredient arrival, active potion/countdown, and the actually active Tome page/countdown or No active Tome page even when another page is selected. Browser checks passed at 390×844, 360×780, landscape 844×390 and 932×430, and 1920×1080 desktop, including 6→7→1 brewing progress, activation/expiry, reload without replay, touch mute/wager, unobstructed controls, and desktop stirring/decanting/transfer. Builds and package verification passed.

- [x] **ARC-003: Finish tome pages and page-flipping animations**
  - Owner: Arcane Buddy. Completion and verification reported; existing lectern/tome and page-turn assets were reused.
  - Page-turn assets: `output/artwork/arcane-archives-2026-11-18/cauldron/page-flip-v1/`, including `animation.json` timing/placement and blank-page overlays. These assets are integrated into the live Tome dialog with readable server-owned page names and ingredients.
  - [x] Create the lectern artwork.
  - [x] Create the tome artwork.
  - [x] Design the three Tome pages for the player to flip through, using server-owned names and ingredients.
  - [x] Integrate page-flipping animations with page navigation and verify desktop, phone, and reduced-motion behavior while preserving the existing Tome rules.
  - Completion verification reported by Arcane Buddy: Playwright passed at 1440×900, 390×844 portrait, and 844×390 phone landscape, covering turns in both directions, touch/arrow navigation, selection/reopen/reload, Escape/focus return, M, 44 px targets, and no horizontal overflow. Grimoire activation locked the page for five paid spins and updated the outside indicator. Reduced motion switches pages immediately without loading the atlas or replaying on reload. Wukong help remained unchanged. Arcane build, host typecheck, and all 20 package boundary checks passed.

- [x] **ARC-004: Finish potion shelf and bottle artwork with potion animations**
  - Owner: Arcane Buddy. Completion and verification reported.
  - [x] Create and integrate potion-bottle artwork for all six variants.
  - [x] Create and integrate dedicated potion-shelf artwork, matching the existing Arcane art. Show an empty shelf with no bottles, then populate it from the saved potion inventory.
  - [x] Verify that the shelf starts empty when there are no bottled potions. Arcane Buddy reports zero count and an empty message on initial load and reload at 1920×1080 and 390×844.
  - [x] Add completed potions to the shelf as they are made in the cauldron.
  - [x] Animate automatic decanting after the last ingredient arrives, then transfer the matching bottle to the shelf.
  - [x] Animate successful potion use/activation and display its ongoing reel effects.
  - Verification reported by Arcane Buddy: bottle, decanting, shelf-transfer, activation, and ongoing-effect checks passed at 1920×1080 and 390×844. Reduced-motion 360×780 uses a static aura; reload restores state without replaying animations.
  - [x] Verify the new shelf artwork with empty/populated inventory, bottling transfer, potion use, reload, and desktop/phone portrait/landscape layouts while preserving the existing controls and animations.
  - Final verification reported by Arcane Buddy: dedicated transparent walnut/brass shelf art is integrated into the desktop workbench and shelf dialog; saved bottle sprites are shown dynamically while the full named inventory and Use controls remain available. Bare shelf, one/multiple bottles, stir → decant → transfer alignment within 1 px, activation consumption, three-spin effect expiry, reload with no replay, and static reduced-motion aura passed at 1920×1080, 390×844 touch, and 844×390 touch/reduced motion without art/control overlap or overflow. Arcane build, host typecheck, and package boundaries passed.
  - Runtime artwork: `games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/src/assets/art/optimized/arcane-archives-potion-shelf-v1.webp`. Source PNG and ImageGen prompt: `output/artwork/arcane-archives-2026-11-18/potions-v1/potion-shelf/`.
