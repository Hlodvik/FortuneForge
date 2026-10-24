# Royal Indian table pass

The existing two-pile/Joker, betting and control layout is retained. The table now has restrained palace arches, jali lattice and paisley corner engraving, rather than religious imagery. Existing emerald felt and curved black-leather rail remain.

## Image asset

Built-in image-generation edit of `client/FortuneForge.Games.AndarBahar.Client/src/assets/emerald-table.webp`, saved non-destructively as `client/FortuneForge.Games.AndarBahar.Client/src/assets/palace-emerald-table.webp`. The generated 1536×1024 output was encoded as WebP at quality 82: 212,342 bytes. The old artwork remains available for comparison.

Final prompt:

> Use case: precise-object-edit. Asset type: lightweight 1536 x 1024 casino game table background. Image 1 is the edit target. Preserve the exact emerald-green felt hue, soft lighting, fine felt texture, large uncluttered central playing area, and curved black-leather bottom rail with thin warm-gold trim. Change only the decorative gold ornamentation around the outer margins into a restrained royal Indian palace style: delicate carved scalloped palace arches at the very top and sides, tiny jali lattice details, graceful paisley botanical corner filigree in muted antique gold. Keep ornament confined to the outermost 10% on left/right and 8% top so all cards, headings, controls and betting zones remain clear. The result must feel like the same tasteful polished rendered casino game, not a photograph of a room, not a new UI. Thin understated engraved decoration, low contrast, elegant rather than busy. No text, no letters, no numbers, no chips, no cards, no deities or religious/sacred symbols. Keep existing camera, framing, background dimensions, bottom curve and centre empty. Do not brighten or change the felt color.

## Typography and sound

Sora and Fraunces are locally bundled, with their OFL licenses, matching the established FortuneForge typography. Card ranks/pips retain their conventional card typography.

Existing Kenney CC0 chip/card sounds are retained. Enabled utility buttons use a short dry click. Positive-net results use an original 1.3-second physically modeled plucked-string/hand-drum cue, not a traditional instrument recording or third-party sample. It is generated deterministically by `tools/generate-palace-cue.mjs` and is 62,444 bytes. Losses use a neutral card tap. There is no automatically playing music loop, and restored results cannot start the musical cue without a prior table interaction. Muting stops current playback.
