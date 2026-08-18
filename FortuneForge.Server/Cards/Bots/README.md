# Card practice platform (contract v2)

This milestone is server-first and practice/demo/test-only. It does not create bot accounts,
touch account balances or ledgers, fund prize pools, contact a payment provider, or alter the
existing paid Blackjack and competitive Solitaire routes. Every feature is off unless its
per-game `Enabled` value is explicitly set to `true`.

## Frozen HTTP contract

All responses carry `contractVersion: "cards.bot.v2"`. Public seats expose only a random,
match-scoped opaque seat ID, a normal display name, seat position, and status. Automation kind,
internal skill, internal actor identity, queue grace timing, and durable lease identity are
server-only. Public action events map their actor to the same opaque seat ID and display name.
Commands are versioned and idempotent and use the same `CardBotCommandRequest` envelope for
every seat. The practice session is identified by a non-account `X-Practice-Session-Id` header
of 16–128 characters, but that raw session ID is never returned as a public seat ID.

Each game exposes the same route shape:

- `POST /api/cards/blackjack/bot-practice/queue`
- `GET /api/cards/blackjack/bot-practice/session`
- `POST /api/cards/blackjack/bot-practice/matches/{matchId}/commands`
- `POST /api/cards/solitaire/bot-practice/queue`
- `GET /api/cards/solitaire/bot-practice/session`
- `POST /api/cards/solitaire/bot-practice/matches/{matchId}/commands`
- `POST /api/cards/texas-holdem/bot-practice/queue`
- `GET /api/cards/texas-holdem/bot-practice/session`
- `POST /api/cards/texas-holdem/bot-practice/matches/{matchId}/commands`

Queue body:

```json
{
  "playerCount": 2,
  "difficulty": 3,
  "idempotencyKey": "practice_join_key_0001"
}
```

Command body:

```json
{
  "type": "stand",
  "expectedVersion": 1,
  "idempotencyKey": "practice_action_key_0001",
  "arguments": {}
}
```

Solitaire `move` arguments use `fromZone`, `fromIndex`, `startIndex`, `toZone`, and
`toIndex`; `flip` uses `column`. Hold'em `raise` uses integer virtual-chip `raiseTo`.

## Configuration

The exact configuration names are:

- `Cards:Bots:WorkerIntervalMilliseconds`
- `Cards:Bots:TurnLeaseSeconds`
- `Cards:Bots:{Blackjack|Solitaire|TexasHoldem}:Enabled`
- `Cards:Bots:{Blackjack|Solitaire|TexasHoldem}:MaxBotsPerMatch`
- `Cards:Bots:{Blackjack|Solitaire|TexasHoldem}:HumanWaitGraceMilliseconds`
- `Cards:Bots:{Blackjack|Solitaire|TexasHoldem}:MinimumThinkDelayMilliseconds`
- `Cards:Bots:{Blackjack|Solitaire|TexasHoldem}:MaximumThinkDelayMilliseconds`
- `Cards:Bots:{Blackjack|Solitaire|TexasHoldem}:ThreeStarErrorRate`
- `Cards:Bots:{Blackjack|Solitaire|TexasHoldem}:FourStarImperfectionRate`

Bounds are validated at startup/options resolution. Four-star imperfection must remain greater
than zero; one-star and five-star levels are rejected.

## Local run

The server already requires the repository's normal Google/Firebase development credentials.
Bot turn claims use the `cardBotTurnLeases` Firestore collection so concurrent workers and
restarts cannot replay a completed bot version. To enable only practice Blackjack in a local
PowerShell process:

```powershell
$env:Cards__Bots__Blackjack__Enabled = "true"
dotnet run --project FortuneForge.Server --launch-profile http
```

Set the equivalent `Solitaire` or `TexasHoldem` environment key only for the game being tested.
Poll `GET .../session` through the grace period; a background worker fills only missing seats
and applies delayed bot turns without blocking request threads.

## Information and accounting boundaries

- Blackjack exposes all player hands and actions but redacts the dealer hole until settlement.
- Hold'em exposes community cards, pot, stacks, and actions. A viewer sees only its own hole
  cards until showdown; folded cards stay hidden.
- Solitaire returns only the requesting seat's board/version. Other live boards, commands,
  deal seed, and hidden cards are absent; only seat metadata is shared until final standings.
- Public queue, match, result, and event JSON never identifies an individual seat as automated.
  Operators may retain automation/skill metadata in private telemetry, outside player APIs.
- Practice chips/scores live only in the match aggregate. No account, balance, ledger, payment,
  payout, clock, seed, or result service is injected into any game bot agent.

## Activation blockers and next integration work

Paid bot seats and winnings have no approved product/accounting policy. Do not enable these
features in a real-credit queue, create house-funded entries, or merge practice results into
paid history until that policy, disclosure, auditing, and regulatory review are complete.

The first playable milestone keeps practice queue/match snapshots in the singleton practice
runtime; reconnect/version/idempotency are supported while that runtime is alive, and durable
Firestore bot-turn leases prevent duplicate bot commands across workers/restarts. Before a
multi-instance production practice rollout, persist the queue/match snapshots with transactional
compare-and-swap and add Firestore-emulator restart/failover tests. Client rendering and an
end-to-end browser pass are also intentionally deferred; no central menu or client route was
changed here.
