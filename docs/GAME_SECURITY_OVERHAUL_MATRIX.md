# Game security overhaul inventory

Local implementation inventory, 2026-10-31. This is a coverage checklist, not a claim that cheating detection is complete. The first isolated API pilot omits the concurrent Mines and paid Craps feature integrations; see [pilot scope](GAME_SECURITY_AUDIT_PROCESS.md#initial-api-pilot-scope).

## Slot backend count

The home catalogue contains **20 slot titles**. All 20 paid paths call the same server endpoint, `POST /api/slots/spins`, and the same `SpinService.Spin` instance registered by `SlotServicesConfiguration`. That service uses one reel generator, one evaluator, and one payout calculator. **Distinct slot outcome engines needing an overhaul: 1, serving 20/20 slot titles.** There are **2 ingress paths** to consider: paid spins and demo spins. The demo path also calls `SpinService.Spin` but does not settle a user's paid balance.

Wukong and Pirates Fortune supply bespoke base reel sets and feature branches to this shared engine. The other titles use their own symbol/reel/payline/paytable definitions and special-round profiles. These are title-specific rules inside one engine, not extra backend engines. The audit must freeze or version the effective rules per spin rather than assuming a later configuration still describes the original outcome.

| Slot title | Shared outcome engine | Distinct rule inputs |
| --- | --- | --- |
| Wukong Journey to the West | `SpinService` | Bespoke reel set and feature logic |
| Rainbow Realm | `SpinService` | Configured profile and math |
| Pirates Fortune | `SpinService` | Bespoke reel set and feature logic |
| Gods of Olympus | `SpinService` | Configured profile and math |
| Reel Riches | `SpinService` | Configured profile and math |
| High Noon Fortune | `SpinService` | Configured profile and math |
| Royal Draw | `SpinService` | Configured profile and math |
| Arcane Archives | `SpinService` | Configured profile and math |
| Cosmic Fortune | `SpinService` | Configured profile and math |
| Dino Dominion | `SpinService` | Configured profile and math |
| Neon Nights | `SpinService` | Configured profile and math |
| Jungle Jackpot | `SpinService` | Configured profile and math |
| Ocean Odyssey | `SpinService` | Configured profile and math |
| Samurai Fortune | `SpinService` | Configured profile and math |
| Candy Carnival | `SpinService` | Configured profile and math |
| Phantom Manor | `SpinService` | Configured profile and math |
| Nordic Legends | `SpinService` | Configured profile and math |
| Desert Treasures | `SpinService` | Configured profile and math |
| Robot Revolution | `SpinService` | Configured profile and math |
| Dragon Hoard | `SpinService` | Configured profile and math |

The paid path stores an admission record before the spin, then saves the spin result, frozen engine/rule evidence, reel stops, visible window, payout breakdown and wallet settlement. A server event links the request to the spin, and the database auditor independently recalculates saved paid outcomes using the frozen rules for all 20 titles. A stale paid admission without settlement opens an operational investigation after two minutes. The demo endpoint writes a private round and completion event for each public spin, with the request, engine result, response result and frozen engine details. Demo spins do not move account funds. The database pass independently replays demo math and bonus progression from that frozen evidence. Neither path establishes unbiased random generation, secrecy of a future result or a human player's intent.

## Non-slot catalogue

The home catalogue has **20 non-slot titles**, so the requested primary pass covers **1 shared slot outcome engine plus 20 non-slot titles**. Four more playable non-slot routes are default-hidden from the home catalogue. Asteroids, Flappy, Horse Flight and Snake now have hosted server paths with private run audits. Snake commits each accepted turn/tick and its event in a Firestore transaction; its completed run contains a server-generated start seed, action trace, board score class and final state for deterministic database replay. A reset of an unfinished run creates an incomplete audit episode.

| Title | Local evidence and database check | Remaining audit work |
| --- | --- | --- |
| Blackjack | Private final audit and legacy/table state journal; database replay checks ordered deck consumption, dealer draw, split/insurance outcomes and gross returns | Replay the complete legacy and table command sequence, including decision timing |
| Texas Hold'em | Private cards, deck, all seats and actions, hand check, engine journal and revenue/hand-ledger second pass; escrow buy-ins and recorded cash-outs checked | Complete escrow lifecycle reconciliation and calibrate strategy-based collusion analysis |
| Competitive Solitaire | Paid final match audit with all seats; database pass checks the seeded deal, private 52-card boards, human command transitions, final standings and entitlements; a later pass reconciles source, seats, tickets, buy-ins, pool/revenue and claimed prize ledger | Independently implement move rules instead of calling the game engine; replay batched bot actions; maintain the claim audit hold until the later credit is checked |
| Three Card Poker | Private final audit and table state journal; independent database replay of the saved deck, dealt cards, wagers and gross return | Replay the complete table command sequence independently |
| House Hold'em | Paid final audit and state journal; database pass replays saved deal, decisions and payout and links the action-key wager ledgers | Independently replay the complete decision timeline and strengthen deal provenance |
| Hearts | Paid and free private match audits and command journals; database pass validates each intermediate state and legal adjacent pass/play, then replays deals, tricks and cumulative scores and reconciles paid source, funding, revenue, entry and prize ledgers | Make free state and journal atomic; calibrate collusion analysis |
| Hi-Lo | Paid final audit and state journal; database pass replays revealed cards, choices and payout and links operation-key wallet ledgers | Independently replay the complete decision timeline and strengthen deal provenance |
| Casino War | Paid final audit and state journal; database pass replays deck, tie decision and payout and checks opening/war wallet ledgers | Replay complete timestamped action sequence independently |
| Video Poker | Paid final audit and state journal; database pass replays held cards, final hands and payout and checks wallet ledgers | Independently implement hand evaluator for the DB pass and check full command timing |
| Dragon Tiger | Paid final audit and state journal; database pass replays cards/wagers/payout and checks wallet ledgers | Complete shoe provenance and separate timed draw admission |
| Andar Bahar | Paid final audit and state journal; database pass replays deal sequence/payout and checks wallet ledgers | Complete deck provenance and separate timed draw admission |
| Baccarat | Paid final audit; database pass replays dealt cards, side bets and payout and checks wallet ledgers | Separate timed admission/deal events and shoe provenance |
| Roulette | In-memory practice with acknowledged Firestore journal and terminal evidence; database pass replays each saved bet/action, wheel result and payout and checks recorded remove-bet index | Make state and journal atomic; independently reconstruct managed wheel choices; missing sanitized command arguments remain a warning |
| Craps | In-memory practice with acknowledged Firestore journal and terminal evidence; database pass replays saved betting/roll transitions and payout | Make state and journal atomic; missing sanitized command arguments remain a warning |
| Sic Bo | Paid final audit and terminal journal; database pass recomputes bets/dice/gross return and reconciles source, settlement event and wallet ledgers | Separate timed admission/draw events |
| Liar's Dice | In-memory practice with Firestore journal; new v2 terminal evidence stores the round seed and database replay checks dice, bids, challenges, eliminations and winner | Make state and journal atomic; historical v1 rounds lack a replay seed and remain incomplete; client-supplied free-play seeds do not prove fair generation |
| Mines | Paid final audit and every accepted state version; database pass checks reveal/cash-out sequence, independent payout, source result and exact wallet entries | Prove board generation provenance and unbiased RNG |
| Keno | Paid final audit and terminal journal; database pass recomputes ticket/draw/payout and reconciles source, settlement event and wallet ledgers | Separate timed admission/draw events |
| 2048 | In-memory practice with Firestore journal; database pass independently replays seeded board, tile draws, moves, score, undo and terminal evidence; missing direction evidence is a coverage warning | Make state and journal atomic; link earlier history into reopened completion episodes; handle legitimate runs beyond the 2,000-event scan cap |
| Drop Merge | In-memory practice with Firestore journal; database pass independently replays seeded queue, drops, merges, score, undo and terminal evidence; missing column evidence is a coverage warning | Make state and journal atomic; link earlier history into reopened completion episodes; handle legitimate runs beyond the 2,000-event scan cap |

Asteroids, Flappy and Horse Flight write hosted start/completion events and private terminal evidence. The database worker independently replays their saved inputs against the engine, source run and paid entry debit. Paid competition prize/refund settlement remains separate, so those rounds retain that coverage warning. Snake uses a hosted deterministic engine and database replay of the complete accepted input trace; its terminal audit payload is self-contained and does not depend on a mutable `snakeGames` source snapshot. “Final audit” means a source result and evidence record exists; status may be `incomplete`, and neither a fingerprint nor a saved seed certifies unbiased randomness or honest play. The shared request recorder captures sanitized authenticated commands, server time, session/IP source and optional location context. A request joins an instance when a server event carries its request ID. Network observations affect pair priority only after verified trusted ingress is explicitly enabled; baseline pair screening always runs. The six in-memory practice services acknowledge Firestore writes, but local state mutation cannot be atomic with Firestore; a failure can leave incomplete evidence.

## Audit acceptance criteria

For each title or shared backend path, verify an authoritative instance ID, player/bot identities, rule/build/RNG version, server-timed wager and command events, hidden and public outcomes, settlement ledger references, immutable private evidence, and a replay coverage result. Explicitly label missing fields. An integrity incident pins and copies the *entire available* affected instance, including unflagged actions and players, and records missing sources. After verified trusted ingress opt-in, IP or approximate location raises review priority only; Hold'em, paid Hearts and competitive Solitaire pair screens still run for pairs with different or missing network observations.
