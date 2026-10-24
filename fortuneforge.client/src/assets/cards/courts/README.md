# Retired court illustration experiments

These portraits are retained for reference only and are **not used or shipped**
by the current shared card renderer. The approved Jack sword, arched Queen crown,
and pointed King crown are inline SVGs in
`src/games/cards/shared/CourtCardSymbol.tsx`, inheriting the card's black/red ink.
Do not reintroduce these detailed portraits when updating a game.

The twelve WebP files contain simplified, single-ink mirrored Jack, Queen, and
King figures: black for clubs/spades and red for hearts/diamonds. The decorative
blue, yellow, and other coloured fills have been removed. They have transparent surroundings: no card background, inset
border, corner rank, or corner suit is embedded. `PlayingCard` supplies the
existing shared card surface and corner indices; number cards and backs are not
changed.

Source: the alternate `jack_of_*2.svg`, `queen_of_*2.svg`, and `king_of_*2.svg`
illustrations already present in the sibling `CardGames/wwwroot/Assets/Images`
directory. The source SVG headers identify Byron Knoll's vector-playing-cards
project: <http://code.google.com/p/vector-playing-cards/>. Its maintained mirror
documents the original artwork as public domain, with optional WTFPL licensing:
<https://github.com/notpeter/Vector-Playing-Cards#license>.

`scripts/prepare-court-art.mjs` extracts the court-figure group, excludes the
separate inner-frame and corner groups, retains only the dark illustration and
facial linework in the suit's ink colour, and exports each portrait at 200 pixels
wide. Only these lightweight derivatives ship, not the multi-megabyte SVG deck.
Images are requested only for visible court cards; face-down cards do not include
an illustration element.
