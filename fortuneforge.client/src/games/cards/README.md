# Card client boundaries

Card client dependencies flow in one direction:

`pages/cards` → `games/cards/<game>` → `games/cards/shared`

- `pages/cards` owns route composition, page-only styles, route tests, and previews.
- Each game folder owns that game's domain, API, models, reusable components, and tests.
- `shared` owns only reusable card models, deck/RNG helpers, `PlayingCard`, and shared card styles.
- Games must not import pages or another game. Shared code must not import a specific game.
- Client API modules must not import route or UI modules.
