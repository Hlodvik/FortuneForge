import { HeartsGatewayError, type HeartsDifficulty, type HeartsGateway, type HeartsMatch, type HeartsOnboardingGateway, type HeartsOnboardingState, type HeartsPaidGateway, type HeartsPaidRequest, type HeartsPaidSession, type HeartsPaidTable, type HeartsSeat, type HeartsStatus } from './contracts'

export class HttpHeartsGateway implements HeartsGateway {
  readonly basePath: string
  readonly paid: HeartsPaidGateway
  readonly onboarding: HeartsOnboardingGateway
  constructor(basePath = '/api/games/hearts', private readonly requestFn: typeof fetch = fetch) {
    this.basePath = basePath.replace(/\/$/, '')
    this.onboarding = {
      getState: signal => this.request('/onboarding', { signal }, isOnboardingState),
      confirmExperience: signal => this.request('/onboarding', jsonPost({ hasPlayedBefore: true }, { signal }), isOnboardingState),
    }
    const mutate = (path: string, payload: object, options: HeartsPaidRequest) => this.request('/paid' + path, jsonPost({ ...payload, expectedVersion: options.expectedVersion }, options), isPaidSession)
    this.paid = {
      getSession: signal => this.request('/paid/session', { signal }, isPaidSession),
      join: (entryCredits, options) => mutate('/join', { entryCredits }, options),
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
  pass(matchId: string, cards: readonly string[], signal?: AbortSignal) { return this.request(`/matches/${encodeURIComponent(matchId)}/pass`, jsonPost({ cards }, { signal }), isMatch) }
  playCard(matchId: string, card: string, signal?: AbortSignal) { return this.request(`/matches/${encodeURIComponent(matchId)}/card`, jsonPost({ card }, { signal }), isMatch) }
  advance(matchId: string, signal?: AbortSignal) { return this.request(`/matches/${encodeURIComponent(matchId)}/advance`, jsonPost({}, { signal }), isMatch) }
  nextRound(matchId: string, seed?: number, signal?: AbortSignal) { return this.request(`/matches/${encodeURIComponent(matchId)}/next-round`, jsonPost({ seed }, { signal }), isMatch) }
  private async request<T>(path: string, init: RequestInit, validate: (value: unknown) => value is T): Promise<T> {
    const response = await this.requestFn(`${this.basePath}${path}`, init)
    const value: unknown = await response.json().catch(() => null)
    if (!response.ok) { const error = isError(value) ? value : null; throw new HeartsGatewayError(error?.message ?? `The Hearts table request failed (${response.status}).`, error?.code, response.status) }
    if (!validate(value)) throw new HeartsGatewayError('The Hearts service returned an invalid response.', 'hearts-invalid-response')
    return value
  }
}

function jsonPost(payload: object, options: { signal?: AbortSignal; idempotencyKey?: string }): RequestInit {
  return { method: 'POST', headers: { 'content-type': 'application/json', ...(options.idempotencyKey ? { 'Idempotency-Key': options.idempotencyKey } : {}) }, body: JSON.stringify(payload), signal: options.signal }
}
function isRecord(value: unknown): value is Record<string, unknown> { return typeof value === 'object' && value !== null }
function isStatus(value: unknown): value is HeartsStatus { return isRecord(value) && typeof value.available === 'boolean' && Number.isInteger(value.defaultTargetScore) && typeof value.mode === 'string' }
function isOnboardingState(value: unknown): value is HeartsOnboardingState { return isRecord(value) && typeof value.shouldAskPlayedBefore === 'boolean' }
function isTrialMatch(value: unknown): value is HeartsMatch { return isMatch(value) && value.isTrial === true }
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
  if (value.kind === 'queue') return value.table === null && isRecord(value.queue) && typeof value.queue.matchId === 'string' && money(value.queue.entryCredits) && money(value.queue.potCredits) && typeof value.queue.joinedAtUtc === 'string' && typeof value.queue.message === 'string' && Array.isArray(value.queue.players) && value.queue.players.every(player => isRecord(player) && isSeat(player.seat) && typeof player.displayName === 'string' && typeof player.isCurrentPlayer === 'boolean')
  return value.queue === null && isPaidTable(value.table)
}
function isHistory(value: unknown): value is { items: HeartsPaidTable[] } { return isRecord(value) && Array.isArray(value.items) && value.items.every(isPaidTable) }
function isError(value: unknown): value is { code: string; message: string } { return isRecord(value) && typeof value.code === 'string' && typeof value.message === 'string' }
