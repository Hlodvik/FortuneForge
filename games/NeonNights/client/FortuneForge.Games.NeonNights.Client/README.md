# Neon Nights client

Neon owns its entry component, controller, result/state validation, reel/special-round sequencing, paytable presentation, help, art, audio and videos. The host injects authenticated HTTP transport, account identity, the wallet/navbar shell and optional payment alerts. It does not supply gameplay rules.

Stable render/audio/dialog/animation primitives come from `@fortuneforge/slot-client-core`; measured payline rendering comes from `@fortuneforge/client-presentation/slots/paylines`. Neither package imports a game.

The host lazily imports this package only for `/slots/neon-nights` and `/slots/neon-nights/demo`. Its lobby uses a separate small catalogue thumbnail, so visiting another game does not load Neon.

From the repository root:

```sh
npm run check --workspace @fortuneforge/games-neon-nights
npm run build --workspace @fortuneforge/games-neon-nights
npm run test --workspace @fortuneforge/games-neon-nights
npm run verify:neon-standalone
npm run verify:neon-bundle
```

Build prerequisites are the two neutral packages. The root workspace runner orders them automatically. Backend HTTP paths remain compatible with the platform gateway; Neon result math and state transitions live in the Neon backend project.
