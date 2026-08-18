# Client source layout

The client is organized by responsibility:

- `app/` contains application startup and route selection.
- `pages/` contains route-level React components, grouped by URL/domain (`account`, `auth`, `cards`, `payments`, and `slots`).
- `games/slots/` contains one folder per slot game. A game's manifest, cabinet theme, symbols, mascot configuration, generated visuals, and theme copy belong in its own folder.
- `games/cards/` contains card-game domains and their shared card primitives; route composition stays in `pages/cards/`.
- `features/` contains reusable domain behavior such as account sessions, payment APIs, and the shared slot engine.
- `components/` contains application-wide reusable UI.
- `assets/` contains static media shared by those modules.

Dependencies should flow from pages and game definitions toward shared modules. Shared modules must not import route pages or a specific game's theme.
