# Catalogue slot audio — source and delivery decisions

Checked October 31, 2026 (Central time). This record covers the twenty current slot sound sets plus their shared default. It records a concrete delivery choice and source evidence; it does not certify legal compliance or claim the files were auditioned.

## Recorded music retained but disabled

The primary [OpenTracks audio-source license](https://opentracks.com/en/help/articles/license/) permits commercial background use in apps and games and processing/looping. Its separate prohibitions 7–8 restrict distribution permitting standalone audio use and easy duplication of the audio file. The [permitted-use explanation](https://opentracks.com/en/help/articles/license-usage/) confirms app/game use remains subject to the full license and applicable creator terms.

The former Vite MP3 imports emitted ordinary downloadable audio asset URLs. This task did not establish that delivery satisfied those separate restrictions. The chosen implementation therefore disables this music across the catalogue. The cue source retains a non-fetching `disabled:opentracks/<filename>.mp3` identifier and `enabled: false`; the audio hook rejects disabled cues before constructing `Audio`. The MP3 imports were removed so a new clean bundle does not emit these recordings through this module. Existing source files remain untouched. Earlier build directories and any previously published assets are outside this change; this is not a cleanup or deployment.

The separate non-slot `src/features/audio/ambientGameMusic.ts` still imports `cyber-night.mp3`, `forest-journey.mp3` and `haunted-chimes.mp3`. It was outside this file ownership and remains unchanged. Consequently a whole-app build may still emit those three recordings through that module. This slot-only change does not close that separate delivery issue or establish whole-app audio clearance.

All sound-set IDs, cue IDs, semantic event selections, existing categories, loop flags and volumes remain. This preserves the recorded OpenTracks selection for a future cleared delivery decision. Reel Riches' original procedural bonus-only arrangement remains active. At the user's subsequent request, Midnight Vault now uses the separately licensed CC BY 4.0 noir recording [I Knew a Guy](../../dragonHoard/evidence/audio-provenance.md); its earlier OpenTracks candidate remains retired. The other nineteen base music selections remain disabled.

| Existing source filename under `src/assets/slots/ambient/` | Recorded track page | Catalogue selections |
|---|---|---|
| `ancient-sword.mp3` | [An Ancient Sword, 23147](https://dova-s.jp/en/bgm/detail/23147) | Gods of Olympus |
| `asian-dragon.mp3` | [Asian Dragon, 19980](https://dova-s.jp/en/bgm/detail/19980) | Samurai Fortune; Wukong |
| `carnival-lights.mp3` | [Carnival, 12606](https://dova-s.jp/en/bgm/detail/12606) | Candy Carnival; Rainbow Realm |
| `casino-rock.mp3` | [Casino de Rock, 18657](https://dova-s.jp/en/bgm/detail/18657) | Royal Draw; shared default |
| `country-trail.mp3` | [Wagon Wheel Jazz, 23708](https://dova-s.jp/en/bgm/detail/23708) | High Noon Fortune |
| `cyber-night.mp3` | [Cyber-style BGM, 8334](https://dova-s.jp/en/bgm/detail/8334) | Cosmic Fortune; Neon Nights; Robot Revolution; Midnight Vault |
| `desert-wind.mp3` | [Swirling desert wind, 23035](https://dova-s.jp/en/bgm/detail/23035) | Desert Treasures |
| `forest-journey.mp3` | [A Journey in the Forest, 23670](https://dova-s.jp/en/bgm/detail/23670) | Dino Dominion; Jungle Jackpot |
| `haunted-chimes.mp3` | [Horror chime ambience, 21024](https://dova-s.jp/en/bgm/detail/21024) | Arcane Archives; Phantom Manor |
| `nordic-atmosphere.mp3` | [Nordic Atmosphere, 20973](https://dova-s.jp/en/bgm/detail/20973) | Nordic Legends |
| `pirate-voyage.mp3` | [Pirate Ship Dungeon, 21763](https://dova-s.jp/en/bgm/detail/21763) | Ocean Odyssey; Pirates' Fortune manual/autoplay; Reel Riches base game |

These track mappings come from the retained ambient credits and current sound configuration. The general license pages were read directly for this decision; the table is not a claim of a new track-by-track creator-terms audit. No explicit user-selected music instruction was found in the scoped repository evidence. Recorded game-specific selections remain identifiable and can be reviewed with music off.

## Existing CC0 effects replace unproven generic recordings

The five selected existing files in `src/assets/audio/cards/` come from Kenney Casino Audio 1.1. The retained `License.txt` identifies CC0. The primary [Casino Audio asset page](https://kenney.nl/assets/casino-audio) labels the pack CC0, and [Kenney's support page](https://kenney.nl/support) confirms commercial use and optional attribution. Direct asset-page fetching was restricted by robots during this check; the publisher's search-indexed page and the local pack license agree.

Source-specific provenance was not located for the generic slot recordings below. This does not assert they are unlawfully licensed. Their shared runtime imports were replaced with the documented existing CC0 files; the original source files were preserved.

| Former generic recording | Existing CC0 replacement |
|---|---|
| `lever-coin.wav` | `chip-lay-1.ogg` |
| `lever-register.wav` | `chips-handle-1.ogg` |
| `reel-spin.ogg` | `card-shuffle.ogg` |
| `reel-stop.wav` | `chip-lay-1.ogg` |
| `low-win.wav`; `five-jewel.wav` | `chips-handle-1.ogg` |
| `premium-win.wav`; `five-casino-win.wav` | `chips-collide-1.ogg` |
| `five-twinkle.wav` | `chip-lay-1.ogg` |
| `no-win-water-drop.wav` | `card-place-1.ogg` |

The generic names remain semantic aliases inside the configuration so existing game event mappings retain their identities and relative levels. Midnight Vault's selected chip/card mappings and levels remain. This compact shared palette is a release fallback; per-game sound design and listening review remain open work.

## Existing game-specific effects retained

- **Wukong:** its seven active effects remain the original procedural files documented in `src/assets/slots/audio/WUKONG-SOURCES.md` and reproducible with `tools/generate_wukong_audio.py`. No third-party samples are used according to that retained source record.
- **Reel Riches:** its seven original bonus files remain, including `reel-riches-trophy-loop.wav`, generated by `tools/generate_reel_riches_audio.py` using additive synthesis and deterministic noise. The active bonus arrangement remains separate from the disabled base-game music. Its previously generic bonus loss cue now uses the CC0 card-placement sound.
- **Pirates' Fortune:** its helm, gull and wave recordings remain, with the retained `PIRATES_FORTUNE_SOURCES.md` provenance. The primary publisher's search-indexed pages identify [Spinning Wheel](https://opengameart.org/content/spinning-wheel-0), [Solo Seagull Sound Effects](https://opengameart.org/node/92772) and [Beach Ocean Waves](https://opengameart.org/content/beach-ocean-waves) as CC0. Direct page fetching returned a restriction/403, so this check did not independently inspect their downloadable payloads. Generic cue recordings within the normal/special palette now use the Kenney mappings above. No sounds were downloaded or regenerated.

## Verification boundary

`soundSets.test.ts` enumerates all twenty actual manifests, checks nineteen recorded base music selections remain disabled and Midnight Vault selects its licensed score, validates every enabled cue against documented source families, and exercises `useSlotAudio` with an instrumented `Audio` constructor. Mounting the hook and requesting disabled loops/one-shots allocate nothing, while selected effects and the original Reel Riches bonus loop allocate only on demand. Midnight Vault's focused test covers its separate licensed score. A fresh production-build asset inspection is needed to verify the removed imports do not appear in emitted output. No server, game math, RNG, payouts, settlement, security, routing or artwork is modified here.

Initial fallback validation passed 9 tests. After the licensed Midnight Vault score was added, the current focused run passed **10 tests across 3 files** (`src/features/slots/config/soundSets.test.ts`, `src/games/slots/slotAudioCoverage.test.ts`, `src/games/slots/dragonHoard/audio.test.ts`) using Vitest with one worker and no cache. Owned tracked files passed `git diff --check`. Combined current-source type/lint/build and browser validation remain with the coordinating owner.
