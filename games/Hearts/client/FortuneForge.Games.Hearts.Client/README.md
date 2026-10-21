# Fortune Forge Hearts client

Reusable four-seat React table for Hearts. The server owns shuffles, legal actions, scoring, paid entries, matchmaking, deadlines, and payouts. The client presents the viewer's own cards, public trick information, and authoritative session state.

```powershell
npm install
npm run dev
```

Open http://127.0.0.1:5178/?mock=1 for the development-only interactive fixture. It simulates onboarding, a separate one-hand Trial run, Free Play, and fixed-entry competitive tables. Its account setup resets on refresh: it is not real account persistence, payment, or production identity. Without `mock=1`, the preview uses the configured API proxy.

The host can supply shared card artwork through `renderCard`. The standalone fixture uses the package's lightweight card fallback instead. Sound honors mute and animation honors reduced motion.

## Entry and trial

The authenticated host provides `gateway.onboarding`. While its account state is unknown or pending, mode choices are absent. “Learn to play” starts the dedicated trial; “I know how to play” awaits a saved server answer. An already-active paid queue/table is restored without interrupting its deadline.

The Trial run uses the normal practice-action endpoints, but only the server can identify it as a trial and complete onboarding after thirteen tricks. Free Play cannot complete it. Leaving a trial awaits authoritative cancellation; a missing trial is recovered by re-reading onboarding. The on-board coach uses only the viewer's hand and already-public cards, and its suggestions never restrict other legal choices.

## Fonts and assets

The package self-hosts only Latin WOFF2 fonts: [Fraunces](https://github.com/undercasetype/Fraunces) for display typography and [Sora](https://github.com/sora-xor/sora-font) for interface text. Both use the SIL Open Font License; their copyright notices and complete licenses are in `src/assets/fonts/`. No runtime font service is used. Chip artwork reuses the application's approved Three Card Poker style.

Checks:

```powershell
npm run check
npm test
npm run build
```

Do not use the fixture as evidence of server settlement or account persistence. Production hosts should use `HttpHeartsGateway` with their authenticated request function and remount the game when account identity changes.
