# Original Crooked Road sound suite

All 11 sounds are synthesized from original code in
`games/CrookedRoad/tools/render-audio.py`. No recordings, sampled instruments,
existing songs or other game audio are used. Run that renderer from any working
directory with Python 3.10 or newer; it uses only the standard library and writes
the same seeded suite back here.

The harp timbre uses a damped string model. Stone and coin sounds combine short
modal resonances with filtered seeded noise. The quiet eight-second harp loop
contains an original four-bar phrase in three beats; tails wrap across its
boundary. One-shot endings fade to zero. The reel-roll loop is 0.96 seconds.

Outputs are mono, 22,050 Hz, 16-bit PCM WAV and total 829,126 bytes. The renderer
checks for clipping and excessive loop-boundary steps and prints peak and RMS
levels. WAV keeps the renderer dependency-free; ffmpeg was not on the execution
host's PATH when the suite was created.

`src/sounds.ts` owns cue imports, conservative levels and event mappings. All
playback goes through the existing host sound set, so mute, result-only mode and
volume controls continue to apply. There are no custom audio listeners or
independent playback loops. Journey entry and completion cues are exposed via
the host's normal bonus lifecycle events.
