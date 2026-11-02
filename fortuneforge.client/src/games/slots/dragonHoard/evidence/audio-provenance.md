# Midnight Vault audio and music

Reviewed 2026-10-31 (Central time). The historical sound-set ID `dragon-hoard-audio-v1`, semantic event IDs, cue categories, reel loop flags, player preferences and gameplay contracts remain unchanged. A licensed noir recording was downloaded at the user's request for ambient theme music. Existing CC0 effects remain; no sounds were generated or encoded. The separate [catalogue audio decision](../../shared/evidence/audio-provenance.md) documents the remaining nineteen titles' unresolved base music and shared CC0 fallback.

## Current music: I Knew a Guy

**I Knew a Guy**, composed by **Kevin MacLeod**, is listed by the composer as dark, mysterious jazz at 75 BPM, with bass, spare drums and vibraphone. That instrumentation informed its selection for an understated noir/heist atmosphere. The recording was not auditioned in this task, and musical suitability remains subject to the owner's listening review.

- [Official track page, ISRC USUAN1100199](https://incompetech.com/music/royalty-free/index.html?Search=Search&isrc=USUAN1100199)
- [Composer's actual catalogue data](https://incompetech.com/music/royalty-free/pieces.json)
- [Official downloadable recording](https://incompetech.com/music/royalty-free/mp3-royaltyfree/I%20Knew%20a%20Guy.mp3)
- [Composer's licensing page](https://incompetech.com/music/royalty-free/licenses/)
- [CC BY 4.0 license](https://creativecommons.org/licenses/by/4.0/)

The official track page's HTML supplies the title-specific CC BY 4.0 credit. Its actual download template and catalogue `filename` field supplied the download URL; it was not guessed. The composer's licensing page identifies a free credited option. CC BY 4.0 permits commercial redistribution and adaptation with appropriate credit, a license link and identification of changes. The shipped recording is unmodified; this choice permits ordinary audio-file delivery with the required attribution.

Public asset: `public/assets/slots/midnight-vault/v3/audio/i-knew-a-guy-kevin-macleod.mp3` — **6,044,335 bytes**, SHA-256 `10e3480eefcbefe839f69dfff27d99991e16c23981072fca769f38eb21aab50c`. HTTP download succeeded and its ID3 header/MPEG-1 Layer III frame were inspected (44.1 kHz stereo); this is file-format validation, not listening or a complete decoder test. The publisher lists duration 2:47. No transcoder was available, so the original file was retained rather than installing software. Playback repeats the whole recording; seamless loop transitions are not claimed.

The target ambience cue selects this local file with `enabled: true`, `loop: true`, effect category and base volume 0.04. The existing volume/mute/results-only controls apply. Playback must begin following a player interaction, with the coordinating controller work providing that guard; no claim is made here about a guard this file does not own. The file is not preloaded by the sound configuration.

The matching public `CREDITS.txt` retains this exact credit and links. The game's visible help must also include it:

> "I Knew a Guy" Kevin MacLeod (incompetech.com)  
> Licensed under Creative Commons: By Attribution 4.0 License  
> https://creativecommons.org/licenses/by/4.0/

## Retired OpenTracks candidate

`src/assets/slots/ambient/cyber-night.mp3` (2,528,940 encoded bytes) was the earlier candidate. Existing credits identify it as **踏み込めばサイバー的なBGM**, by **TAKAO Masaki**, track 8334. Its cool electronic description informed that earlier selection. Midnight Vault no longer selects, requests or plays this recording. The source file remains preserved; shared slot configuration now retains only a disabled non-fetching identifier where that library is still recorded. The track was not auditioned in this task.

- [Official track page](https://dova-s.jp/en/bgm/detail/8334)
- [Creator profile and terms](https://opentracks.com/en/creator/detail/62)
- [Audio source license](https://opentracks.com/en/help/articles/license/)
- [Permitted uses, including game BGM](https://opentracks.com/en/help/articles/license-usage/)

OpenTracks, formerly DOVA-SYNDROME, permits commercial in-game background music and editing/looping while separately restricting standalone-file access/duplication in prohibitions 7–8. Ordinary Vite MP3 delivery was not cleared against that condition. The retired target recording was therefore omitted; the new CC BY score above supplies music through a different license. This does not certify packaging compliance for older recordings elsewhere in the application. No encoding workaround or deployment was performed.

## Existing CC0 effects selected

The target uses the already-installed **Kenney Casino Audio 1.1** clips in `src/assets/audio/cards`. The retained `License.txt` and `README.md` identify CC0 and the official pack page independently labels the pack CC0: [Kenney Casino Audio](https://kenney.nl/assets/casino-audio). No new asset copies are needed. The same files are already used by the shared card-game audio module.

| Target event | Existing source | Base volume | File bytes |
|---|---|---:|---:|
| Lever/start | `chip-lay-1.ogg` |0.26|5,720|
| Reel motion | `card-shuffle.ogg` |0.10|30,002|
| Reel stop | `chip-lay-1.ogg` |0.16|same file|
| Ordinary win | `chips-handle-1.ogg` |0.22|9,374|
| Premium result | `chips-collide-1.ogg` |0.28|6,356|
| Bonus result | `chips-collide-1.ogg` |0.30|same file|
| Five-symbol result | `chips-handle-1.ogg` |0.30|same file|
| Miss | `card-place-1.ogg` |0.08|9,624|

The five distinct currently reachable effect files total **61,076 encoded bytes**. Event IDs remain `lever-pull`, `reel-spin`, `reel-stop` and the established result IDs. Reel motion retains `loop: false`; categories and engine behavior are unchanged. Effects retain their restrained levels. The background score uses base volume 0.04, and existing mute/results-only/volume preferences apply to all cues.

A source-specific license record for the former generic shared slot WAVs was not located, so the target's reachable effects use Kenney. The [shared audio evidence](../../shared/evidence/audio-provenance.md) records the catalogue fallback; unused inherited cue definitions retain their established event identities. No other game's music is enabled by this target-specific licensed-track addition.

Music and effects were not auditioned in this task. Encoded bytes describe local assets, not measured browser download totals. Focused tests verify the local score mapping and lazy `Audio` allocation, preserve disabled unresolved shared music, and cover all twenty catalogue sound sets. The coordinating owner owns visible credits, user-interaction playback gating and final browser/network validation.

Current focused validation: **10 tests passed across 3 files** (`soundSets.test.ts`, `slotAudioCoverage.test.ts`, and this game's `audio.test.ts`). The score integrity test checks its exact byte count, ID3 header, SHA-256 and matching attribution file. Owned tracked files passed `git diff --check`.
