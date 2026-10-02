import type { LiarsDiceBid, LiarsDiceMatch } from './contracts'

export function bidLabel(bid: LiarsDiceBid | null): string { return bid ? `${bid.quantity} × ${bid.face}s` : 'No bid yet' }
export function playerLabel(match: LiarsDiceMatch, id: string): string { const player = match.players.find(player => player.id === id); return player?.isHuman ? 'You' : player?.displayName ?? id }

/** Check the entered bid against the server's exact-face ordering without changing it. */
export function validBid(bid: LiarsDiceBid, currentBid: LiarsDiceBid | null, totalDice: number): boolean {
  if (!validTotal(totalDice) || !bidInRange(bid, totalDice) || currentBid && !bidInRange(currentBid, totalDice)) return false
  return currentBid === null || bid.quantity > currentBid.quantity
    || bid.quantity === currentBid.quantity && bid.face > currentBid.face
}

/** The lowest legal raise; the top bid can only be challenged or called Spot On. */
export function nextBid(bid: LiarsDiceBid | null, totalDice: number): LiarsDiceBid | null {
  if (!validTotal(totalDice) || bid && !bidInRange(bid, totalDice)) return null
  if (!bid) return { quantity: 1, face: 1 }
  const next = bid.face < 6 ? { quantity: bid.quantity, face: bid.face + 1 } : { quantity: bid.quantity + 1, face: 1 }
  return validBid(next, bid, totalDice) ? next : null
}

/** Present the server's outcome, never infer a winner or loss from private dice. */
export function outcomeLabel(match: LiarsDiceMatch): string {
  if (match.winner) { const winner = playerLabel(match, match.winner); return `${winner} ${winner === 'You' ? 'win' : 'wins'} the match` }
  if (match.phase === 'bidding') { const actor = playerLabel(match, match.currentPlayerId); return actor === 'You' ? 'Your turn' : `${actor} to act` }
  return resolvedOutcomeLabel(match) ?? 'Round resolved'
}

export type LiarsDiceHistoryEntry = Readonly<{ matchId: string; round: number; text: string }>

export function historyEntry(match: LiarsDiceMatch): LiarsDiceHistoryEntry | null {
  const text = match.phase === 'resolved' ? resolvedOutcomeLabel(match) : null
  return text === null ? null : { matchId: match.matchId, round: match.roundNumber, text }
}

/** A refresh cannot duplicate a round, and two matches may both have round one. */
export function appendRoundHistory(history: readonly LiarsDiceHistoryEntry[], match: LiarsDiceMatch, limit = 8): readonly LiarsDiceHistoryEntry[] {
  if (!Number.isSafeInteger(limit) || limit <= 0) return []
  const entry = historyEntry(match)
  const entries = entry ? [entry, ...history] : history
  const seen = new Set<string>()
  const result: LiarsDiceHistoryEntry[] = []
  for (const item of entries) {
    const id = JSON.stringify([item.matchId, item.round])
    if (seen.has(id)) continue
    seen.add(id); result.push(item)
    if (result.length === limit) break
  }
  return result
}

function resolvedOutcomeLabel(match: LiarsDiceMatch): string | null {
  const outcome = match.outcome
  if (!outcome) return null
  const call = outcome.callType === 'spot-on' ? 'Spot On' : 'Liar'
  const loser = playerLabel(match, outcome.loserId)
  return `${call} · ${outcome.matchingDice} matching ${outcome.face}s · ${loser} ${loser === 'You' ? 'lose' : 'loses'} a die`
}

function bidInRange(bid: LiarsDiceBid, totalDice: number): boolean { return Number.isSafeInteger(bid.quantity) && bid.quantity > 0 && bid.quantity <= totalDice && validFace(bid.face) }
function validFace(face: number): boolean { return Number.isSafeInteger(face) && face >= 1 && face <= 6 }
function validTotal(totalDice: number): boolean { return Number.isSafeInteger(totalDice) && totalDice > 0 }
