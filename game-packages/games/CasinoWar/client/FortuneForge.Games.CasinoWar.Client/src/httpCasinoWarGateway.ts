import {
  CasinoWarGatewayError,
  type CasinoWarCard,
  type CasinoWarDecision,
  type CasinoWarGateway,
  type CasinoWarOpeningOutcome,
  type CasinoWarPrimarySettlement,
  type CasinoWarRound,
  type CasinoWarRequestOptions,
  type CasinoWarRoundOutcome,
  type CasinoWarSettlementDisposition,
  type CasinoWarStatus,
  type CasinoWarTieSettlement,
} from './contracts'

export class HttpCasinoWarGateway implements CasinoWarGateway {
  readonly basePath: string
  private readonly requestFn: typeof fetch

  constructor(basePath = '/api/games/casino-war', requestFn: typeof fetch = fetch) {
    this.basePath = basePath.replace(/\/$/, '')
    this.requestFn = requestFn
  }

  getStatus(signal?: AbortSignal) {
    return this.request('/status', { signal }, isStatus)
  }

  getRound(roundId: string, signal?: AbortSignal) {
    return this.request(`/rounds/${encodeURIComponent(roundId)}`, { signal }, isRound)
  }

  createRound(primaryStake: number, tieStake: number, options: CasinoWarRequestOptions = {}) {
    if (!isPositiveFinite(primaryStake) || !isNonNegativeFinite(tieStake))
      throw new CasinoWarGatewayError('Casino War stakes must use a positive primary stake and nonnegative Tie stake.', 'casino-war-invalid-request')
    return this.request('/rounds', jsonPost({ primaryStake, tieStake }, options.signal, options.idempotencyKey ?? createIdempotencyKey('opening')), isRound)
  }

  decide(roundId: string, decision: CasinoWarDecision, options: CasinoWarRequestOptions = {}) {
    if (!isNonBlankString(roundId) || !isNullableDecision(decision) || decision === null)
      throw new CasinoWarGatewayError('The Casino War decision request is invalid.', 'casino-war-invalid-request')
    return this.request(`/rounds/${encodeURIComponent(roundId)}/decision`, jsonPost({ decision }, options.signal, options.idempotencyKey ?? createIdempotencyKey('decision')), isRound)
  }

  private async request<T>(path: string, init: RequestInit, validate: (value: unknown) => value is T): Promise<T> {
    let response: Response
    try {
      response = await this.requestFn(`${this.basePath}${path}`, init)
    } catch (error) {
      throw new CasinoWarGatewayError(
        error instanceof Error ? error.message : 'The Casino War request could not be completed.',
      )
    }

    const value: unknown = await response.json().catch(() => null)
    if (!response.ok) {
      const error = isError(value) ? value : null
      throw new CasinoWarGatewayError(
        error?.message ?? `The Casino War request failed (${response.status}).`,
        error?.code,
        response.status,
      )
    }
    if (!validate(value)) throw new CasinoWarGatewayError('The Casino War service returned an invalid response.')
    return value
  }
}

function jsonPost(body: object, signal: AbortSignal | undefined, idempotencyKey: string): RequestInit {
  return { method: 'POST', headers: { 'content-type': 'application/json', 'Idempotency-Key': idempotencyKey }, body: JSON.stringify(body), signal }
}

function isStatus(value: unknown): value is CasinoWarStatus {
  return isRecord(value) && typeof value.available === 'boolean' &&
    isPositiveFinite(value.minimumPrimaryStake) && isPositiveFinite(value.maximumPrimaryStake) &&
    isPositiveFinite(value.stakeIncrement) && isNonNegativeFinite(value.maximumTieStake) &&
    isNonNegativeFinite(value.balance) && value.minimumPrimaryStake <= value.maximumPrimaryStake &&
    typeof value.mode === 'string' && value.mode.trim().length > 0
}

function isRound(value: unknown): value is CasinoWarRound {
  if (!isRecord(value) || !isNonBlankString(value.roundId) || !isNonNegativeFinite(value.balance) ||
    !isPositiveFinite(value.primaryStake) || !isNonNegativeFinite(value.tieStake) || !isPhase(value.phase) ||
    !isCard(value.playerOpeningCard) || !isCard(value.dealerOpeningCard) || !isNullableDecision(value.decision) ||
    !isNullableCard(value.playerWarCard) || !isNullableCard(value.dealerWarCard) ||
    !isNullablePrimarySettlement(value.primarySettlement) || !isNullableTieSettlement(value.tieSettlement)) return false

  if ((value.tieStake === 0) !== (value.tieSettlement === null)) return false
  if (value.tieSettlement !== null && !isValidTieSettlement(value.tieSettlement, value.tieStake, value.playerOpeningCard, value.dealerOpeningCard)) return false

  const openingComparison = compareCards(value.playerOpeningCard, value.dealerOpeningCard)
  if (value.phase === 'awaiting-tie-decision') {
    return openingComparison === 0 && value.decision === null && value.playerWarCard === null &&
      value.dealerWarCard === null && value.primarySettlement === null
  }

  if (value.primarySettlement === null) return false
  if (openingComparison > 0) {
    return value.decision === null && value.playerWarCard === null && value.dealerWarCard === null &&
      isPrimarySettlement(value.primarySettlement, value.primaryStake, 'player-opening-win', 'win', value.primaryStake * 2)
  }
  if (openingComparison < 0) {
    return value.decision === null && value.playerWarCard === null && value.dealerWarCard === null &&
      isPrimarySettlement(value.primarySettlement, value.primaryStake, 'dealer-opening-win', 'loss', 0)
  }
  if (value.decision === 'surrender') {
    return value.playerWarCard === null && value.dealerWarCard === null &&
      isPrimarySettlement(value.primarySettlement, value.primaryStake, 'player-surrendered', 'surrender', value.primaryStake / 2)
  }
  if (value.decision !== 'go-to-war' || value.playerWarCard === null || value.dealerWarCard === null) return false

  const warComparison = compareCards(value.playerWarCard, value.dealerWarCard)
  return warComparison > 0
    ? isPrimarySettlement(value.primarySettlement, value.primaryStake * 2, 'player-war-win', 'win', value.primaryStake * 3)
    : warComparison < 0
      ? isPrimarySettlement(value.primarySettlement, value.primaryStake * 2, 'dealer-war-win', 'loss', 0)
      : isPrimarySettlement(value.primarySettlement, value.primaryStake * 2, 'player-war-tie-win', 'win', value.primaryStake * 4)
}

function isPrimarySettlement(
  value: CasinoWarPrimarySettlement,
  totalWagered: number,
  outcome: CasinoWarRoundOutcome,
  disposition: CasinoWarSettlementDisposition,
  totalReturn: number,
): boolean {
  return value.outcome === outcome && value.disposition === disposition &&
    nearlyEqual(value.totalWagered, totalWagered) && nearlyEqual(value.totalReturn, totalReturn) &&
    nearlyEqual(value.profit, value.totalReturn - value.totalWagered)
}

function isValidTieSettlement(
  value: CasinoWarTieSettlement,
  stake: number,
  playerCard: CasinoWarCard,
  dealerCard: CasinoWarCard,
): boolean {
  const isTie = compareCards(playerCard, dealerCard) === 0
  const outcome: CasinoWarOpeningOutcome = isTie ? 'tie-decision-required' :
    compareCards(playerCard, dealerCard) > 0 ? 'player-win' : 'dealer-win'
  return value.outcome === outcome && value.won === isTie && value.disposition === (isTie ? 'win' : 'loss') &&
    nearlyEqual(value.stake, stake) && nearlyEqual(value.totalReturn, isTie ? stake * 11 : 0) &&
    nearlyEqual(value.profit, value.totalReturn - value.stake)
}

function isNullablePrimarySettlement(value: unknown): value is CasinoWarPrimarySettlement | null {
  return value === null || (isRecord(value) && isDisposition(value.disposition) && isRoundOutcome(value.outcome) &&
    isNonNegativeFinite(value.totalWagered) && isNonNegativeFinite(value.totalReturn) && isFiniteNumber(value.profit))
}

function isNullableTieSettlement(value: unknown): value is CasinoWarTieSettlement | null {
  return value === null || (isRecord(value) && typeof value.won === 'boolean' && isDisposition(value.disposition) &&
    isOpeningOutcome(value.outcome) && isPositiveFinite(value.stake) && isNonNegativeFinite(value.totalReturn) && isFiniteNumber(value.profit))
}

function isCard(value: unknown): value is CasinoWarCard {
  return isRecord(value) && isCardRank(value.rank) && isCardSuit(value.suit)
}

function isNullableCard(value: unknown): value is CasinoWarCard | null {
  return value === null || isCard(value)
}

function compareCards(playerCard: CasinoWarCard, dealerCard: CasinoWarCard): number {
  return rankStrength(playerCard.rank) - rankStrength(dealerCard.rank)
}

function rankStrength(rank: CasinoWarCard['rank']): number {
  return rank === 'ace' ? 14 : ['two', 'three', 'four', 'five', 'six', 'seven', 'eight', 'nine', 'ten', 'jack', 'queen', 'king'].indexOf(rank) + 2
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}

function isNonBlankString(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0
}

function isPositiveFinite(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value) && value > 0
}

function isNonNegativeFinite(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value) && value >= 0
}

function isFiniteNumber(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value)
}

function nearlyEqual(left: number, right: number): boolean {
  return Math.abs(left - right) <= Number.EPSILON * Math.max(1, Math.abs(left), Math.abs(right)) * 16
}

function isPhase(value: unknown): value is CasinoWarRound['phase'] {
  return value === 'awaiting-tie-decision' || value === 'completed'
}

function isNullableDecision(value: unknown): value is CasinoWarDecision | null {
  return value === null || value === 'surrender' || value === 'go-to-war'
}

function isDisposition(value: unknown): value is CasinoWarSettlementDisposition {
  return value === 'win' || value === 'loss' || value === 'surrender'
}

function isOpeningOutcome(value: unknown): value is CasinoWarOpeningOutcome {
  return value === 'player-win' || value === 'dealer-win' || value === 'tie-decision-required'
}

function isRoundOutcome(value: unknown): value is CasinoWarRoundOutcome {
  return value === 'player-opening-win' || value === 'dealer-opening-win' || value === 'player-surrendered' ||
    value === 'player-war-win' || value === 'dealer-war-win' || value === 'player-war-tie-win'
}

function isCardRank(value: unknown): value is CasinoWarCard['rank'] {
  return value === 'ace' || value === 'two' || value === 'three' || value === 'four' || value === 'five' ||
    value === 'six' || value === 'seven' || value === 'eight' || value === 'nine' || value === 'ten' ||
    value === 'jack' || value === 'queen' || value === 'king'
}

function isCardSuit(value: unknown): value is CasinoWarCard['suit'] {
  return value === 'clubs' || value === 'diamonds' || value === 'hearts' || value === 'spades'
}

function isError(value: unknown): value is { code: string; message: string } {
  return isRecord(value) && typeof value.code === 'string' && typeof value.message === 'string'
}

function createIdempotencyKey(action: 'opening' | 'decision'): string {
  const random = typeof crypto?.randomUUID === 'function'
    ? crypto.randomUUID().replaceAll('-', '')
    : `${Date.now().toString(36)}${Math.random().toString(36).slice(2)}`
  return `casino-war-${action}-${random}`
}
