# Slot client core

Shared presentation and browser transport primitives used by standalone slot
clients. This package has no dependency on the host app or a game package.

It provides reel animation, image rendering, generic cabinet and control widgets,
symbol value presentation, dialog focus handling, keyboard mapping, money display,
and audio playback/preferences. A game supplies its own symbols, sounds, help
content, rules, requests, outcome contracts, state transitions, and visual skin.

Import runtime exports from `@fortuneforge/slot-client-core` and the presentation
foundation from `@fortuneforge/slot-client-core/styles.css`. Deep exports exist for
legacy host compatibility paths. Existing `pirates-spin-controls` CSS names are
retained solely as presentation compatibility names for the shared sidecar
controls; they do not load Pirates artwork or implement Pirates rules.

The host's legacy stylesheets import the matching `styles/*` fragment and retain
only game-owned overrides. Neutral widget styles therefore have one source here.
Components do not automatically load CSS: standalone games import `styles.css`
before their own skin, and legacy consumers keep their existing stylesheet order.

`npm run check --workspace @fortuneforge/slot-client-core` checks this package on
its own. `npm run build --workspace @fortuneforge/slot-client-core` emits its
runtime, declarations, CSS, and the two generic cursor assets.
