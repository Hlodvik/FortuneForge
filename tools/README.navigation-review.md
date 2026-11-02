# Local navigation review

Run `node tools/navigation-review-server.mjs` from the repository root, then open
`http://127.0.0.1:4205/__navigation-fixture` in a separate review tab. Node and the
installed frontend dependencies are sufficient. This starts Vite middleware with
no backend proxy. Every `/api/**` request is synthetic; unknown APIs return 503.
The account, wallet and game state exist only in that process. Stop it with Ctrl+C.

The index links to accepted, idle, completed, queued and uncertain-request states.
`phone=1` supplies an all-in Texas hand or a Hearts interval between rounds.
Click the canonical Home link to test the accessible confirmation. Stay or Escape
keeps the page; Leave page performs only navigation. An uncertain House state
starts idle: place a chip on Ante and Deal to simulate a lost response.

The app presents its own dialog for ordinary same-window links and synchronous
navigation through `requestGameNavigation`. Native cross-document Back, closing
and reload use a generic best-effort browser `beforeunload` prompt. Browser policy
may suppress it. The guard does not alter history or perform financial actions on
visibility changes, pagehide, unload, confirmation or cancellation. Active slots
are exempt. The local Arcane Archives transport uses a historical captured spin
at its minimum wager; its in-memory delayed balance update is not a paid-server
settlement test.

For the automated desktop/phone matrix, from `fortuneforge.client` run:

```powershell
node ../node_modules/@playwright/test/cli.js test --config playwright.game-leave.config.ts
```

That suite runs on independent port 4204 with per-page API interception. Its raw
Vite URL is not the manual-review server. The tests cover accepted waiting hands,
terminal and idle cleanup, queues, uncertain requests, focus/Escape/cancel,
repeated leave-link/Back visits, programmatic no-op protection, background events,
and an accepted slot transport completing once after immediate departure.
