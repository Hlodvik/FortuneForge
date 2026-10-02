import { LiarsDiceGatewayError, type LiarsDiceBid, type LiarsDiceGateway, type LiarsDiceMatch, type LiarsDiceOutcome, type LiarsDicePlayer, type LiarsDiceStatus } from './contracts'

export class HttpLiarsDiceGateway implements LiarsDiceGateway {
  readonly basePath: string
  constructor(basePath = '/api/games/liars-dice', private readonly fetcher: typeof fetch = (...args) => fetch(...args)) { this.basePath = basePath.replace(/\/$/, '') }
  getStatus(signal?: AbortSignal) { return this.request('/status', { signal }, isStatus) }
  getMatch(matchId: string, signal?: AbortSignal) { return this.request(`/matches/${encodeURIComponent(matchId)}`, { signal }, matchingMatch(matchId)) }
  startMatch(options: { dicePerPlayer: number; seed?: number }, signal?: AbortSignal) { return this.request('/matches', { method: 'POST', headers: jsonHeaders, body: JSON.stringify(options), signal }, isMatch) }
  bid(matchId: string, bid: { quantity: number; face: number }, signal?: AbortSignal) { return this.request(`/matches/${encodeURIComponent(matchId)}/bid`, { method: 'POST', headers: jsonHeaders, body: JSON.stringify(bid), signal }, matchingMatch(matchId)) }
  challenge(matchId: string, signal?: AbortSignal) { return this.request(`/matches/${encodeURIComponent(matchId)}/challenge`, { method: 'POST', signal }, matchingMatch(matchId)) }
  spotOn(matchId: string, signal?: AbortSignal) { return this.request(`/matches/${encodeURIComponent(matchId)}/spot-on`, { method: 'POST', signal }, matchingMatch(matchId)) }
  advance(matchId: string, signal?: AbortSignal) { return this.request(`/matches/${encodeURIComponent(matchId)}/advance`, { method: 'POST', signal }, matchingMatch(matchId)) }
  nextRound(matchId: string, seed?: number, signal?: AbortSignal) { return this.request(`/matches/${encodeURIComponent(matchId)}/next-round`, { method: 'POST', headers: jsonHeaders, body: JSON.stringify({ seed }), signal }, matchingMatch(matchId)) }
  private async request<T>(path: string, init: RequestInit, validate: (value: unknown) => value is T): Promise<T> { const response = await this.fetcher(`${this.basePath}${path}`, init); const value: unknown = await response.json().catch(() => null); if (!response.ok) { const error = isError(value) ? value : null; throw new LiarsDiceGatewayError(error?.message ?? `The Liar's Dice request failed (${response.status}).`, error?.code, response.status) } if (!validate(value)) throw new LiarsDiceGatewayError("The Liar's Dice service returned an invalid response."); return value }
}

const jsonHeaders = { 'content-type': 'application/json' }
function isRecord(value: unknown): value is Record<string, unknown> { return typeof value === 'object' && value !== null }
function isStatus(value: unknown): value is LiarsDiceStatus { return isRecord(value) && typeof value.available === 'boolean' && isPositiveInteger(value.startingDicePerPlayer) && isId(value.mode) }
function matchingMatch(matchId: string): (value: unknown) => value is LiarsDiceMatch {
  return (value): value is LiarsDiceMatch => isMatch(value) && value.matchId === matchId
}
function isMatch(value: unknown): value is LiarsDiceMatch {
  if (!(isRecord(value) && isId(value.matchId) && isPhase(value.phase) && isPositiveInteger(value.roundNumber)
    && isId(value.currentPlayerId) && (value.currentBid === null || isBid(value.currentBid))
    && (value.currentBidderId === null || isId(value.currentBidderId)) && isPositiveInteger(value.totalDice)
    && Array.isArray(value.hand) && value.hand.every(isFace)
    && Array.isArray(value.players) && value.players.length >= 2 && value.players.every(isPlayer)
    && (value.outcome === null || isOutcome(value.outcome)) && (value.winner === null || isId(value.winner))
    && typeof value.opponentsThinking === 'boolean'
    && typeof value.message === 'string')) return false

  const match = value as unknown as LiarsDiceMatch
  const ids = new Set(match.players.map(player => player.id))
  if (ids.size !== match.players.length || !ids.has(match.currentPlayerId)) return false
  const humans = match.players.filter(player => player.isHuman)
  if (humans.length !== 1) return false
  const human = humans[0]
  const active = match.players.filter(player => player.active)
  const current = match.players.find(player => player.id === match.currentPlayerId)!
  if ((match.currentBid === null) !== (match.currentBidderId === null)) return false
  if (match.currentBid && (match.currentBid.quantity > match.totalDice || !ids.has(match.currentBidderId!) || match.currentBidderId === current.id)) return false

  const remainingDice = match.players.reduce((sum, player) => sum + player.diceCount, 0)
  if (match.phase === 'bidding') {
    return match.outcome === null && match.winner === null && active.length >= 2 && current.active
      && (match.currentBidderId === null || match.players.some(player => player.id === match.currentBidderId && player.active))
      && match.totalDice === remainingDice && match.hand.length === human.diceCount
  }

  const outcome = match.outcome
  if (!outcome || !match.currentBid || !match.currentBidderId || active.length === 0
    || match.totalDice !== remainingDice + 1
    || outcome.challengerId !== current.id || outcome.bidderId !== match.currentBidderId
    || outcome.challengerId === outcome.bidderId || !ids.has(outcome.loserId)
    || outcome.quantity !== match.currentBid.quantity || outcome.face !== match.currentBid.face
    || outcome.matchingDice > match.totalDice
    || match.hand.length !== human.diceCount + (outcome.loserId === human.id ? 1 : 0)) return false
  // The outcome count is supplied by the server; no hidden hands are reconstructed.
  const expectedLoser = outcome.callType === 'spot-on'
    ? outcome.matchingDice === outcome.quantity ? outcome.bidderId : outcome.challengerId
    : outcome.matchingDice >= outcome.quantity ? outcome.challengerId : outcome.bidderId
  if (outcome.loserId !== expectedLoser) return false
  if (!match.players.every(player => player.id !== outcome.bidderId && player.id !== outcome.challengerId || player.active || player.id === outcome.loserId)) return false
  return active.length === 1 ? match.winner === active[0].id : match.winner === null
}
function isPlayer(value: unknown): value is LiarsDicePlayer {
  return isRecord(value) && isId(value.id) && isId(value.displayName) && isNonNegativeInteger(value.diceCount)
    && typeof value.active === 'boolean' && value.active === (value.diceCount > 0) && typeof value.isHuman === 'boolean'
}
function isBid(value: unknown): value is LiarsDiceBid { return isRecord(value) && isPositiveInteger(value.quantity) && isFace(value.face) }
function isOutcome(value: unknown): value is LiarsDiceOutcome {
  return isRecord(value) && isId(value.challengerId) && isId(value.bidderId) && isId(value.loserId)
    && isPositiveInteger(value.quantity) && isFace(value.face) && isNonNegativeInteger(value.matchingDice)
    && (value.callType === undefined || value.callType === 'liar' || value.callType === 'spot-on')
}
function isPhase(value: unknown): value is LiarsDiceMatch['phase'] { return value === 'bidding' || value === 'resolved' }
function isError(value: unknown): value is { code: string; message: string } { return isRecord(value) && typeof value.code === 'string' && typeof value.message === 'string' }
function isId(value: unknown): value is string { return typeof value === 'string' && value.trim().length > 0 }
function isNonNegativeInteger(value: unknown): value is number { return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0 }
function isPositiveInteger(value: unknown): value is number { return isNonNegativeInteger(value) && value > 0 }
function isFace(value: unknown): value is number { return typeof value === 'number' && Number.isInteger(value) && value >= 1 && value <= 6 }
