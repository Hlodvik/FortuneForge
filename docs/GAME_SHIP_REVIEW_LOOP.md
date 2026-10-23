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

- Active game: **Hearts**
- Current phase: **four-seat table presentation and fixed-buy-in implementation**
- Start gate: complete — the owner started the loop
- Advance gate after start: the owner explicitly says **move on** after hands-on review
- Baccarat: owner directed deployment and advancement on October 18, 2026; the guarded release is in progress.
- Parallel new-game build: **Dragon Tiger**, in the separate **Build Dragon Tiger** chat. This does not change the active owner-led review game.
- Hearts settlement approved by owner: equal fixed buy-ins, lowest match score wins the pot, tied lowest scores split it. The paid entry must be explicit and resumable. Implementation and review are in progress.

## Review queue

All non-slot games come first. The slot catalog follows, with Wukong and Pirates deferred to the end. Reordering the queue does not itself advance the active game.

| Order | Game | Category | State |
| ---: | --- | --- | --- |
| 1 | Keno | Casino | Shippable — owner approved and deployed |
| 2 | Fortune Blackjack | Card | Shippable — owner directed advancement and deployed |
| 3 | Casino War | Card/table | Shippable — owner marked complete |
| 4 | Baccarat | Card/table | Owner approved advancement — deployment in progress |
| 5 | Video Poker | Card | Shippable — owner directed advancement and deployed |
| 6 | Roulette | Casino | Shippable — owner directed advancement and deployment |
| 7 | Craps | Casino | Queued |
| 8 | Liar's Dice | Casino | Queued |
| 9 | Sic Bo | Casino | Shippable — owner directed deployment and advancement |
| 10 | Texas Hold'em | Card | Shippable — owner explicitly marked done |
| 11 | Flappy | Arcade | Shippable — owner explicitly marked finished |
| 12 | Asteroids | Arcade | Reviewed as Asteroid Blaster — owner directed deployment and advancement |
| 13 | Drop Merge | Puzzle | Queued |
| 14 | 2048 | Puzzle | Queued |
| 15 | Snake | Arcade | Queued |
| 16 | Horse Flight | Arcade | Paused — owner directed temporary delisting and post-deadline repairs |
| 17 | Competitive Solitaire | Card | Owner directed deployment — release state being audited |
| 18 | Hearts | Card | Active — fixed buy-ins and professional four-seat table implementation |
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
