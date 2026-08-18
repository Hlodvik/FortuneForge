# Slot games

Every slot game has its own directory in this folder. Keep all game-specific code there:

- `manifest.ts` owns features, help text, and the assembled runtime experience;
- `catalog.ts` owns the library card copy and thumbnail assets without importing runtime symbols or themes;
- `cabinetTheme.ts` owns cabinet artwork and palette when the game has a custom cabinet;
- `symbols.ts` owns the game's symbol definitions;
- `mascot.ts` owns theme-specific mascot assets and timing when a game supplies one;
- `visuals.ts` owns generated theme artwork when applicable.

Only reusable, theme-neutral factories, types, renderers, and styles belong in `shared/`. Register each game once in the lightweight `routeRegistry.ts` with static title/backdrop metadata and a dynamic manifest loader. The app shell and page titles use that metadata without importing game assets; the slot library imports catalog modules only when its route is visited.

Use `_template/` to start a new game. Never place multiple game themes into one source file.
