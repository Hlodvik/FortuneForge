import type { GameCommandRecovery } from '@fortuneforge/client-presentation/game-command-recovery'
import { LiarsDiceGatewayError, type LiarsDiceBid, type LiarsDiceGateway, type LiarsDiceMatch, type LiarsDiceOutcome, type LiarsDicePlayer, type LiarsDiceStatus } from './contracts'

export class HttpLiarsDiceGateway implements LiarsDiceGateway {
  readonly basePath: string
  constructor(basePath = '/api/games/liars-dice', private readonly fetcher: typeof fetch = (...args) => fetch(...args), private readonly recovery?: GameCommandRecovery, private readonly expectedAccountId?: string) { this.basePath = basePath.replace(/\/$/, '') }
  getStatus(signal?: AbortSignal) { return this.request('/status', { signal }, isStatus) }
  async getMatch(matchId: string, signal?: AbortSignal) {
    const current = await this.request(`/matches/${encodeURIComponent(matchId)}`, { signal }, matchingMatch(matchId))
    if (this.recovery && !this.recovery.read().pending) this.recovery.setActiveBoardId(current.matchId)
    return current
  }
  startMatch(options: { dicePerPlayer: number; seed?: number }, signal?: AbortSignal) { return this.request('/matches', { method: 'POST', headers: jsonHeaders, body: JSON.stringify(options), signal }, isMatch) }
  bid(matchId: string, bid: { quantity: number; face: number }, signal?: AbortSignal) { return this.request(`/matches/${encodeURIComponent(matchId)}/bid`, { method: 'POST', headers: jsonHeaders, body: JSON.stringify(bid), signal }, matchingMatch(matchId)) }
  challenge(matchId: string, signal?: AbortSignal) { return this.request(`/matches/${encodeURIComponent(matchId)}/challenge`, { method: 'POST', signal }, matchingMatch(matchId)) }
  spotOn(matchId: string, signal?: AbortSignal) { return this.request(`/matches/${encodeURIComponent(matchId)}/spot-on`, { method: 'POST', signal }, matchingMatch(matchId)) }
  advance(matchId: string, signal?: AbortSignal) { return this.request(`/matches/${encodeURIComponent(matchId)}/advance`, { method: 'POST', signal }, matchingMatch(matchId)) }
  nextRound(matchId: string, seed?: number, signal?: AbortSignal) { return this.request(`/matches/${encodeURIComponent(matchId)}/next-round`, { method: 'POST', headers: jsonHeaders, body: JSON.stringify({ seed }), signal }, matchingMatch(matchId)) }
  async recoverMatch(signal?: AbortSignal): Promise<LiarsDiceMatch | null> {
    const saved = this.recovery?.read()
    if (saved?.pending) {
      const pending = saved.pending
      if (!pending.path.startsWith(this.basePath + '/')) throw new LiarsDiceGatewayError('The saved command belongs to a different game service.')
      const legacyId = this.legacyMatchId(pending.path)
      if (legacyId) {
        try {
          const current = await this.getMatch(legacyId, signal)
          this.recovery?.complete(pending, current.matchId)
          return current
        } catch (reason) {
          if (reason instanceof LiarsDiceGatewayError && reason.status === 404) this.recovery?.complete(pending, null)
          throw reason
        }
      }
      const recovered = await this.request(pending.path.slice(this.basePath.length),
        { method: 'POST', headers: jsonHeaders, body: pending.body, signal }, isMatch)
      return this.getMatch(recovered.matchId, signal)
    }
    return saved?.activeBoardId ? this.getMatch(saved.activeBoardId, signal) : null
  }
  private legacyMatchId(path: string): string | undefined {
    if (!path.startsWith(this.basePath + '/')) return undefined
    return /^\/matches\/([0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12})\//i.exec(path.slice(this.basePath.length))?.[1]
  }
  private async request<T>(path: string, init: RequestInit, validate: (value: unknown) => value is T): Promise<T> {
    const target = this.basePath + path
    const body = String(init.body ?? '')
    const prior = init.method === 'POST' ? this.recovery?.read().pending : null
    if (prior?.path === target && prior.body === body && this.legacyMatchId(prior.path)) {
      const recovered: unknown = await this.recoverMatch(init.signal ?? undefined)
      if (!validate(recovered)) throw new LiarsDiceGatewayError('The saved match could not be recovered.')
      return recovered
    }
    const command = init.method === 'POST' ? this.recovery?.prepare(target, body) : undefined
    if (command) {
      const headers = new Headers(init.headers)
      headers.set('Idempotency-Key', command.idempotencyKey)
      init = { ...init, headers, body: command.body }
    }
    if (this.expectedAccountId) {
      const headers = new Headers(init.headers)
      headers.set('X-FortuneForge-Expected-Account', this.expectedAccountId)
      init = { ...init, headers }
    }
    const response = await this.fetcher(target, init)
    const value: unknown = await response.json().catch(() => null)
    if (!response.ok) {
      if (command && response.status < 500 && ![401, 403, 408, 425, 429].includes(response.status)) this.recovery?.complete(command)
      const error = isError(value) ? value : null
      throw new LiarsDiceGatewayError(error?.message ?? `The Liar's Dice request failed (${response.status}).`, error?.code, response.status)
    }
    if (!validate(value)) throw new LiarsDiceGatewayError("The Liar's Dice service returned an invalid response.")
    if (command && isMatch(value)) this.recovery?.complete(command, value.matchId)
    return value
  }
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
    && typeof value.message === 'string'
    && (value.resolvedRounds === undefined || Array.isArray(value.resolvedRounds) && value.resolvedRounds.every(round =>
      isRecord(round) && isPositiveInteger(round.roundNumber) && isOutcome(round.outcome))))) return false

  const match = value as unknown as LiarsDiceMatch
  const ids = new Set(match.players.map(player => player.id))
  if (ids.size !== match.players.length || !ids.has(match.currentPlayerId)) return false
  if (match.resolvedRounds && (new Set(match.resolvedRounds.map(round => round.roundNumber)).size !== match.resolvedRounds.length ||
    match.resolvedRounds.some(round => round.roundNumber > match.roundNumber || !ids.has(round.outcome.challengerId) ||
      !ids.has(round.outcome.bidderId) || !ids.has(round.outcome.loserId)))) return false
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
