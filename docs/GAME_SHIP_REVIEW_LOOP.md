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

- Active game: **Keno**
- Current phase: owner hands-on review (verified locally; deployment awaits approval)
- Start gate: complete — the owner started the loop
- Advance gate after start: the owner explicitly says **move on** after hands-on review

## Review queue

The order puts games with specific reported failures first, then completes the remaining catalog. Reordering the queue does not itself advance the active game.

| Order | Game | Category | State |
| ---: | --- | --- | --- |
| 1 | Keno | Casino | Active — owner review pending |
| 2 | Fortune Blackjack | Card | Queued |
| 3 | Casino War | Card/table | Queued |
| 4 | Baccarat | Card/table | Queued |
| 5 | Video Poker | Card | Queued |
| 6 | Roulette | Casino | Queued |
| 7 | Craps | Casino | Queued |
| 8 | Liar's Dice | Casino | Queued |
| 9 | Sic Bo | Casino | Queued |
| 10 | Texas Hold'em | Card | Queued |
| 11 | Flappy | Arcade | Queued |
| 12 | Asteroids | Arcade | Queued |
| 13 | Drop Merge | Puzzle | Queued |
| 14 | 2048 | Puzzle | Queued |
| 15 | Snake | Arcade | Queued |
| 16 | Horse Flight | Arcade | Queued |
| 17 | Competitive Solitaire | Card | Queued |
| 18 | Hearts | Card | Queued |
| 19 | Rainbow Realm | Slot | Queued |
| 20 | Gods of Olympus | Slot | Queued |
| 21 | Reel Riches | Slot | Queued |
| 22 | High Noon Fortune | Slot | Queued |
| 23 | Royal Draw | Slot | Queued |
| 24 | Arcane Archives | Slot | Queued |
| 25 | Cosmic Fortune | Slot | Queued |
| 26 | Dino Dominion | Slot | Queued |
| 27 | Neon Nights | Slot | Queued |
| 28 | Jungle Jackpot | Slot | Queued |
| 29 | Ocean Odyssey | Slot | Queued |
| 30 | Samurai Fortune | Slot | Queued |
| 31 | Candy Carnival | Slot | Queued |
| 32 | Phantom Manor | Slot | Queued |
| 33 | Nordic Legends | Slot | Queued |
| 34 | Desert Treasures | Slot | Queued |
| 35 | Robot Revolution | Slot | Queued |
| 36 | Dragon Hoard | Slot | Queued |
| 37 | Wukong's Journey to the West | Slot | Deferred to end of queue |
| 38 | Pirates' Fortune | Slot | Deferred to end of queue |

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
