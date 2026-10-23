import type { HeartsCard, HeartsMatch, HeartsPassDirection, HeartsSeat } from './contracts'

export function seatLabel(seat: HeartsSeat): string { return seat[0].toUpperCase() + seat.slice(1) }
export function directionLabel(direction: HeartsPassDirection): string { return direction === 'hold' ? 'Hold' : direction[0].toUpperCase() + direction.slice(1) }
export function cardColor(card: HeartsCard): 'red' | 'black' { return card.suit === 'diamonds' || card.suit === 'hearts' ? 'red' : 'black' }
export const seatsClockwise: readonly HeartsSeat[] = ['north', 'east', 'south', 'west']
export function relativeSeat(seat: HeartsSeat, human: HeartsSeat): 'bottom' | 'left' | 'top' | 'right' {
  const distance = (seatsClockwise.indexOf(seat) - seatsClockwise.indexOf(human) + 4) % 4
  return (['bottom', 'left', 'top', 'right'] as const)[distance]
}
export function cardName(card: HeartsCard): string {
  const names: Record<string, string> = { A: 'Ace', J: 'Jack', Q: 'Queen', K: 'King', '2': 'Two', '3': 'Three', '4': 'Four', '5': 'Five', '6': 'Six', '7': 'Seven', '8': 'Eight', '9': 'Nine', '10': 'Ten' }
  const label = card.label.replace(/[♠♥♣♦]/g, '')
  return `${names[label] ?? label} of ${card.suit}`
}
export function turnLabel(match: HeartsMatch): string { return match.winner ? `${seatLabel(match.winner)} wins with the lowest score.` : match.phase === 'complete' ? 'Round complete. Start the next round.' : match.yourTurn ? 'Your turn' : `${seatLabel(match.turn)} is acting…` }
export function legalCardRationale(match: Pick<HeartsMatch, 'phase' | 'yourTurn' | 'currentTrick' | 'legalCards' | 'heartsBroken'> & Partial<Pick<HeartsMatch, 'completedTricks'>>): string {
  if (match.phase !== 'playing' || !match.yourTurn) return 'Follow suit when possible. Hearts cannot lead until they are broken.'
  if (!match.legalCards.length) return 'Waiting for your available cards…'
  const ledSuit = match.currentTrick.plays[0]?.card.suit
  if (ledSuit) {
    const label = suitLabel(ledSuit)
    return match.legalCards.every(card => card.suit === ledSuit)
      ? `${label} were led, so you must follow ${label.toLowerCase()}.`
      : match.completedTricks?.length === 0 && match.legalCards.every(card => !isPointCard(card))
        ? `No ${label.toLowerCase()} left. Play an available non-point card on this first trick.`
        : `You have no ${label.toLowerCase()}, so any available card is legal.`
  }
  if (match.completedTricks?.length === 0 && match.legalCards.length === 1 && match.legalCards[0].suit === 'clubs' && match.legalCards[0].label === '2♣') return 'The 2 of clubs starts the first trick.'
  if (!match.heartsBroken && match.legalCards.every(card => card.suit !== 'hearts')) {
    return 'You are leading. Hearts stay unavailable until someone breaks them.'
  }
  return 'You are leading this trick. Any available card is legal.'
}
function isPointCard(card: HeartsCard) { return card.suit === 'hearts' || card.suit === 'spades' && /^(Q|queen)$/i.test(card.rank) }
function suitLabel(suit: string): string { return suit[0]?.toUpperCase() + suit.slice(1) }
