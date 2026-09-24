# Fortune Forge game ship-review loop

## Rule of operation

The ship-review loop begins only after the existing prompt backlog has been implemented, integrated, tested, and deployed. Once it begins, exactly one game is active at a time. Work does not advance to the next game until the owner explicitly says to move on.

For every game, repeat this cycle:

1. Compare the current game with appropriate professional examples and inspect its code, assets, rules, and prior implementation history.
2. Fix obvious functional, visual, audio, responsive, accessibility, and interaction defects before asking for review.
3. Verify the primary play loop on desktop and mobile. Desktop must not scroll; on mobile, the full playable surface and primary controls must fit within the first viewport even when supporting content can scroll.
4. Present the updated game to the owner for hands-on review.
5. Implement the owner's feedback, retest, and return to step 4.
6. Mark the game shippable and advance only when the owner explicitly says **move on**.

Automated checks, repository-wide fixes, and delegated stabilization may continue in parallel, but they do not change the active game or mark another game shippable.

## Current state

- Active game: **Pirates' Fortune**
- Current phase: deployment and owner hands-on review
- Start gate: complete — the owner started the loop
- Advance gate after start: the owner explicitly says **move on** after hands-on review

## Review queue

The order puts games with specific reported failures first, then completes the remaining catalog. Reordering the queue does not itself advance the active game.

| Order | Game | Category | State |
| ---: | --- | --- | --- |
| 1 | Pirates' Fortune | Slot | Active — owner review pending |
| 2 | Wukong's Journey to the West | Slot | Queued |
| 3 | Keno | Casino | Queued |
| 4 | Fortune Blackjack | Card | Queued |
| 5 | Casino War | Card/table | Queued |
| 6 | Baccarat | Card/table | Queued |
| 7 | Video Poker | Card | Queued |
| 8 | Roulette | Casino | Queued |
| 9 | Craps | Casino | Queued |
| 10 | Liar's Dice | Casino | Queued |
| 11 | Sic Bo | Casino | Queued |
| 12 | Texas Hold'em | Card | Queued |
| 13 | Flappy | Arcade | Queued |
| 14 | Asteroids | Arcade | Queued |
| 15 | Drop Merge | Puzzle | Queued |
| 16 | 2048 | Puzzle | Queued |
| 17 | Snake | Arcade | Queued |
| 18 | Horse Flight | Arcade | Queued |
| 19 | Competitive Solitaire | Card | Queued |
| 20 | Hearts | Card | Queued |
| 21 | Rainbow Realm | Slot | Queued |
| 22 | Gods of Olympus | Slot | Queued |
| 23 | Reel Riches | Slot | Queued |
| 24 | High Noon Fortune | Slot | Queued |
| 25 | Royal Draw | Slot | Queued |
| 26 | Arcane Archives | Slot | Queued |
| 27 | Cosmic Fortune | Slot | Queued |
| 28 | Dino Dominion | Slot | Queued |
| 29 | Neon Nights | Slot | Queued |
| 30 | Jungle Jackpot | Slot | Queued |
| 31 | Ocean Odyssey | Slot | Queued |
| 32 | Samurai Fortune | Slot | Queued |
| 33 | Candy Carnival | Slot | Queued |
| 34 | Phantom Manor | Slot | Queued |
| 35 | Nordic Legends | Slot | Queued |
| 36 | Desert Treasures | Slot | Queued |
| 37 | Robot Revolution | Slot | Queued |
| 38 | Dragon Hoard | Slot | Queued |

## Shippable gate

A game is shippable only when all of the following are true:

- Its normal play loop starts, accepts input, resolves, and can start again.
- Its important rules and outcomes are understandable without developer knowledge.
- Its layout is stable throughout play and does not clip or overlap controls.
- Desktop has no document scrolling; mobile keeps the game and primary controls in the first viewport.
- Its theme, art, animation, sound, and terminology are coherent.
- It does not expose implementation terms such as `bot`, `demo balance`, internal IDs, or environment details.
- Keyboard, touch, focus, reduced-motion, selection, and native image-drag behavior have been checked where applicable.
- Focused automated tests and the relevant repository release checks pass.
- The owner has reviewed the deployed version and explicitly approved moving on.
