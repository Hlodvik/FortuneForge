import rules from './assets/frontier-rules.json' with { type: 'json' }
import outlawRules from './assets/frontier-outlaw-rules.json' with { type: 'json' }

// Presentation metadata only. Outcomes, funding and hands come from the paid server.
export const FRONTIER_RULES = rules
export const OUTLAW_RULES = outlawRules
export const HAND_NAMES: Record<string, string> = {
  pair: 'One Pair', 'two-pair': 'Two Pair', trips: 'Three of a Kind', 'full-house': 'Full House',
  four: 'Four of a Kind', five: 'Five of a Kind', grand: 'Five Wilds',
}
export const INITIAL_REELS = [
  ['J', 'HAT', 'Q'], ['K', 'SPUR', 'A'], ['POUCH', 'Q', 'REVOLVER'],
  ['A', 'BAR', 'K'], ['WANTED', 'BADGE', 'WILD'],
] as const
const labels: Record<string, string> = {
  J: 'Jack of clubs', Q: 'Queen of hearts', K: 'King of diamonds', A: 'Ace of spades',
  SPUR: 'Silver spur', HAT: 'Trail hat', POUCH: 'Gold pouch', REVOLVER: 'Six-shooter',
  BADGE: 'Sheriff badge', BAR: 'Brass BAR', WILD: 'Western WILD', WANTED: 'Wanted poster',
}
export const FRONTIER_SYMBOLS = rules.symbols.map(symbol => ({ id: symbol.id, label: labels[symbol.id] ?? symbol.id }))
export const PAYLINES = rules.paylines
export const PAYTABLE: Record<string, Record<number, number>> = Object.fromEntries(
  rules.symbols.filter(symbol => symbol.payouts.length > 0).map(symbol => [
    symbol.id, Object.fromEntries(symbol.payouts.map((payout, index) => [index + 3, payout / rules.payoutDivisor])),
  ]),
)
