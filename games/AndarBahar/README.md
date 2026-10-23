# FortuneForge Andar Bahar

A separately packaged solo house table, ready for owner review. All source belongs to `games/AndarBahar`; the central app, catalogue, solution, manifests, lockfile and release scripts are deliberately untouched. Integration remains a parent-chat batch step; see [INTEGRATION.md](INTEGRATION.md).

## Play locally

Open **[the playable table](http://127.0.0.1:5182/)**. The isolated API is **http://127.0.0.1:5202**. Both are running for review at delivery.

To restart, from the repository root in separate terminals:

```powershell
npm run dev:api --prefix games/AndarBahar/client/FortuneForge.Games.AndarBahar.Client
npm run dev:ui --prefix games/AndarBahar/client/FortuneForge.Games.AndarBahar.Client
```

The sample is **development practice only**, with R10,000 of separate practice credit per local browser account. It cannot use production funds. An HTTP-only local cookie identifies the practice account; a durable local ledger survives API restarts. Browser pending wagers retain their original idempotency key, and restoring them cannot create a second debit. Use the same hostname throughout; the Vite proxy handles the API cookie.

## Selected rules

One fresh standard 52-card deck, no printed jokers. Its first card is the central **Joker**, the target rank. Cards are dealt individually **Andar first**, Bahar second, alternating until the first same-rank card appears. Suits never affect the result. The latest possible first match is card 49; ties and pushes are impossible.

This solo variant accepts one main-side wager before turning over the Joker. The provider's reference procedure permits betting after the Joker is shown; this documented presentation timing change leaves the probabilities identical for every possible Joker. There are no speculative side bets, queues, other players or bots. No separate cash-out is needed: each wager settles atomically before its card replay begins.

| Side | Profit payout | R10 winning return | Exact win probability | RTP |
| --- | ---: | ---: | ---: | ---: |
| Andar, dealt first | 0.9:1 | R19 | 429/833 | 97.8511404562% |
| Bahar, dealt second | 1:1 | R20 | 404/833 | 96.9987995198% |

Winning returns include the stake. Losing bets return zero. Wagers are R0.50–R100 in R0.50 increments; all accounting uses exact integer cents, including the R0.95 return on a winning R0.50 Andar wager. [Engine documentation](src/FortuneForge.Games.AndarBahar/README.md) includes the authoritative rule source, combinatorial derivation, house edges, ordering and settlement contract.

## Digital reference and presentation

After the engine passed its 36-rule-test gate, actual digital game screens were viewed for **OneTouch Andar Bahar**, **TaDa/JILI Andar Bahar**, and **RTG Andar Bahar**, plus OneTouch's portrait presentation. [REFERENCE_REVIEW.md](REFERENCE_REVIEW.md) records the screen URLs and decisions.

The chosen reference is **[OneTouch's digital Andar Bahar](https://www.onetouch.io/games/andar-bahar/)**: emerald felt, brass table lip, restrained ornamental corners, the Joker flanked by two card piles, chip wagering and nearby clear/deal controls. FortuneForge uses an original generated table material and its established chip images/audio. No provider branding, artwork or extracted assets is shipped. The paytable is the independently verified Andar-first variant above.

The game includes stacked chips, traditional card indexes/pips following the shared FortuneForge card convention, individual 320ms card dealing, optional skip, matching-card lighting, result dialog with return/net/balance, rebet/undo/clear, recent results, rules, native modal focus, keyboard/touch input, reduced motion and persistent mute. A skipped animation or refreshed browser recovers the same server settlement.

## Package map

- `src/FortuneForge.Games.AndarBahar`: immutable rules and payout engine using shared .NET card/descriptor primitives.
- `server/FortuneForge.Games.AndarBahar.Server`: authenticated endpoint extension, cryptographic shuffle, exact-cent settlement, Firestore transactional wallet and private recovery, durable development practice store.
- `client/FortuneForge.Games.AndarBahar.Client`: reusable React component, strict HTTP gateway, game-owned material/assets and standalone Vite preview.
- `samples/FortuneForge.Games.AndarBahar.Web`: loopback development practice API.
- `tests/FortuneForge.Games.AndarBahar.Tests`: engine, HTTP, durable practice and real Firestore emulator tests.
- `integration`: prepared host-route/stylesheet and catalogue descriptor for the parent to copy during batch integration.
- `evidence`: reviewed desktop/mobile/landscape screenshots.

## Verification

```powershell
dotnet test games/AndarBahar/tests/FortuneForge.Games.AndarBahar.Tests -c Release
npm run check --prefix games/AndarBahar/client/FortuneForge.Games.AndarBahar.Client
npm run test --prefix games/AndarBahar/client/FortuneForge.Games.AndarBahar.Client
npm run build --prefix games/AndarBahar/client/FortuneForge.Games.AndarBahar.Client
npm run test:e2e --prefix games/AndarBahar/client/FortuneForge.Games.AndarBahar.Client
```

The browser test requires the two practice servers. It verifies its development-practice mode before submitting any wager. The Firestore tests start an isolated local emulator using existing installed tooling; no production project is contacted. In a restrictive sandbox, set `TEMP`/`TMP` to a writable game-local `.test-temp` and `PLAYWRIGHT_BROWSERS_PATH` to the installed local browser cache. No dependency install is needed with the existing repository dependencies. See [VERIFICATION.md](VERIFICATION.md) for final results and limits.

Owner review and central authenticated-route integration are still required before release. This task did not commit, push, deploy, alter live configuration, or advance the active Hearts review.
