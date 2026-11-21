import type { GameCommandRecovery, PendingGameCommand } from '@fortuneforge/client-presentation/game-command-recovery'
import { HeartsGatewayError, type HeartsDifficulty, type HeartsGateway, type HeartsMatch, type HeartsOnboardingGateway, type HeartsOnboardingState, type HeartsPaidGateway, type HeartsPaidRequest, type HeartsPaidSession, type HeartsPaidTable, type HeartsRecoveredSession, type HeartsSeat, type HeartsStatus } from './contracts'

export class HttpHeartsGateway implements HeartsGateway {
  readonly basePath: string
  readonly paid: HeartsPaidGateway
  readonly onboarding: HeartsOnboardingGateway
  constructor(basePath = '/api/games/hearts', private readonly requestFn: typeof fetch = fetch,
    private readonly recovery?: GameCommandRecovery, private readonly expectedAccountId?: string) {
    this.basePath = basePath.replace(/\/$/, '')
    this.onboarding = {
      getState: async signal => { await this.recoverPending(signal); return this.request('/onboarding', { signal }, isOnboardingState) },
      confirmExperience: signal => this.request('/onboarding', jsonPost({ hasPlayedBefore: true }, { signal }), isOnboardingState),
    }
    const mutate = (path: string, payload: object, options: HeartsPaidRequest) => this.request('/paid' + path, jsonPost({ ...payload, expectedVersion: options.expectedVersion }, options), isPaidSession)
    this.paid = {
      getSession: async signal => { await this.recoverPending(signal); return this.request('/paid/session', { signal }, isPaidSession) },
      join: (entryCredits, options) => mutate('/join', { entryCredits }, options),
      playHouse: (id, options) => mutate(`/matches/${encodeURIComponent(id)}/play-house`, {}, options),
      getMatch: (id, signal) => this.request(`/paid/matches/${encodeURIComponent(id)}`, { signal }, isPaidSession),
      pass: (id, cards, options) => mutate(`/matches/${encodeURIComponent(id)}/pass`, { cards }, options),
      play: (id, card, options) => mutate(`/matches/${encodeURIComponent(id)}/card`, { card }, options),
      advance: (id, options) => mutate(`/matches/${encodeURIComponent(id)}/advance`, {}, options),
      nextRound: (id, options) => mutate(`/matches/${encodeURIComponent(id)}/next-round`, {}, options),
      leave: (id, options) => mutate(`/matches/${encodeURIComponent(id)}/leave`, {}, options),
      getHistory: async signal => (await this.request('/paid/history', { signal }, isHistory)).items,
    }
  }
  getStatus(signal?: AbortSignal) { return this.request('/status', { signal }, isStatus) }
  startTrial(signal?: AbortSignal) { return this.request('/trial', { method: 'POST', signal }, isTrialMatch) }
  cancelTrial(matchId: string, signal?: AbortSignal) { return this.request(`/trial/${encodeURIComponent(matchId)}/cancel`, { method: 'POST', signal }, isOnboardingState) }
  startMatch(options: { targetScore: number; difficulty?: HeartsDifficulty; seed?: number }, signal?: AbortSignal) { return this.request('/matches', jsonPost(options, { signal }), isMatch) }
  leaveMatch(matchId: string, signal?: AbortSignal) { return this.request(`/matches/${encodeURIComponent(matchId)}/leave`, { method: 'POST', signal }, isEmpty) }
  pass(matchId: string, cards: readonly string[], signal?: AbortSignal) { return this.request(`/matches/${encodeURIComponent(matchId)}/pass`, jsonPost({ cards }, { signal }), matchingMatch(matchId)) }
  playCard(matchId: string, card: string, signal?: AbortSignal) { return this.request(`/matches/${encodeURIComponent(matchId)}/card`, jsonPost({ card }, { signal }), matchingMatch(matchId)) }
  advance(matchId: string, signal?: AbortSignal) { return this.request(`/matches/${encodeURIComponent(matchId)}/advance`, jsonPost({}, { signal }), matchingMatch(matchId)) }
  nextRound(matchId: string, seed?: number, signal?: AbortSignal) { return this.request(`/matches/${encodeURIComponent(matchId)}/next-round`, jsonPost({ seed }, { signal }), matchingMatch(matchId)) }

  async recoverSession(signal?: AbortSignal): Promise<HeartsRecoveredSession | null> {
    const recovered = await this.recoverPending(signal)
    if (recovered?.kind === 'paid') return { kind: 'paid', session: await this.request('/paid/session', { signal }, isPaidSession) }
    if (recovered?.kind === 'closed') return recovered
    const matchId = recovered?.kind === 'practice' ? recovered.match.matchId : this.recovery?.read().activeBoardId
    if (!matchId) return null
    try {
      return { kind: 'practice', match: await this.request(`/matches/${encodeURIComponent(matchId)}`, { signal }, matchingMatch(matchId)) }
    } catch (reason) {
      if (reason instanceof HeartsGatewayError && reason.status === 404) {
        this.recovery?.setActiveBoardId(null)
        return { kind: 'closed' }
      }
      throw reason
    }
  }

  private async recoverPending(signal?: AbortSignal): Promise<HeartsRecoveredSession | null> {
    const command = this.recovery?.read().pending
    if (!command) return null
    if (!command.path.startsWith(this.basePath + '/')) throw new HeartsGatewayError('The saved command belongs to a different game service.')
    const path = command.path.slice(this.basePath.length)
    const init: RequestInit = { method: 'POST', ...(command.body ? { headers: { 'content-type': 'application/json' } } : {}), body: command.body, signal }
    if (path === '/paid/join' || /^\/paid\/matches\/[^/]+\/(?:play-house|pass|card|advance|next-round|leave)$/u.test(path))
      return { kind: 'paid', session: await this.request(path, init, isPaidSession) }
    if (path === '/onboarding') { await this.request(path, init, isOnboardingState); return null }
    if (/^\/trial\/[^/]+\/cancel$/u.test(path)) { await this.request(path, init, isOnboardingState); return { kind: 'closed' } }
    if (/^\/matches\/[^/]+\/leave$/u.test(path)) { await this.request(path, init, isEmpty); return { kind: 'closed' } }
    if (path === '/trial') return { kind: 'practice', match: await this.request(path, init, isTrialMatch) }
    if (path === '/matches') return { kind: 'practice', match: await this.request(path, init, isMatch) }
    const action = /^\/matches\/([^/]+)\/(?:pass|card|advance|next-round)$/u.exec(path)
    if (action) return { kind: 'practice', match: await this.request(path, init, matchingMatch(decodeURIComponent(action[1]))) }
    throw new HeartsGatewayError('The saved Hearts command cannot be recovered.', 'hearts-invalid-command')
  }

  private async request<T>(path: string, init: RequestInit, validate: (value: unknown) => value is T): Promise<T> {
    const command = init.method === 'POST' ? this.recovery?.prepare(this.basePath + path, String(init.body ?? ''),
      new Headers(init.headers).get('Idempotency-Key') ?? undefined) : undefined
    if (command || this.expectedAccountId) {
      const headers = new Headers(init.headers)
      if (command) headers.set('Idempotency-Key', command.idempotencyKey)
      if (this.expectedAccountId) headers.set('X-FortuneForge-Expected-Account', this.expectedAccountId)
      init = { ...init, headers, ...(command ? { body: command.body } : {}) }
    }
    const response = await this.requestFn(`${this.basePath}${path}`, init)
    const value: unknown = response.status === 204 ? undefined : await response.json().catch(() => null)
    if (!response.ok) {
      if (command && response.status < 500 && ![401, 403, 408, 425, 429].includes(response.status)) this.recovery?.complete(command)
      const error = isError(value) ? value : null
      throw new HeartsGatewayError(error?.message ?? `The Hearts table request failed (${response.status}).`, error?.code, response.status)
    }
    if (!validate(value)) throw new HeartsGatewayError('The Hearts service returned an invalid response.', 'hearts-invalid-response')
    if (command) this.complete(command, path, value)
    return value
  }

  private complete(command: PendingGameCommand, path: string, value: unknown) {
    const closed = path.startsWith('/paid/') || path.endsWith('/leave') || path.endsWith('/cancel')
    this.recovery?.complete(command, isMatch(value) ? value.matchId : closed ? null : undefined)
  }
}

function jsonPost(payload: object, options: { signal?: AbortSignal; idempotencyKey?: string }): RequestInit {
  return { method: 'POST', headers: { 'content-type': 'application/json', ...(options.idempotencyKey ? { 'Idempotency-Key': options.idempotencyKey } : {}) }, body: JSON.stringify(payload), signal: options.signal }
}
function isRecord(value: unknown): value is Record<string, unknown> { return typeof value === 'object' && value !== null }
function isStatus(value: unknown): value is HeartsStatus { return isRecord(value) && typeof value.available === 'boolean' && Number.isInteger(value.defaultTargetScore) && typeof value.mode === 'string' }
function isOnboardingState(value: unknown): value is HeartsOnboardingState { return isRecord(value) && typeof value.shouldAskPlayedBefore === 'boolean' }
function isTrialMatch(value: unknown): value is HeartsMatch { return isMatch(value) && value.isTrial === true }
function isEmpty(value: unknown): value is void { return value === undefined || value === null }
function matchingMatch(matchId: string): (value: unknown) => value is HeartsMatch { return (value): value is HeartsMatch => isMatch(value) && value.matchId === matchId }
export function isMatch(value: unknown): value is HeartsMatch { return isRecord(value) && typeof value.matchId === 'string' && Number.isInteger(value.targetScore) && Number.isInteger(value.roundNumber) && isPhase(value.phase) && isDirection(value.passDirection) && isSeat(value.turn) && isSeat(value.humanSeat) && typeof value.yourTurn === 'boolean' && typeof value.opponentsThinking === 'boolean' && typeof value.heartsBroken === 'boolean' && Array.isArray(value.players) && value.players.every(isPlayer) && Array.isArray(value.hand) && value.hand.every(isCard) && Array.isArray(value.legalCards) && value.legalCards.every(isCard) && Array.isArray(value.submittedPasses) && value.submittedPasses.every(isSeat) && isTrick(value.currentTrick) && (value.recentTrick === null || isRecentTrick(value.recentTrick)) && Array.isArray(value.completedTricks) && isScore(value.score) && isScore(value.roundScore) && isDifficulty(value.difficulty) && (value.winner === null || isSeat(value.winner)) && typeof value.message === 'string' && (value.isTrial === undefined || typeof value.isTrial === 'boolean') }
function isPlayer(value: unknown): boolean { return isRecord(value) && isSeat(value.seat) && typeof value.displayName === 'string' && Number.isInteger(value.handCount) && Number.isInteger(value.roundScore) && Number.isInteger(value.matchScore) && typeof value.hasPassed === 'boolean' }
function isCard(value: unknown): boolean { return isRecord(value) && typeof value.code === 'string' && typeof value.rank === 'string' && typeof value.label === 'string' && typeof value.suit === 'string' }
function isPlay(value: unknown): boolean { return isRecord(value) && isSeat(value.seat) && isCard(value.card) }
function isTrick(value: unknown): boolean { return isRecord(value) && isSeat(value.leader) && Array.isArray(value.plays) && value.plays.every(isPlay) }
function isRecentTrick(value: unknown): boolean { return isTrick(value) && isRecord(value) && Number.isInteger(value.number) && isSeat(value.winner) && Number.isInteger(value.points) }
function isScore(value: unknown): boolean { return isRecord(value) && ['north', 'east', 'south', 'west'].every(seat => typeof value[seat] === 'number' && Number.isFinite(value[seat])) }
function isPhase(value: unknown): value is HeartsMatch['phase'] { return value === 'passing' || value === 'playing' || value === 'complete' }
function isDirection(value: unknown): boolean { return value === 'left' || value === 'right' || value === 'across' || value === 'hold' }
function isDifficulty(value: unknown): value is HeartsDifficulty { return value === 'relaxed' || value === 'standard' || value === 'sharp' }
function isSeat(value: unknown): value is HeartsSeat { return value === 'north' || value === 'east' || value === 'south' || value === 'west' }
function money(value: unknown): value is number { return typeof value === 'number' && Number.isFinite(value) && value >= 0 }
function isPaidTable(value: unknown): value is HeartsPaidTable { return isRecord(value) && isMatch(value.match) && money(value.entryCredits) && money(value.potCredits) && money(value.payoutCredits) && Array.isArray(value.winnerSeats) && value.winnerSeats.every(isSeat) && Array.isArray(value.players) && value.players.every(player => isRecord(player) && isSeat(player.seat) && typeof player.displayName === 'string' && typeof player.isCurrentPlayer === 'boolean' && typeof player.hasLeft === 'boolean' && money(player.entryCredits) && money(player.payoutCredits)) && (value.actionDeadlineAtUtc === null || typeof value.actionDeadlineAtUtc === 'string') && (value.completedAtUtc === null || typeof value.completedAtUtc === 'string') }
export function isPaidSession(value: unknown): value is HeartsPaidSession {
  if (!isRecord(value) || !['idle', 'queue', 'match', 'result'].includes(String(value.kind)) || !Number.isInteger(value.version) || !money(value.balanceCredits) || !Array.isArray(value.entryOptions) || !value.entryOptions.every(option => money(option) && option > 0)) return false
  if (value.kind === 'idle') return value.queue === null && value.table === null
  if (value.kind === 'queue') return value.table === null && isRecord(value.queue) && typeof value.queue.matchId === 'string' && money(value.queue.entryCredits) && money(value.queue.potCredits) && typeof value.queue.joinedAtUtc === 'string' && typeof value.queue.message === 'string' && (value.queue.canPlayHouse === undefined || typeof value.queue.canPlayHouse === 'boolean') && Array.isArray(value.queue.players) && value.queue.players.every(player => isRecord(player) && isSeat(player.seat) && typeof player.displayName === 'string' && typeof player.isCurrentPlayer === 'boolean')
  return value.queue === null && isPaidTable(value.table)
}
function isHistory(value: unknown): value is { items: HeartsPaidTable[] } { return isRecord(value) && Array.isArray(value.items) && value.items.every(isPaidTable) }
function isError(value: unknown): value is { code: string; message: string } { return isRecord(value) && typeof value.code === 'string' && typeof value.message === 'string' }
