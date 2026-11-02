# FortuneForge client presentation

Shared display code for independently built game clients. This package
contains no game rules, card ordering, payouts, gateways or wallet behavior.

`StandardCardFace` renders a named standard playing card using the existing
`ff-playing-card` classes and accessible label. Dragon Tiger and Hi-Lo keep
their typed `DefaultCardFace` adapters and local CSS. Their optional host-owned
`renderCardFace` callbacks still take precedence over this standalone fallback.

```tsx
import { StandardCardFace } from '@fortuneforge/client-presentation'

<StandardCardFace card={{ rank: 'queen', suit: 'hearts' }} />
```

The separate `@fortuneforge/client-presentation/money` entry exports
`formatMoney(value, symbol)`, preserving Baccarat and Video Poker's existing
display convention: literal symbol prefix, minus sign before that prefix,
runtime-default number locale and exactly two fraction digits. Its module has
no React import. Baccarat keeps its additional positive-sign wrapper locally;
other formatting conventions are separate. Video Poker retains its existing
`videoPokerPresentation` named export through a re-export.

The money fixture captures the original helpers across 16 numeric edge cases,
six symbol strings and three controlled default locales. NaN, infinities and
negative zero use tagged fixture inputs so JSON does not collapse them.

Exports resolve compiled JavaScript and declarations in `dist`. React is a peer
dependency and is not bundled here. Game library builds bundle the face markup
while keeping their existing React externals and CSS output.

From the repository root, `npm run games:build`, `npm run games:check` and
`npm run games:test` include this dependency before its consumers. Check/test
also build shared prerequisites so a fresh checkout does not require stale
`dist` files. Before running a consumer's commands individually, run
`npm run build --workspace @fortuneforge/client-presentation` once (and again
after editing this package).

The regression fixture captures all 52 cards from both original game renderers
before extraction. Tests compare the exact resulting HTML, including class
names, labels, corner markup and court-card treatment.
# Player-game knowledge

The `./game-experience` export supplies a typed, data-only context to every playable route through the host's shared loader. Game packages can read `useGameExperience()` without importing host code. It does not add tutorials, change controls, or record tutorial completion.

`hasPlayed` is a boolean only when `status` is `known`. Loading, incomplete historical coverage, errors, guests and demos use `null`; they must not be treated as a first-time player. Only an authenticated server response can establish accepted play or a proven negative. Guest/demo play does not create an authenticated relationship.

