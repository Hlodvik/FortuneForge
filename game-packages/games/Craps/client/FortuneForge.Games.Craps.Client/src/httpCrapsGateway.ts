import {
  CrapsGatewayError,
  type CrapsGateway,
  type CrapsExtraBetRequest,
  type CrapsOutcome,
  type CrapsPhase,
  type CrapsRoll,
  type CrapsRollResult,
  type CrapsRound,
  type CrapsStatus,
} from './contracts'

export class HttpCrapsGateway implements CrapsGateway {
  readonly basePath: string
  private readonly fetcher: typeof fetch

  constructor(basePath = '/api/games/craps', fetcher: typeof fetch = (...args) => fetch(...args)) {
    this.basePath = basePath.replace(/\/$/, '')
    this.fetcher = fetcher
  }

  getStatus(signal?: AbortSignal): Promise<CrapsStatus> {
    return this.request('/status', { signal }, isCrapsStatus)
  }

  getRound(roundId: string, signal?: AbortSignal): Promise<CrapsRound> {
    return this.request(`/rounds/${encodeURIComponent(roundId)}`, { signal }, matchingRound(roundId))
  }

  startRound(stake: number, signal?: AbortSignal, extraBets?: readonly CrapsExtraBetRequest[]): Promise<CrapsRound> {
    return this.request('/rounds', {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ stake, extraBets: extraBets ?? [] }),
      signal,
    }, isCrapsRound)
  }

  placeOdds(roundId: string, stake: number, signal?: AbortSignal): Promise<CrapsRound> {
    return this.request(`/rounds/${encodeURIComponent(roundId)}/odds`, {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ stake }),
      signal,
    }, matchingRound(roundId))
  }

  roll(roundId: string, signal?: AbortSignal): Promise<CrapsRound> {
    return this.request(`/rounds/${encodeURIComponent(roundId)}/roll`, {
      method: 'POST',
      signal,
    }, matchingRound(roundId))
  }

  private async request<T>(
    path: string,
    init: RequestInit,
    validate: (value: unknown) => value is T,
  ): Promise<T> {
    const response = await this.fetcher(`${this.basePath}${path}`, init)
    const value: unknown = await response.json().catch(() => null)
    if (!response.ok) {
      const error = isErrorResponse(value) ? value : null
      throw new CrapsGatewayError(
        error?.message ?? `The Craps table request failed (${response.status}).`,
        error?.code,
        response.status,
      )
    }
    if (!validate(value)) {
      throw new CrapsGatewayError('The Craps service returned an invalid response.')
    }
    return value
  }
}

function isCrapsStatus(value: unknown): value is CrapsStatus {
  return isRecord(value)
    && typeof value.available === 'boolean'
    && isPositiveNumber(value.minimumStake)
    && isPositiveNumber(value.maximumStake)
    && value.minimumStake <= value.maximumStake
    && isPositiveNumber(value.stakeIncrement)
    && typeof value.mode === 'string'
    && value.mode.trim().length > 0
}

function isCrapsRound(value: unknown): value is CrapsRound {
  if (!(isRecord(value)
    && typeof value.roundId === 'string'
    && value.roundId.trim().length > 0
    && isPositiveNumber(value.stake)
    && isPhase(value.phase)
    && (value.point === null || isPoint(value.point))
    && Array.isArray(value.rolls)
    && value.rolls.every((roll, index) => isRoll(roll) && roll.rollNumber === index + 1)
    && (value.lastOutcome === null || isOutcome(value.lastOutcome))
    && (value.extraBets === undefined || (Array.isArray(value.extraBets) && value.extraBets.every(isExtraBet))))) return false

  const round = value as unknown as CrapsRound
  const latest = round.rolls.at(-1)
  const outcome = round.lastOutcome
  if (round.phase === 'come-out') {
    if (round.point !== null || latest !== undefined || outcome !== null) return false
  } else {
    if (!latest || !outcome || latest.first !== outcome.first || latest.second !== outcome.second
      || latest.total !== outcome.total || latest.result !== outcome.result) return false
    if (round.phase === 'point' && (round.point === null || outcome.isTerminal)) return false
    if (round.phase === 'resolved' && !outcome.isTerminal) return false
    if (!matchesResult(outcome, round.point)) return false
  }
  return (round.extraBets ?? []).every(bet => {
    if (bet.kind === 'odds') return round.point !== null && (round.phase === 'resolved' ? bet.resolved : !bet.resolved)
    return latest ? bet.resolved : !bet.resolved
  })
}

function matchingRound(roundId: string): (value: unknown) => value is CrapsRound {
  return (value): value is CrapsRound => isCrapsRound(value) && value.roundId === roundId
}

function isExtraBet(value: unknown): boolean {
  return isRecord(value)
    && (value.kind === 'field' || value.kind === 'any-seven' || value.kind === 'any-craps' || value.kind === 'odds')
    && isPositiveNumber(value.stake)
    && typeof value.resolved === 'boolean'
    && typeof value.won === 'boolean'
    && (value.resolved
      ? isNonNegativeNumber(value.totalReturn) && value.won === (value.totalReturn > 0)
      : value.totalReturn === null && !value.won)
}

function isRoll(value: unknown): value is CrapsRoll {
  return isRecord(value)
    && Number.isInteger(value.rollNumber)
    && Number(value.rollNumber) > 0
    && isDie(value.first)
    && isDie(value.second)
    && value.total === value.first + value.second
    && (value.result === null || isResult(value.result))
}

function isOutcome(value: unknown): value is CrapsOutcome {
  return isRecord(value)
    && isDie(value.first)
    && isDie(value.second)
    && value.total === value.first + value.second
    && isResult(value.result)
    && typeof value.isTerminal === 'boolean'
    && value.isTerminal === isTerminalResult(value.result)
    && (value.isTerminal ? isNonNegativeNumber(value.totalReturn) : value.totalReturn === null)
}

function matchesResult(outcome: CrapsOutcome, point: number | null): boolean {
  switch (outcome.result) {
    case 'natural-win': return point === null && (outcome.total === 7 || outcome.total === 11)
    case 'craps-loss': return point === null && (outcome.total === 2 || outcome.total === 3 || outcome.total === 12)
    case 'point-established': return point !== null && outcome.total === point
    case 'point-hit': return point !== null && outcome.total === point
    case 'seven-out': return point !== null && outcome.total === 7
    case 'no-decision': return point !== null && outcome.total !== point && outcome.total !== 7
  }
}

function isTerminalResult(result: CrapsRollResult): boolean {
  return result === 'natural-win' || result === 'craps-loss' || result === 'point-hit' || result === 'seven-out'
}

function isErrorResponse(value: unknown): value is { code: string; message: string } {
  return isRecord(value) && typeof value.code === 'string' && typeof value.message === 'string'
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}

function isPositiveNumber(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value) && value > 0
}

function isNonNegativeNumber(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value) && value >= 0
}

function isDie(value: unknown): value is number {
  return Number.isInteger(value) && Number(value) >= 1 && Number(value) <= 6
}

function isPoint(value: unknown): value is number {
  return value === 4 || value === 5 || value === 6 || value === 8 || value === 9 || value === 10
}

function isPhase(value: unknown): value is CrapsPhase {
  return value === 'come-out' || value === 'point' || value === 'resolved'
}

function isResult(value: unknown): value is CrapsRollResult {
  return value === 'natural-win'
    || value === 'craps-loss'
    || value === 'point-established'
    || value === 'point-hit'
    || value === 'seven-out'
    || value === 'no-decision'
}
