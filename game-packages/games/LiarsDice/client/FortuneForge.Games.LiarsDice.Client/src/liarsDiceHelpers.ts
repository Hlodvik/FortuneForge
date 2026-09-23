import type { LiarsDiceBid, LiarsDiceMatch } from './contracts'

export function bidLabel(bid: LiarsDiceBid | null): string { return bid ? `${bid.quantity} × ${bid.face}s` : 'No bid yet' }
export function playerLabel(match: LiarsDiceMatch, id: string): string { return match.players.find(player => player.id === id)?.displayName ?? id }
export function nextBid(bid: LiarsDiceBid | null, totalDice: number): LiarsDiceBid { if (!bid) return { quantity: 1, face: 1 }; return bid.face < 6 ? { quantity: bid.quantity, face: bid.face + 1 } : { quantity: bid.quantity + 1, face: 1 } }
