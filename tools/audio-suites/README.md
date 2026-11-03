# Original themed slot audio suites

Run from the repository root:

```powershell
node tools/audio-suites/render-themed-suites.mjs
```

The generator uses Node.js built-ins and a local FFmpeg executable. Set `FF_AUDIO_FFMPEG` to override the bundled executable path. Optional command arguments are title slugs to rerender; untouched title manifests remain in the aggregate.

The twenty baseline cues per title are original synthesized material written for FortuneForge. No external recordings, copyrighted reference tracks, downloaded effects, services or third-party synthesis libraries are used by this generator. The instrument names describe additive/FM instrument models, not recordings of acoustic instruments. Title-specific scales, motifs, harmonies, meters, rhythms, timbres and arrangements give the sixteen new titles distinct sound identities. Neon Nights is encoded from the unchanged, separately approved original WAV masters at `output/artwork/neon-nights-2026-11-02/audio-suite-v1`.

Runtime selections may include separately documented additions outside this generator. Neon Nights selects a CC0 voice callout for paid wins, Arcane Archives selects MintoDog's CC0 `magic-puzzle-in-game-1.ogg` for continuous base and bonus music, and Gods of Olympus selects Viktor Kraus's CC BY 3.0 `secret-sanctum-v1.ogg` for base music. Their per-title manifests document these additions. Preserve the added track entries when rerendering these titles.

Runtime assets are 44.1 kHz stereo Ogg Vorbis quality 5 at `fortuneforge.client/public/assets/slots/audio-suites/v1/{slug}/{cue}.ogg`. Twenty stable cue identifiers per title cover base/bonus music, controls, base/bonus reels, four collections, three win tiers, special symbols, neutral no-win, bonus entry and bonus completion. The four collection filenames retain Neon identifiers for the shared runtime contract; their music and thematic intent belong to the corresponding title.

Original new PCM masters are retained under `output/artwork/themed-slots-audio-2026-11-02/masters`. Per-title and aggregate manifests record source hashes, exported hashes, durations, source and decoded RMS/peaks, cue playback volume and synthesis provenance. Every Ogg export is decoded once to verify finite samples, audible content, expected duration and peak headroom. The generator fails on cue contract or audio-bound violations. Music and rolling textures use circular mixing/reverb and short exact-zero loop boundary fades. Effects use short edge fades. This is the reproducible asset pipeline; game code plays files rather than synthesizing instruments.

Existing Midnight Vault (`dragon-hoard`), Wukong and Pirates assets are excluded.
