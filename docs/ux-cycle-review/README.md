# UX batch review against `main`

This record tracks the review of `ux/pro-game-cycle-2026-09-30` against the live `main` line.

## Review policy

- Port clear bug fixes, mobile compatibility fixes, accessibility corrections, and state/recovery safeguards directly to `main` after focused tests.
- Preserve newer work already on `main`; a difference from the batch baseline is not evidence that it should be removed.
- Treat visual redesigns, new explanatory copy, and changes to the owner's established interaction model as proposals unless they are required to fix objective breakage.
- Review one completed game at a time. Do not advance the separate owner-led ship-review loop.

## Source

- Batch branch: `ux/pro-game-cycle-2026-09-30`
- Batch baseline: `a7f1205`
- `main` at audit start: `627a95d`

## Games reviewed

| Game | Batch commits | Decision |
| --- | --- | --- |
| Keno | `41a8c7b`, `bf13365` | Port the batch changes. They fix stable board geometry, portrait and short-landscape fit, hit/miss presentation, wager controls, audio state, keyboard navigation, and recovery/error behavior without restoring removed quick-pick or saved-ticket features. |
| Blackjack | `d25d163` | Port the batch changes. They fix five-seat short-landscape layout, touch targets, responsive card fans, wager validation and request locking, help-dialog focus, surrender availability, timer semantics, hidden-page audio, and live reduced-motion behavior. No bot engine or matchmaking behavior is taken from this commit. |
| Casino War | `4c45de1` | Keep the newer `main` table design. Port only the objective fixes: delayed balance publication until reveal, authoritative balance retention, blank/invalid and unaffordable wager handling, recovery cleanup, pending-stake restoration, immediate surrender completion, rules-dialog focus/Escape/outside behavior, and short-landscape containment. Do not replace the table with the batch branch's two-column layout, details panel, or added persistent copy. Preserve `main`'s safe fetch wrapper. |
| Baccarat | `96d6527` | Port the checkpoint. `main` had no later Baccarat work, and the branch fixes clipped controls, omitted third cards, deal order/timing, premature outcome and balance publication, wager editing/affordability, exact-key recovery, account/mode isolation, road correctness, focus behavior, and all target mobile viewports. The optional rules remain behind `?`; the professional reference's letterboxing and tiny controls are not copied. |
| Video Poker | `c549af5` | Port the checkpoint. `main` had no later Video Poker work, and the branch fixes clipped controls and five-hand results, moving held cards, reveal timing, current-balance and wager continuity, supported-hand and affordability validation, exact-key recovery, rejected-wager cleanup, keyboard/touch/focus behavior, and all target mobile viewports. Existing strategy help is confined to an optional panel and never changes holds; vendor-specific rules, gamble features, assets, and payout changes are not copied. |

## Next review

Roulette is in progress on the batch branch. Review it when the producer thread reports the completed commit and evidence; then append the decision here before touching `main`.
