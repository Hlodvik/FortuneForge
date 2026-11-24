# Approved Crooked Road artwork

These WebP exports come from the approved PNG masters in
`C:/Users/Lctiv/.codex/visualizations/2026/11/22/01a2054e-8565-76a1-82d4-40ee1c04b17f/irish-slots-concept`.
Only resizing and format conversion were applied with Pillow. Alpha channels are
preserved. The approved black cast-iron cauldron is
`symbols/crooked-road-pot-of-gold-black-v2.webp`; the rejected green cauldron is
not included.

The 20 exports total 2,394,742 bytes. Reel symbols are 512 × 512; backgrounds are
1672 × 941; the wordmark is 960 × 480; Finbar is 566 × 640; the map board is
768 × 768; road tiles are 256 × 256; the reel surround is 1280 × 853.

`src/symbols.ts` owns all imports. Paying symbols ascend from clover, horseshoe,
lantern, fiddle and harp to fox. ACE is the hat, FREE is the fairy door and POWER
is the trail. Only these nine server-defined symbols are reel symbols. Finbar,
the black gold pot, road pieces and map are separate named artwork in
`CROOKED_ROAD_ARTWORK`; the game does not use shared-builder symbol aliases.

Only straight and junction tile masters are available. The trail symbol supplies
a bend illustration; directional connections in the playable board belong to
its accessible UI and server road state rather than inferred image pixels.
