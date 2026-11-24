# Sunstone Eclipse slot client

An original temple-and-sun slot presentation. The public game is Sunstone Eclipse;
the package and server identifiers remain stable so saved rounds, balances and
analytics continue to resolve. The public play route is `/slots/sunstone-eclipse`.

This package owns its manifest, catalogue metadata, symbols, cabinet
configuration, styles, audio contract and in-game media. Authentication,
wallet settlement, Firebase hosting and the shared Fortune Forge navbar remain
host responsibilities.

The game-owned Sunwheel panel renders the settled server feature. One Solar or
Lunar Eclipse on the centre reel activates five paid rounds at the triggering
wager. Each Eclipse round presents three distinct alignments on the same board,
retains guardian awakenings for that round, and reveals Totality when all three
awaken. Solar turns clockwise; Lunar turns anticlockwise. The host persists the
paid mode and locks the wager. The panel reports its three-step presentation
interval to the host so another wager cannot interrupt it; reduced motion
presents the final result immediately.

Hover, focus or touch a guardian to inspect its real path over the reels.
The client never draws outcomes or calculates wallet awards.
An absent feature from a resumed older round is supported; malformed or foreign
feature data cannot create a displayed award.

The illustrated introduction includes the complete feature rules and obtains
ordinary payline values from the authoritative exported engine guide. Eclipse
line values and wheel calibration come from `src/eclipseMath.generated.ts`,
regenerated from native game constants before checks and builds. Current
contract-v3 sequences and historical contract-v1/v2 features are validated
separately, so owed free bonuses retain their original presentation and rules.
Keyboard path inspection and live result announcements are supported.

Five native carved glyphs and a separate Wild live in `src/assets/art/fifth-sun-glyphs/`.
The named Sunstone Eclipse wire symbols are used for new games; rendering-only legacy
aliases support owed historical bonuses without changing their financial evidence.
The Fraunces variable display font is bundled with its original OFL license in
`src/assets/fonts/`.

Original art lives in `src/assets/art/sunstone-eclipse/`. Original synthesized
audio lives in `src/assets/audio/sunstone-eclipse/`; regenerate those WAV cues with
`node scripts/generate-sunstone-audio.mjs`. No third-party recordings are used.

