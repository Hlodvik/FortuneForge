# FortuneForge original themed audio, version 1

Sixteen title-specific suites, with twenty Ogg Vorbis cues each. All material is original offline synthesis. Neon Nights retains its exact approved PCM source composition. The other fifteen suites have distinct instrument palettes, motifs, scales, harmonies and rhythms fitting their titles. No external music or effects recordings are included.

Arcane Archives selects one additional CC0 loop, MintoDog's `magic-puzzle-in-game-1.ogg`, for its active music. It continues through bonus rounds at a 0.28 base gain. Its generated `night-drive.ogg` and `midnight-mix.ogg` remain as inactive source assets. See [the Arcane music source note](arcane-archives/MAGIC-PUZZLE-SOURCE.md) for the creator page, license and source hash. The original-synthesis declaration applies to Arcane's baseline twenty tracks, not this added recording.

Runtime URL: `/assets/slots/audio-suites/v1/{slug}/{cue-id}.ogg`. The manifest at this directory and each title's `manifest.json` provide all URLs, loop flags, recommended playback gains, measured source/decoded levels, durations, content hashes and source provenance. Generic collection cue filenames are shared identifiers; each suite provides its own four differentiated sounds.

Reproducible source: `tools/audio-suites/render-themed-suites.mjs`. Original PCM masters remain in `output/artwork/themed-slots-audio-2026-11-02/masters`, with approved Neon masters at `output/artwork/neon-nights-2026-11-02/audio-suite-v1`. Runtime assets are pre-rendered 44.1 kHz stereo, Vorbis quality 5. Every file is decoded and checked during rendering. Midnight Vault, Wukong and Pirates retain their existing audio. Gold Foundry supplies its own palette under `/assets/slots/gold-foundry/v1/audio/`.
