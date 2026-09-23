import {
  CrapsGatewayError,
  type CrapsGateway,
  type CrapsOutcome,
  type CrapsPhase,
  type CrapsRoll,
  type CrapsRollResult,
  type CrapsRound,
  type CrapsStatus,
} from './contracts'

export class HttpCrapsGateway implements CrapsGateway {
  readonly basePath: string

  constructor(basePath = '/api/games/craps') {
    this.basePath = basePath.replace(/\/$/, '')
  }

  getStatus(signal?: AbortSignal): Promise<CrapsStatus> {
    return this.request('/status', { signal }, isCrapsStatus)
  }

  startRound(stake: number, signal?: AbortSignal): Promise<CrapsRound> {
    return this.request('/rounds', {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ stake }),
      signal,
    }, isCrapsRound)
  }

  roll(roundId: string, signal?: AbortSignal): Promise<CrapsRound> {
    return this.request(`/rounds/${encodeURIComponent(roundId)}/roll`, {
      method: 'POST',
      signal,
    }, isCrapsRound)
  }

  private async request<T>(
    path: string,
    init: RequestInit,
    validate: (value: unknown) => value is T,
  ): Promise<T> {
    const response = await fetch(`${this.basePath}${path}`, init)
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
    && isPositiveNumber(value.stakeIncrement)
    && typeof value.mode === 'string'
}

function isCrapsRound(value: unknown): value is CrapsRound {
  return isRecord(value)
    && typeof value.roundId === 'string'
    && isPositiveNumber(value.stake)
    && isPhase(value.phase)
    && (value.point === null || isDieTotal(value.point))
    && Array.isArray(value.rolls)
    && value.rolls.every(isRoll)
    && (value.lastOutcome === null || isOutcome(value.lastOutcome))
}

function isRoll(value: unknown): value is CrapsRoll {
  return isRecord(value)
    && Number.isInteger(value.rollNumber)
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
    && (value.totalReturn === null || isNonNegativeNumber(value.totalReturn))
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

function isDieTotal(value: unknown): value is number {
  return Number.isInteger(value) && Number(value) >= 2 && Number(value) <= 12
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
