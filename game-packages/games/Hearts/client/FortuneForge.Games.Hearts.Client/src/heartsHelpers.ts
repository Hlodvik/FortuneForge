import type { HeartsCard, HeartsMatch, HeartsPassDirection, HeartsSeat } from './contracts'

export function seatLabel(seat: HeartsSeat): string { return seat[0].toUpperCase() + seat.slice(1) }
export function directionLabel(direction: HeartsPassDirection): string { return direction === 'hold' ? 'Hold' : direction[0].toUpperCase() + direction.slice(1) }
export function cardColor(card: HeartsCard): 'red' | 'black' { return card.suit === 'diamonds' || card.suit === 'hearts' ? 'red' : 'black' }
export function turnLabel(match: HeartsMatch): string { return match.winner ? `${seatLabel(match.winner)} wins with the lowest score.` : match.phase === 'complete' ? 'Round complete. Start the next round.' : match.yourTurn ? 'Your turn' : `${seatLabel(match.turn)} is acting…` }
