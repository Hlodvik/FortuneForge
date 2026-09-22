# Horse Flight client package

Horse Flight owns its rendered runtime, styles, type declarations, and all visual media in this
package. The application shell consumes the package through its package export; it must not carry
Horse Flight image files in `public/` or another game's asset tree.

```text
assets/
  horse/sprites/       Player run, jump, slide, and rear-facing sprite sheets
  backgrounds/         Distant scene layers
  midgrounds/          Parallax scene layers between background and track
  obstacles/           Collision obstacles
  hazards/             Animated opponents and environmental hazards
  platforms/           Platform artwork when introduced
dist/                  Published ESM, CSS, and declaration files
```

The baseball-style low-motion sheet is named `slide-sprite-sheet-v1.png` and is what the current
right-click slide action uses. The faster run sheet stays separate; a dedicated dash mechanic has
not been enabled by this package yet.
