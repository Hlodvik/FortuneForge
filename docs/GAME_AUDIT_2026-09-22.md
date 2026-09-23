# Fortune Forge game audit

Completed September 22, 2026 against production commit `2b408ef`.

## Outcome

- The consolidated application is live: Cloud Run revision `fortuneforge-api-00081-tpb` serves 100% of API traffic and Firebase Hosting serves <https://fortuneforgegame.web.app>.
- The canonical roster is 38 games: 20 slots, 7 card games, 5 casino/dice games, and 6 arcade/puzzle games.
- Every game was opened and its primary interaction was exercised. All 20 slots were checked in production; the remaining 18 games were exercised against the same commit with a local API and Firestore emulator so real customer state was not changed.
- The application CI baseline passed: 424 server tests, 218 client tests, lint, dependency audit, production build, Firestore-emulator integration, and deployment-image construction.
- The release is operational, but the roster is not release-quality as a whole. Three slots have no server profile, Rainbow Realm cannot accept its server response, 12 cloned slot profiles fail the configured math guard, Sic Bo has a mouse-blocking control overlap, and the reusable-games source/release path is not yet reproducible from committed source.

Severity labels: **P0** release or financial-safety gate; **P1** broken core path or unreliable outcome; **P2** meaningful usability, accessibility, performance, or product-completeness failure; **P3** polish.

## Method

Each game went through the same loop:

1. Inspect its route, client, server adapter/domain implementation, and tests.
2. Run the relevant automated test and production-build surfaces.
3. Exercise the rendered game through its primary action and result state.
4. Inspect loading, error, responsive, keyboard, touch, accessibility, feedback, pacing, and settlement behavior.
5. Compare it with a current professional counterpart or category standard.
6. Record concrete defects and improvements before moving to the next game.

This was a functional and product audit, not certification. Slot simulations are release-signal samples, not a substitute for independently verified math, long-run RTP analysis, or jurisdiction-specific compliance testing.

## Repository and release integrity

### What is consolidated and healthy

- `FortuneForge-consolidated/main` is the deployed application source and matches `origin/main` at `2b408ef` before this report.
- The previous Discord image generator and Discord PNGs are gone from the deployed repository.
- The remote has only `main` and the older `refactor/feature-organization` branch. Local salvage branches remain as recovery points and do not affect deployment.
- All application restore/build/test/deploy steps run from the consolidated checkout.

### Game-source consolidation resolved

- **Resolved — reproducible package source.** Reusable server engines and deployed client packages now live under `game-packages` in this repository. The server uses project references, the web application uses npm workspaces, and copied `.nupkg`/`.tgz` feeds have been removed.
- **Resolved — package test baseline.** Roulette and Hearts metadata now agree with their manifests and tests. The retained package suite passes from the monorepo.
- **Resolved — Drop Merge client coverage.** Drop Merge now has a runnable helper test suite, so every deployed client workspace has tests.
- **Resolved — CI ownership guard.** CI verifies that server games use source projects, every client game dependency maps to an in-repository workspace, and obsolete copied-package feeds are absent.
- **Resolved — package UI pipeline.** CI type-checks, tests, and builds every deployed client game workspace before testing and building the host application.

## Cross-game release backlog

- **P0 — Expand slot math validation to all 20 catalog entries.** The deploy script currently analyzes only the two explicit JSON definitions. It passed while 12 cloned server profiles were outside the same configured safeguards and three profiles did not exist.
- **P0 — Keep Pirates' Fortune, Cosmic Fortune, and Jungle Jackpot away from wallet-credit settlement until their over-100% samples are corrected and independently verified.** Their 100,000-spin samples returned 124.229%, 105.133%, and 103.898% respectively.
- **P1 — Add one catalog-to-server-to-client contract test for every game.** It must verify route, status endpoint, playable profile, symbol/config version, payline count, and a valid primary result. This would have caught all three missing profiles and Rainbow Realm before deployment.
- **P1 — Treat contract/math errors as their own states.** The slot shell converts every spin exception into “Demo unavailable,” hiding Rainbow Realm's symbol-set mismatch as an outage.
- **P1 — Add a compliance/product-safety shell before any real-value casino release.** Rules, RTP/house edge, volatility where applicable, wager limits, session time, net position, reality checks, and responsible-play access are not consistently available. Fast stop/autospin also require jurisdiction-specific review.
- **P2 — Preserve game identity on mobile.** The slot layout hides the title entirely at the tested 390×844 breakpoint.
- **P2 — Replace “Demo unavailable” during availability checks.** A neutral loading message should be shown until the check actually fails.
- **P2 — Prevent fixed overlays from covering game actions.** The global music pill blocked Sic Bo's Roll Dice button at the 1021×732 audit viewport.
- **P2 — Standardize free-play versus wallet-credit labeling.** Roulette, Craps, Hearts, Liar's Dice, 2048, Drop Merge, and Snake use short-lived/free-play state while neighboring casino games use the account wallet. The catalog and in-game chrome should make the distinction unmistakable.
- **P2 — Add useful result/history surfaces.** Professional poker, roulette, baccarat, and arcade products retain hand/round history, recent results, personal bests, or replays. Fortune Forge usually shows only the current result.
- **P2 — Add descriptive accessible names and spatial context.** Several `−`/`+` controls are unnamed, and board games expose tile/card values without row/column or suit-target context.
- **P2 — Split the Craps and Liar's Dice route bundles.** The production build reports roughly 727 kB and 724 kB minified chunks, above the 500 kB warning threshold.
- **P3 — Remove the Fast Refresh warning.** `GameAmbientMusic.tsx` exports a non-component alongside a component.

## Slots (1–20)

All slot pages share a capable base cabinet: wager controls, spin/early-stop, autospin, rules, audio settings, symbol values, line feedback, and result recovery worked where the server contract was valid. The product gap is that most themes are the same 5×4/23-line experience with new art and profile numbers, while current premium counterparts differentiate games with mechanics such as tumbles, pays-anywhere clusters, progressive multipliers, bonus choices, challenges, and stronger session information.

Math samples used 250,000 paid spins for Wukong and Rainbow (the deploy-time configured definitions) and 100,000 paid spins for each cloned profile. “Pass” means the current tool's configured safeguard passed; it is not certification.

| # | Game | Primary path | Math sample | Ruin | Verdict and TODO |
|---:|---|---|---:|---:|---|
| 1 | Wukong's Journey to the West | Production spin, win explanation, early stop, autospin, rules, settings, and meters worked | 90.807% | 58.82% | **Pass.** Add RTP/volatility/max-win disclosure, a mobile title, and session/responsible-play controls. Expand settings beyond audio. |
| 2 | Rainbow Realm | Availability is 204, but every spin is rejected by the client | 87.746% | 74.96% | **P1.** Server returns `wukong-treasures-v3`; client requires `rainbow-realm-fruits-v1-symbols`. Rules say 22 paylines while server config says 23. Align the contract and add a cross-tier test. |
| 3 | Pirates' Fortune | Production spin worked | **124.229%** | 0.02% | **P0 settlement gate.** Retune and run a much larger independent simulation before wallet play. Add a pirate-specific mechanic rather than a skin-only profile. |
| 4 | Gods of Olympus | Production spin worked | 91.366% | 60.20% | **Pass.** Differentiate it from the shared line engine; premium Olympus competitors use tumbles, pays-anywhere wins, and large random multipliers. |
| 5 | Reel Riches | Spin disabled; status endpoint returns game-not-found | — | — | **P1.** Register a server profile or remove the tile until one exists; then add contract and math coverage. |
| 6 | High Noon Fortune | Production spin worked | **81.462%** | 96.77% | **P1.** Retune; the sampled return and ruin rate make the game feel punishing. Add theme-specific duel/wanted mechanics and publish the model. |
| 7 | Royal Draw | Production spin worked | **95.581%** | 36.60% | **P1.** The profile misses its configured target band. Decide the intended RTP, encode it explicitly, and validate it in CI. |
| 8 | Arcane Archives | Spin disabled; status endpoint returns game-not-found | — | — | **P1.** Register a server profile or remove the tile; validate symbols, lines, and feature rules end to end. |
| 9 | Cosmic Fortune | Production spin worked | **105.133%** | 12.98% | **P0 settlement gate.** Retune below 100%, verify bonus contribution, and independently certify before wallet play. |
| 10 | Dino Dominion | Spin disabled; status endpoint returns game-not-found | — | — | **P1.** Register a server profile or remove the tile; add a catalog/server launch test. |
| 11 | Neon Nights | Production spin worked, including an isolated retest | 89.387% | 66.62% | **Pass.** Add a distinctive feature loop and improve rapid route-transition resilience testing. |
| 12 | Jungle Jackpot | Production spin worked, including an isolated retest | **103.898%** | 17.16% | **P0 settlement gate.** Retune, validate jackpot contribution/return separately, and block wallet settlement until certified. |
| 13 | Ocean Odyssey | Production spin worked | **84.060%** | 87.75% | **P1.** Retune the profile and reduce the extreme bust rate; add a theme-specific progression/bonus loop. |
| 14 | Samurai Fortune | Production spin worked | **85.187%** | 85.41% | **P1.** Retune and expose the intended volatility; add mechanics that make the theme more than cabinet art. |
| 15 | Candy Carnival | Production spin worked | **76.332%** | 99.76% | **P1.** This is the worst sampled under-return. Rebuild the paytable/reel strips before further product work. |
| 16 | Phantom Manor | Production spin worked | **79.041%** | 98.58% | **P1.** Retune before promotion; add a clear feature trigger/progression loop. |
| 17 | Nordic Legends | Production spin worked | **77.724%** | 99.37% | **P1.** Retune the paytable/reel strips and verify free-game contribution separately. |
| 18 | Desert Treasures | Production spin worked | 91.872% | 51.78% | **Pass.** Add a differentiated mechanic and publish the math model in the help panel. |
| 19 | Robot Revolution | Production spin worked | **79.953%** | 98.24% | **P1.** Retune and add a feature loop that communicates progress and expected value. |
| 20 | Dragon Hoard | Production spin worked | **83.675%** | 90.71% | **P1.** Retune; validate feature contribution and reduce ruin before wallet settlement. |

## Card games (21–27)

### 21. Fortune Blackjack — functional

- Exercised a five-seat credit table: selected R5, received a two-card hand, stood, saw the dealer resolve, settlement update, and next-round state.
- Server rule, contract, table-state, and Firestore-emulator tests passed in the application suite.
- **P2 TODO:** label the `−` and `+` wager controls; keep rules, table limits, deck/S17/surrender/insurance policy, and help available after seating; add optional practice-only basic-strategy/misclick protection. Professional clients disclose variant rules and commonly provide insurance, side bets, and configurable action warnings.

### 22. Texas Hold'em — functional

- Joined the Standard R0.50/R1 table, received pocket eights, called, and reached a flop with the pot and action state updating.
- Engine, credit-store, API, and emulator tests passed.
- **P2 TODO:** add the current best-hand label, hand/action history and replay, descriptive raise-step controls, quick bet sizes, and optional keyboard shortcuts. Keep real-time strategy advice out of competitive play; professional poker clients emphasize history/replay, bet-slider presets, and hotkeys.

### 23. Competitive Solitaire — functional with accessibility gaps

- Started a free deal, drew from the stock, moved through the board, and verified score, moves, timer, undo, pause, and submission surfaces.
- Client engine/API and server competitive/free-run/emulator tests passed.
- **P2 TODO:** add an `h1`; rename or explain “Submit game” in free play; label foundation targets by suit; add hint/autocomplete/restart/game-number affordances. Longer term, add daily challenges, difficulty, themes, achievements, and cross-device progression comparable to Microsoft Solitaire Collection.

### 24. Video Poker — functional

- Dealt five cards at one coin, held four, drew to a straight, and verified the R4 return and account balance.
- Full-pay Jacks or Better engine/paytable and application contract tests passed.
- **P2 TODO:** add Max Bet, auto-hold suggestions as an optional learning aid, a visible 9/6 explanation with theoretical return, and clearer paytable discovery. Professional cabinets keep the paytable continuously legible and commonly support several poker variants/multi-hand modes.

### 25. Baccarat — functional

- Bet R1 on Player, dealt Player 7 versus Banker 1, and verified a R2 total return and wallet refresh.
- Punto Banco engine, paytable, client, contract, and Firestore-backed settlement tests passed.
- **P2 TODO:** add a rules/tableau drawer, Player/Banker pair side bets, commission explanation at decision time, recent-result roads/history, and result replay. Professional Baccarat makes fixed third-card rules and commissions readily inspectable.

### 26. Casino War — functional

- Placed a R1 primary wager, received King versus 2, and verified a R2 return and wallet refresh. Tie stake and Go to War/Surrender are exposed; automated contracts cover the tie branch.
- **P2 TODO:** add in-table rules/help and a complete payout/war-flow explanation; provide hand history and a deterministic way to learn the tie branch in practice mode; label whether the second tie favors the player before the wager.

### 27. Hearts — functional

- Completed three-card passing, followed suit with a legal card, and completed the first trick with score/trick counters updating.
- Domain and application service tests passed.
- **P2 TODO:** add a persistent rules/scoring panel, trick/round history, pass-direction history, and multiplayer/private-table options. Bicycle's current product adds ranked/public/private play, leaderboards, and social features. **P3:** capitalize bot status copy such as “south is thinking…”.

## Casino and dice games (28–32)

### 28. Keno — functional free play

- Selected five numbers, completed the animated 20-number draw, and verified accessible hit/drawn/missed labels and a 2-hit result.
- Engine, package client, application contract, and endpoint tests passed.
- **P2 TODO:** add Quick Pick, spots/payout table, stake or explicit practice-only labeling, multi-draw, draw-speed/skip controls, and recent tickets. Professional lottery Keno supports Quick Pick, selectable stake, consecutive draws, and optional multipliers.

### 29. Sic Bo — logic works; mouse path can be blocked

- Built a Small wager and settled a 4+5+5 roll through the keyboard path.
- **P1:** the fixed bottom-left music pill overlaps Roll Dice at 1021×732. A mouse click toggles music instead of rolling; keyboard activation works. Move the pill or reserve safe-area padding and add a viewport interaction test.
- **P2 TODO:** clean accessible quick-bet names (`SmallTotal 4–10`), hide the raw `three-dice-sic-bo` mode identifier, expose a complete odds/RTP guide, and consider distinct premium mechanics only after the base table is reliable.

### 30. Roulette — functional practice table

- Opened a single-zero round, placed R1 on 17, spun 0, and saw the losing settlement.
- Roulette package tests passed; application server tests cover validation and settlement.
- **P2:** show an explicit “Practice balance” label. The page initially flashes R0 before status resolves, then uses a separate in-memory R1,000 balance rather than the account wallet.
- **P2 TODO:** add recent results, rebet/double, saved bets, racetrack/neighbors, hot/cold statistics with a randomness disclaimer, and round recovery/history. Professional single-zero tables expose these efficiency/history tools and publish the 97.3% theoretical RTP.

### 31. Craps — functional narrow foundation

- Placed R10 on Pass Line, established point 8, and continued with a no-decision 6; roll history and point state updated correctly.
- Engine, package, application service, and controller tests passed.
- **P2 product gap:** this is Pass Line only. A professional Craps product needs Don't Pass, Come/Don't Come, Odds, Place/Buy/Lay, Field, proposition/hardway bets, working toggles, limits, and complete payout/help surfaces. Until then, label it “Pass Line practice,” not full Craps.
- **P2:** disclose that the session is no-credit and short-lived; add round recovery/history.

### 32. Liar's Dice — functional with result-state polish defects

- Played a bid, received three bot raises, called liar, resolved the challenge, lost one die, and started the next round with 19 dice.
- Engine, package, and application service tests passed.
- **P2:** replace internal status copy (`bot-3 placed a bid`) with the bot display name; fix grammar (`you loses a die`); make the resolved screen immediately show the post-challenge total/hand instead of stale 20-dice/five-dice values.
- **P2 TODO:** add a rules/tutorial panel, bidding history, difficulty, multiplayer/private tables, and clearly selectable exact-face versus wild-ones variants.

## Arcade and puzzle games (33–38)

### 33. Asteroids — functional seeded competition loop

- Started a casual server-seeded run, used fire/thrust controls, saw frame/life state advance, and verified desktop and touch controls plus leaderboard entry choices.
- Engine/replay, free-run, paid-entry, package-client, and page tests passed.
- **P2 TODO:** add pause/abandon, controller support, better playfield narration, and more modes/content. Atari's current professional version adds power-ups, 30 challenge levels, global/local leaderboards, co-op, UFOs, and a dynamic soundtrack.

### 34. Flappy — functional

- Started, flapped with Space, crashed, and verified the server-recorded official score and replay path.
- Engine/free-run, package-client, and page tests passed.
- **P2 TODO:** collapse the redundant outer “Start flight” plus inner “Start flight” flow into one clear launch; add pause, richer level/obstacle progression, challenge/quest modes, and an immediately visible personal-best/history surface.

### 35. Horse Flight — functional on mouse/keyboard; incomplete on touch

- Started a run, double-jumped, scored 42, hit an obstacle, and verified the leaderboard save.
- Engine, package-client, Firestore store, and application contract tests passed.
- **P1 mobile:** slide/fast-fall is implemented only as held right mouse button (`button === 2`). Touch users have no equivalent control even though click/tap handles jump. Add a dedicated touch/keyboard control and a mobile interaction test.
- **P2 TODO:** add pause/restart confirmation, visible control buttons, input remapping/controller support, missions, and clearer hazard telegraphs.

### 36. 2048 — functional

- Made a legal move, verified the new tile/move counter, and successfully undid it.
- Engine and package-client tests passed.
- **P2 mobile:** the original 2048 supports swipe; Fortune Forge offers tap buttons but no swipe gesture.
- **P2 accessibility:** tiles announce only values, not row/column, so screen-reader users cannot reconstruct the board. Add a grid model or concise board summary. Persist best score across sessions and offer “Keep playing” after 2048 like the original.

### 37. Drop Merge — functional but untested at the package UI layer

- Started a seven-column run, dropped an 8, verified the next/current queue, and paused/resumed the timer.
- Domain tests pass in the reusable-games suite, but **P1:** the client package has no test files and `npm test` exits 1.
- **P2 TODO:** add client tests for timed auto-drop, pause, undo, chain merges, full-column game over, keyboard controls, and restart; add danger-zone warnings, stronger combo feedback, themes/modes, and cloud leaderboard/persistence comparable to polished drop-merge products.

### 38. Snake — functional

- Started by choosing a direction, steered with touch controls, and verified collision/game-over behavior and statistics.
- Engine, local gateway, package-client, and application route tests passed.
- **P2 TODO:** add pause, speed/board-size/mode settings, persistent best scores/leaderboards, and a first-move grace period or clearer explanation that Start Moving immediately continues Right. Add row/column context or a concise board summary for assistive technology.

## Professional comparison references

- Pragmatic Play premium slots: [Gates of Olympus 1000](https://www.pragmaticplay.com/en/games/gates-of-olympus-1000/), [Fury of Anubis](https://www.pragmaticplay.com/en/campaign/fury-of-anubis/), and [Jelly Express](https://www.pragmaticplay.com/en/campaign/jelly-express/).
- UK Gambling Commission: [Remote gambling and software technical standards](https://www.gamblingcommission.gov.uk/manual/remote-gambling-and-software-technical-standards/3-remote-gambling-and-software-technical-standards), [RTS 14 responsible product design](https://www.gamblingcommission.gov.uk/manual/guidance-to-licensing-authorities/rts-14-responsible-product-design), and [testing strategy](https://www.gamblingcommission.gov.uk/strategy/testing-strategy-for-compliance-with-remote-gambling-and-software-technical/3-procedure-for-testing).
- PokerStars: [Blackjack rules](https://www.pokerstars.com/casino/how-to-play/blackjack/rules/), [Texas Hold'em rules](https://www.pokerstars.com/poker/games/texas-holdem/), [hand histories](https://www.pokerstars.com/help/articles/save-hand-histories/), [Roulette rules/features](https://www.pokerstars.com/casino/how-to-play/roulette/rules/), and [Baccarat rules](https://www.pokerstars.com/casino/games/live/baccarat/rules/).
- Microsoft/Xbox: [Microsoft Solitaire Collection](https://www.xbox.com/en-us/games/store/Microsoft-Solitaire-Collection/9WZDNCRFHWD2).
- Bicycle: [Hearts rules](https://bicyclecards.com/how-to-play/hearts) and [current multiplayer card-game product](https://apps.apple.com/us/app/card-games-by-bicycle/id1536578074).
- Government/lottery rules: [Georgia Lottery Keno](https://gas-origin2.galottery.com/en-us/games/draw-games/keno.html), [California Casino War rules](https://oag.ca.gov/sites/all/files/agweb/pdfs/gambling/BGC_war.pdf), and [Singapore Casino War rules](https://www.gra.gov.sg/docs/default-source/game-rules/rws/other-games/rws-gr---casino-war-v2.pdf).
- Arcade/puzzle: [Atari Asteroids: Recharged](https://atari.com/products/asteroids-recharged), [the original 2048 source](https://github.com/gabrielecirulli/2048), and the current [Flappy Bird product listing](https://play.google.com/store/apps/details?id=com.flappybirdfoundation.flappybird).

## Recommended execution order

1. Freeze wallet settlement for unverified slot profiles; fix the all-game math gate.
2. Register/remove the three missing slots and repair Rainbow Realm's contract.
3. Commit and stabilize the `FortuneForge.Games` migration, fix its three tests, add Drop Merge tests, and make package publishing clean-tree/commit-pinned.
4. Fix Sic Bo's blocked Roll control and Horse Flight's missing touch action.
5. Standardize free-play labeling, rules/RTP/responsible-play surfaces, mobile titles, errors, and accessibility.
6. Retune the remaining nine under/over-target slot profiles.
7. Work through the per-game professional-feature backlog, starting with result/history tools and the narrow Craps/Keno/Roulette experiences.
