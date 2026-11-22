# Sunstone Eclipse slot client

An original temple-and-sun slot presentation. The public game is Sunstone Eclipse;
the package and server identifiers remain stable so saved rounds, balances and
analytics continue to resolve. The public play route is `/slots/sunstone-eclipse`.

This package owns its manifest, catalogue metadata, symbols, cabinet
configuration, styles, audio contract and in-game media. Authentication,
wallet settlement, Firebase hosting and the shared Fortune Forge navbar remain
host responsibilities.

The game-owned Sunwheel Alignment panel renders the settled server feature:
three independently rotated guardian paths, aligned-ring multipliers and the
central Eclipse tribute. Hover, focus or touch a guardian to inspect its real
path over the reels. The client never draws outcomes or calculates wallet awards.
An absent feature from a resumed older round is supported; malformed or foreign
feature data cannot create a displayed award.

The illustrated introduction includes the complete feature rules and obtains
ordinary payline values from the authoritative exported engine guide. Reduced
motion, keyboard path inspection and live result announcements are supported.

Original art lives in `src/assets/art/sunstone-eclipse/`. Original synthesized
audio lives in `src/assets/audio/sunstone-eclipse/`; regenerate those WAV cues with
`node scripts/generate-sunstone-audio.mjs`. No third-party recordings are used.

