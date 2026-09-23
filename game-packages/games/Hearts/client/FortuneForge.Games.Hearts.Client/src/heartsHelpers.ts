import type { HeartsCard, HeartsMatch, HeartsPassDirection, HeartsSeat } from './contracts'

export function seatLabel(seat: HeartsSeat): string { return seat[0].toUpperCase() + seat.slice(1) }
export function directionLabel(direction: HeartsPassDirection): string { return direction === 'hold' ? 'Hold' : direction[0].toUpperCase() + direction.slice(1) }
export function cardColor(card: HeartsCard): 'red' | 'black' { return card.suit === 'diamonds' || card.suit === 'hearts' ? 'red' : 'black' }
export function turnLabel(match: HeartsMatch): string { return match.winner ? `${seatLabel(match.winner)} wins with the lowest score.` : match.phase === 'complete' ? 'Round complete. Start the next round.' : match.yourTurn ? 'Your turn' : `${seatLabel(match.turn)} is acting…` }
export function botPersona(seat: HeartsSeat): string {
  return seat === 'east' ? 'Blaze · bold' : seat === 'south' ? 'Sage · cautious' : seat === 'west' ? 'Moxie · unpredictable' : 'You'
}
export function legalCardRationale(match: Pick<HeartsMatch, 'phase' | 'yourTurn' | 'currentTrick' | 'legalCards' | 'heartsBroken'>): string {
  if (match.phase !== 'playing' || !match.yourTurn) return 'Follow suit when possible. Hearts cannot lead until they are broken.'
  const ledSuit = match.currentTrick.plays[0]?.card.suit
  if (ledSuit) {
    const label = suitLabel(ledSuit)
    return match.legalCards.every(card => card.suit === ledSuit)
      ? `${label} were led, so you must follow ${label.toLowerCase()}.`
      : `You have no ${label.toLowerCase()}, so any highlighted card is legal.`
  }
  if (!match.heartsBroken && match.legalCards.every(card => card.suit !== 'hearts')) {
    return 'You are leading. Hearts stay unavailable until someone breaks them.'
  }
  return 'You are leading this trick. Any highlighted card is legal.'
}
function suitLabel(suit: string): string { return suit[0]?.toUpperCase() + suit.slice(1) }
