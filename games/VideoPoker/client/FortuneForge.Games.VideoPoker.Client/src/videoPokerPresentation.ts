import type { VideoPokerCard, VideoPokerCardPosition, VideoPokerHandRank } from './contracts'

export function cardName(card: VideoPokerCard) { return card.rank + ' of ' + card.suit }
export function formatMoney(value: number, symbol: string) { return (value < 0 ? '-' : '') + symbol + Math.abs(value).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 }) }
export function handLabel(rank: VideoPokerHandRank, payout: number) {
  if (rank === 'pair') return payout > 0 ? 'Jacks or Better' : 'Pair'
  return ({ 'no-win': 'No Win', 'two-pair': 'Two Pair', 'three-of-a-kind': 'Three of a Kind', straight: 'Straight', flush: 'Flush', 'full-house': 'Full House', 'four-of-a-kind': 'Four of a Kind', 'straight-flush': 'Straight Flush', 'royal-flush': 'Royal Flush' } as const)[rank]
}
export function paytableRows(coins: number) { return [
  { hand: 'Royal Flush', payout: coins === 5 ? 4_000 : 250 * coins },
  { hand: 'Straight Flush', payout: 50 * coins },
  { hand: 'Four of a Kind', payout: 25 * coins },
  { hand: 'Full House', payout: 9 * coins },
  { hand: 'Flush', payout: 6 * coins },
  { hand: 'Straight', payout: 4 * coins },
  { hand: 'Three of a Kind', payout: 3 * coins },
  { hand: 'Two Pair', payout: 2 * coins },
  { hand: 'Jacks or Better', payout: coins },
] }
// A basic optional cue, never an automatic hold or optimal-return claim.
export function recommendedHolds(cards: readonly VideoPokerCard[]): VideoPokerCardPosition[] {
  const ranks = ['two', 'three', 'four', 'five', 'six', 'seven', 'eight', 'nine', 'ten', 'jack', 'queen', 'king', 'ace']
  const values = cards.map(card => ranks.indexOf(card.rank) + 2).sort((a, b) => a - b)
  const flush = cards.every(card => card.suit === cards[0]?.suit)
  const straight = values.every((value, index) => index === 0 || value === values[index - 1]! + 1) || values.join(',') === '2,3,4,5,14'
  if (cards.length === 5 && (flush || straight)) return [0, 1, 2, 3, 4]
  const groups = new Map<string, number[]>()
  cards.forEach((card, index) => groups.set(card.rank, [...(groups.get(card.rank) ?? []), index]))
  const made = [...groups.values()].filter(indices => indices.length >= 2).flat()
  if (made.length) return made.sort() as VideoPokerCardPosition[]
  return cards.flatMap((card, index) => ['jack', 'queen', 'king', 'ace'].includes(card.rank) ? [index as VideoPokerCardPosition] : [])
}
