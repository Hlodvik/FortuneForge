# Slot game template

Copy this directory to `src/games/slots/<themeName>` and rename `manifest.template.ts.txt` to `manifest.ts`. A game is assembled from typed, replaceable sets rather than by copying the shared reel controller or Wukong.

## What every game supplies

- a unique manifest ID, catalog card, play route, and demo route;
- a cabinet theme, symbol set, sound set, rules set, and optional mascot;
- help text that accurately describes that game's configured math;
- a server game ID in `createSlotRulesSet(...)`.

Register the finished manifest once in `src/games/slots/index.ts`. The catalog, routing, page title, authenticated route, and balance-free demo route are then derived from it.

## Optional features

The shared slot shell supports optional `energy`, `collections`, and `moneyGrab` capabilities. Omit a capability and its meter, animation, and help content are not rendered. A feature may reference only symbols defined by that game's own symbol set.

Theme-specific features should follow the same pattern: define a small typed capability, keep its art and copy inside the game's folder, and make the shared view render it only when that capability exists. Do not add another game's symbols or rules to a new manifest as a shortcut.

## New math versus a new skin

A visual theme may point at an existing compatible server game ID and declare `serverSymbolSetId` when its client artwork maps to the same server symbols. A game with new reels, payouts, bonuses, or state requires a matching server definition and deterministic tests. Never alter RTP, probability, or payout behavior only in the client.

## Finish checklist

1. Define every initial-reel, guide, energy, collection, and money-grab symbol in the game's symbol set.
2. Add both play and demo routes; route IDs must be unique.
3. Verify the game has no imports from another game's folder.
4. Test normal and reduced-motion sources, dialogs, optional feature visibility, and mobile layout.
5. Run client tests, lint, and the production build before publishing.
