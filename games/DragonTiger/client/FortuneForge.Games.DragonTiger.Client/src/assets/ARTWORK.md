# Dragon Tiger card back

The approved board image is unchanged. `card-back-asian-v1.jpg` is original artwork generated with the built-in image-generation tool, then downsampled to 256×360 and JPEG-optimized for small on-table cards. The full-size generation is not shipped.

Final generation prompt:

> A single small, production-ready Asian-themed playing-card BACK for a premium Dragon Tiger casino game. Only the flat card-back face, straight-on orthographic, full-bleed, vertical 5:7 proportions; no surrounding scene, angle, external shadow or mockup. Dark charcoal-black lacquer ground with restrained oxblood red, delicately engraved antique-gold Chinese cloud curls, a key-fret inner border, and one central symmetrical Chinese endless-knot / ruyi-cloud medallion. Fine illustrated gold linework, not photorealistic metal; readable at 90px wide, complementing red felt with gold dragon and tiger illustrations. Two-way rotational symmetry. No letters, words, numbers, pseudo-Chinese writing, brands or watermark. Keep the design inside the rectangular card face; the UI rounds the corners. Intended final asset is 256×360 pixels; prioritize a crisp silhouette and modest detail.

Local fonts are bundled with their SIL Open Font Licenses in `fonts/`. The Ma Shan Zheng subset contains only the three displayed Hanzi, 龙、和、虎, to avoid shipping a large CJK font. Simplified 龙 is used because the selected brush font does not include Traditional 龍; this prevents a plain-font fallback.
