# Fortune Forge game QA audit — 2026-09-23

## Completion status

The actionable consolidation backlog from this audit is closed. The professional-product comparisons below remain useful context, but ideas that would create an entirely new product surface—such as a second multiplayer ecosystem, additional commercial game variants, or a new social layer—are optional roadmap work rather than unresolved defects.

- The application and every reusable game package now live in this repository. The ownership guard verifies 17 server game projects and 15 client workspaces, with no binary/package dependency on the retired `fortuneforge.games` repository.
- `main` is the only local and remote branch. The catalog guard verifies exactly 38 playable routes and 20 slot contracts.
- Keno was rebuilt with pick presets, Quick Pick, saved tickets, an always-visible prize table, staged draws, repeat play, keyboard/touch support, and responsive controls.
- Card and table work added disclosed rules, strategy/practice help, fast bet controls, timers, histories, statistics, replay/review surfaces, Roulette racetrack controls, Craps odds/propositions, Liar's Dice variants/probability help, Baccarat Big Road and a persistent eight-deck shoe, and atomic multi-hand Video Poker settlement.
- Arcade and puzzle work added touch/controller-equivalent controls, pause/retry flows, persistent bests, accessible narration, progression feedback, and broader tests. Asteroids now adds a deterministic hunter enemy from wave two onward in both live and replay simulation.
- Every slot now exposes its named feature, trigger, persistent progress, rules, status, controls, reduced-motion/audio behavior, and keyboard focus states. Six real-browser visual baselines cover idle, rules, feature, win, error, and narrow-mobile states.
- Slot math now analyzes all 20 catalog entries during deployment. Each profile passes a deterministic 250,000-paid-spin release check for configured RTP, hit rate, sub-100% return, and majority bankroll depletion. Game-level payout calibration applies consistently to both line and feature-token awards.
- Current automated release coverage is 413 shared game-engine tests, 441 server tests, 168 game-client tests, 240 host-client tests, six browser visual tests, the 38-route/20-slot inventory guard, and the production smoke that exercises all routes plus every slot demo spin.

The detailed game notes below record the baseline that drove this work. They are retained as audit history, not as an active TODO list.

## Scope

This is the first production smoke and product-quality pass after the repository consolidation. It covers all 38 catalog games on the deployed site. A pass here means the route loaded and the named core interaction completed; it is not a claim that every rule branch, device, animation, or long-session state has been exhaustively tested.

- 34 games received an interactive production check using demo, free-play, practice, or non-wallet play.
- Video Poker, Baccarat, Casino War, and Sic Bo received production route/render checks plus client, API-contract, and engine tests. A live wallet debit was intentionally not used for QA.
- Automated coverage at the start of the pass was 430 server integration tests, 405 shared game-package tests, all 15 client game suites, and 233 host-client tests. The completion baseline is recorded above.

## Defects fixed during this pass

- Restored production profiles for Reel Riches, Arcane Archives, and Dino Dominion; corrected Rainbow Realm's symbol-set and payline metadata. All four now complete demo spins in production.
- Restored the package stylesheet imports for Video Poker, 2048, Sic Bo, Roulette, Liar's Dice, Keno, Baccarat, Hearts, Casino War, Drop Merge, and Craps. Keno's raw/unformatted production page was one symptom of this shared integration defect.
- Aligned Horse Flight's browser and server obstacle pools, widths, and biome-transition timing. A production collision at score 173 was accepted and saved after deployment.

## Product benchmark

The baseline findings below used these professional products as a quality bar, not as a request to copy their trade dress or mechanics:

- Modern premium slots: readable feature states, strong anticipation/settlement, distinct mechanics, and an immediately accessible rules/paytable surface. Pragmatic Play's Gates of Olympus pages are a useful example of how a product explains tumbles, multipliers, scatters, and free spins.
- Digital casino tables: obvious betting state, result history, statistics, rule disclosure, and polished table feedback. Evolution's live and First Person catalogs are the presentation benchmark; its Baccarat material also demonstrates roads, statistics, and side-bet discoverability.
- Poker and blackjack: explicit table rules, hand/rank help, fast bet controls, and unambiguous action/settlement feedback. PokerStars' rules and Video Poker pages are the benchmark.
- Casual games: touch support, instant restart, persistent bests/progression, readable tutorials, and satisfying feedback. Microsoft Solitaire Collection, the original 2048 project, Atari's modernized Asteroids releases, and Pogo's card catalog are useful reference points.

## Baseline game-by-game results and findings

### Slots (20)

All twenty slot routes loaded and completed a demo spin. The shared shell is functional; the main product gap is that too many titles still feel like themes laid over one cabinet instead of independently authored games.

1. **Wukong's Journey to the West — pass.** Keep its bespoke art direction, but make the special-round trigger/progress understandable before it occurs and strengthen the transition into and out of the feature.
2. **Rainbow Realm — pass after repair.** Add a persistent 23-line explainer, make the winning path visually traceable, and give near-miss/bonus anticipation its own sound and reel treatment.
3. **Pirates' Fortune — pass.** The bonus identity is one of the stronger ones; improve bonus discoverability and make the island-search result feel like a separate scene rather than another shell panel.
4. **Gods of Olympus — pass.** Its professional namesake sets a high bar for kinetic multipliers and feature buildup. Make multipliers/scatters the visual hierarchy and give the Olympian trial a clearer accumulated-state display.
5. **Reel Riches — pass after repair.** The fishing theme needs more game-specific motion and payoff: animate the catch/haul, expose feature progress, and avoid relying on generic reel settlement.
6. **High Noon Fortune — pass.** Clarify showdown readiness and stakes before the spin, then make the showdown sequence faster, larger, and visually separate from ordinary wins.
7. **Royal Draw — pass.** Lean into its card-game identity with clearer hand-like collection feedback and a stronger explanation of why a special outcome paid.
8. **Arcane Archives — pass after repair.** Telegraph rune/shelf progress on the cabinet and make the feature's current objective readable without opening help.
9. **Cosmic Fortune — pass.** Use orbital motion, persistent meter feedback, and stronger depth/audio changes to distinguish the bonus from an ordinary reskinned spin.
10. **Dino Dominion — pass after repair.** Show dig/collection progress continuously and make fossil/meteor events readable before payout text appears.
11. **Neon Nights — pass.** Tighten animation-to-audio synchronization and make the nightclub feature state more legible under the high-saturation palette.
12. **Jungle Jackpot — pass.** Improve foreground/background separation and use creature/environment motion to create anticipation without obscuring symbols.
13. **Ocean Odyssey — pass.** Increase low-value symbol differentiation and preserve payline readability against the detailed blue background.
14. **Samurai Fortune — pass.** Make the storm-spirit and collection states visually persistent, and audit text/symbol contrast on smaller screens.
15. **Candy Carnival — pass.** Its genre competitors are extremely kinetic; add a more distinctive chain/reaction rhythm and clearer escalation instead of depending on theme alone.
16. **Phantom Manor — pass.** Raise symbol contrast in the dark palette and use lighting/audio changes to communicate feature progression accessibly.
17. **Nordic Legends — pass.** Establish one unmistakable hero mechanic and meter; the current theme needs a stronger moment-to-moment identity.
18. **Desert Treasures — pass.** Improve gold-on-sand contrast and make treasure progression visible between spins.
19. **Robot Revolution — pass.** Add sharper mechanical impact, combo/charge feedback, and more distinctive reel-stop audio.
20. **Dragon Hoard — pass.** Build clearer anticipation around the hoard and let accumulated treasure visibly change the cabinet/feature state.

Shared slot priorities:

1. Add one automated production-contract smoke per slot ID so a missing server profile cannot ship again.
2. Add visual-regression coverage for cabinet, help, feature, win, error, and narrow-mobile states.
3. Give every title one mechanically and visually distinct feature rather than only a unique symbol set/backdrop.
4. Standardize accessible reduced-motion, mute, rules/paytable, demo labeling, and keyboard focus behavior.

### Card games (7)

21. **Fortune Blackjack — pass.** Demo deal and stand settled correctly. Match strong digital tables with visible rules (decks, soft 17, blackjack payout), compact chip shortcuts, clearer seat/action focus, and optional basic-strategy help in demo mode.
22. **Texas Hold'em — pass.** Demo call advanced to the flop. Add professional raise controls (slider plus half-pot/pot shortcuts), action timer, showdown hand explanation, compact hand history, and clearer side-pot/all-in treatment.
23. **Competitive Solitaire — pass.** A free deal started and stock draw incremented moves. Add drag-and-drop, legal-move hints, auto-finish, undo history, keyboard/touch parity, and stronger end-of-deal celebration; Microsoft Solitaire's challenges/progression show how much retention depth is still available.
24. **Video Poker — route/tests pass; no wallet debit.** Add multi-hand choice, Bet 1/Bet Max ergonomics, winning paytable-row highlight, strategy-warning help, and a much larger held-card state. Professional products make the paytable part of play, not reference text.
25. **Baccarat — route/tests pass; no wallet debit.** Add bead plate/Big Road history, shoe progress, explicit commission/rule disclosure, quick rebet, and a clearer deal/reveal ritual. Roads and statistics are table stakes in mature digital Baccarat.
26. **Casino War — route/tests pass; no wallet debit.** Make the tie decision unmistakable, explain surrender/go-to-war consequences at decision time, add rebet, and strengthen the card-reveal/showdown pacing.
27. **Hearts — pass.** Three cards were passed and a legal trick completed. Add passing-direction animation, last-trick review, legal-card rationale, score trajectory, rematch/statistics, and difficulty/personality differences for bots.

### Casino and dice games (5)

28. **Keno — pass after visual repair.** A five-number free draw settled with one hit. The broken unstyled page is fixed, but the game is still under-featured: add Quick Pick, saved/favorite tickets, pick-count presets, visible prize/paytable information before draw, staged ball-reveal animation, repeat ticket, and responsive large-target/mobile tuning. This is the highest-priority remaining UX rebuild.
29. **Sic Bo — route/tests pass; no wallet debit.** Add recent-roll history/trends, clearer bet-category grouping and payouts, repeat/clear controls, a physical-feeling dice reveal, and contextual explanations for combinations and triples.
30. **Roulette — pass.** A R1 practice straight-up bet on 17 settled on 2 black without touching the wallet. Add racetrack/neighbors controls, repeat/double/undo, recent-number statistics, more tactile wheel/ball staging, and a compact summary of winning and losing bets.
31. **Craps — pass.** A R10 free-play Pass Line bet rolled 6 and established the point. Expand beyond the guided Pass Line path, make puck/point/table phases unmistakable, add odds and common proposition bets in progressive layers, and provide a beginner coach that can be dismissed.
32. **Liar's Dice — pass.** An opening bid completed and bots advanced the bid. Add visible turn timing, dice/bid probability help for new players, stronger bot personalities/tells, round history, spot-on/call variants, and a faster rematch loop.

### Arcade and puzzle games (6)

33. **Asteroids — pass.** The run started, simulation advanced, and fire input responded. Add controller support, readable thrust/shield state, escalating enemy variety, hit-stop/screen feedback with reduced-motion fallback, pause, and a much faster death-to-retry loop. Atari's modern releases show how shield, enemy, and competitive variants can extend the core.
34. **Flappy — pass.** Start and Space launched an active run. Remove the double-start friction, add a three-count/first-input start, make mobile tap targets and obstacle collision bounds obvious, and keep restart to one action.
35. **Horse Flight — pass after replay repair.** A live collision saved successfully. Add a short first-run control rehearsal, clearer low-vs-high obstacle silhouettes, near-miss feedback, pause, controller/touch affordances, and a one-action restart while preserving deterministic replay validation.
36. **2048 — pass.** Move Left incremented the move count. Match the original's swipe handling and animation clarity, add persistent best score, keyboard/touch instructions, win/continue state, and an undo policy that is explicit rather than surprising.
37. **Drop Merge — pass.** Dropping 16 into column 4 changed the next tile and enabled Undo. Add a projected landing/merge preview, combo chain feedback, clearer danger/failure line, touch drag support, persistent best tile, and stronger high-tile celebrations.
38. **Snake — pass.** The run started and Arrow Down changed direction. Add swipe/controller input, pause/resume, speed or board-size choices, persistent high score, buffered turns, clearer food spawning, and a one-action restart.

## Original execution order (completed)

1. **Keno product rebuild** — quick pick, paytable/prize transparency, staged reveal, saved/repeat tickets, and mobile QA.
2. **Production smoke automation** — every route plus one free/demo core action, run after deploy.
3. **Wallet-game safe test mode** — a server-enforced QA/practice mode for Video Poker, Baccarat, Casino War, and Sic Bo so production interaction can be verified without balance mutation.
4. **Shared casino ergonomics** — repeat/undo/clear bets, history, rule disclosure, result summary, keyboard/touch parity.
5. **Arcade retry/control pass** — one-action restart, pause, controller/touch support, input buffering, persistent bests.
6. **Slot differentiation pass** — one game at a time, give each theme a distinct mechanic, readable persistent state, bespoke feature transition, and visual-regression coverage.

## Reference links

- https://www.pragmaticplay.com/en/games/gates-of-olympus-1000/
- https://www.evolution.com/brands/evolution
- https://games.evolution.com/live-casino/live-baccarat/
- https://www.pokerstars.com/casino/how-to-play/videopoker/
- https://www.pokerstars.com/poker/games/rules/
- https://www.xbox.com/en-us/games/store/Microsoft-Solitaire-Collection/9WZDNCRFHWD2
- https://github.com/gabrielecirulli/2048
- https://atari.com/products/asteroids-deluxe-7800-atari
- https://www.ea.com/pogo
