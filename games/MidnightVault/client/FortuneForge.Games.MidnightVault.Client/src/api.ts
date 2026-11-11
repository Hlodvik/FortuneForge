import {
  MIDNIGHT_GAME_ID,
  MIDNIGHT_SYMBOL_SET_ID,
  midnightMathModelIds,
  midnightSymbolIds,
  type MidnightAuthenticatedFetch,
  type MidnightBonusRound,
  type MidnightCollection,
  type MidnightCollectionId,
  type MidnightDemoState,
  type MidnightSpinRequest,
  type MidnightSpinResult,
  type MidnightState,
} from './types.js'

type Problem = { error?: string; detail?: string; code?: string; available?: number; required?: number; freeSpinsRemaining?: number; retryAfterMilliseconds?: number }

export class MidnightRequestError extends Error {
  readonly status: number
  readonly code?: string
  readonly available?: number
  readonly required?: number
  readonly freeSpinsRemaining?: number
  readonly retryAfterMilliseconds?: number
  constructor(message: string, status: number, problem: Problem | null) {
    super(message)
    this.name = 'MidnightRequestError'
    this.status = status
    this.code = problem?.code
    this.available = problem?.available
    this.required = problem?.required
    this.freeSpinsRemaining = problem?.freeSpinsRemaining
    this.retryAfterMilliseconds = problem?.retryAfterMilliseconds
  }
}

const record = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === 'object' && !Array.isArray(value)
const natural = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value >= 0
const positive = (value: unknown): value is number => natural(value) && value > 0
const finite = (value: unknown): value is number => typeof value === 'number' && Number.isFinite(value)
const symbol = (value: unknown) => typeof value === 'string' && (midnightSymbolIds as readonly string[]).includes(value)
const mathModel = (value: unknown) => typeof value === 'string' && (midnightMathModelIds as readonly string[]).includes(value)
const collectionId = (value: unknown): value is MidnightCollectionId =>
  typeof value === 'string' && ['sync', 'rows', 'paw', 'rand'].includes(value)

function round(value: unknown): value is MidnightBonusRound {
  return record(value) && typeof value.id === 'string' && value.id.length > 0 &&
    positive(value.totalSpins) && value.totalSpins <= 1_000 && positive(value.remainingSpins) &&
    value.remainingSpins <= value.totalSpins && natural(value.wagerPoints) && value.wagerPoints >= 2 &&
    value.wagerPoints <= 2_000 &&
    (value.featureMode === null || typeof value.featureMode === 'string') && mathModel(value.mathModelId)
}

function collection(value: unknown): value is MidnightCollection {
  return record(value) && collectionId(value.sealId) && natural(value.count) &&
    natural(value.averageWagerPoints) && positive(value.requiredCount)
}

function demoState(value: unknown): value is MidnightDemoState {
  if (!record(value) || !Array.isArray(value.rounds) || !value.rounds.every(round) ||
    !record(value.collectionCounts) || !record(value.collectionWagerPoints)) return false
  for (const field of ['collectionCounts', 'collectionWagerPoints'] as const) {
    const values = value[field] as Record<string, unknown>
    if (!Object.entries(values).every(([key, amount]) => collectionId(key) && natural(amount) &&
      amount <= (field === 'collectionCounts' ? 29 : 58_000))) return false
  }
  return true
}

function state(value: unknown): value is Record<string, unknown> & Omit<MidnightState, 'bonusRound' | 'queuedBonusRounds'> {
  return record(value) && natural(value.freeSpinsRemaining) &&
    (value.freeSpinWagerPoints === null || natural(value.freeSpinWagerPoints)) &&
    (value.freeSpinFeatureMode === null || typeof value.freeSpinFeatureMode === 'string') &&
    natural(value.specialPointsBalance) && natural(value.energyBalance) &&
    Array.isArray(value.sealCollections) && value.sealCollections.every(collection) &&
    (value.bonusRound == null || round(value.bonusRound)) &&
    (value.queuedBonusRounds === undefined || Array.isArray(value.queuedBonusRounds) && value.queuedBonusRounds.every(round))
}

function normalizeState(value: Record<string, unknown> & Omit<MidnightState, 'bonusRound' | 'queuedBonusRounds'>): MidnightState {
  return {
    bonusRound: (value.bonusRound ?? null) as MidnightBonusRound | null,
    queuedBonusRounds: (value.queuedBonusRounds ?? []) as MidnightBonusRound[],
    freeSpinsRemaining: value.freeSpinsRemaining,
    freeSpinWagerPoints: value.freeSpinWagerPoints,
    freeSpinFeatureMode: value.freeSpinFeatureMode,
    specialPointsBalance: value.specialPointsBalance,
    energyBalance: value.energyBalance,
    sealCollections: value.sealCollections,
  }
}

function position(value: unknown, rows: number): boolean {
  return record(value) && natural(value.reel) && value.reel < 5 && natural(value.row) && value.row < rows
}

function reels(value: unknown): value is MidnightSpinResult['reels'] {
  return Array.isArray(value) && value.length === 5 && value.every(reel => Array.isArray(reel) &&
    (reel.length === 4 || reel.length === 6) && reel.every(symbol)) &&
    value.every(reel => reel.length === value[0].length)
}

function payout(value: unknown, rows: number): boolean {
  return record(value) && natural(value.totalPoints) && Array.isArray(value.paylines) && value.paylines.every(line =>
    record(line) && natural(line.paylineId) && natural(line.amountPoints) && Array.isArray(line.matches) && line.matches.every(paid =>
      record(paid) && natural(paid.amountPoints) && finite(paid.multiplier) && paid.multiplier >= 0 &&
      record(paid.match) && natural(paid.match.paylineId) && symbol(paid.match.symbolId) &&
      natural(paid.match.matchLength) && Array.isArray(paid.match.positions) &&
      paid.match.positions.every(cell => position(cell, rows)) && Array.isArray(paid.match.wildPositions) &&
      paid.match.wildPositions.every(cell => position(cell, rows))))
}

export function isMidnightSpinResult(value: unknown): value is MidnightSpinResult {
  if (!state(value) || value.gameId !== MIDNIGHT_GAME_ID || typeof value.spinId !== 'string' ||
    value.symbolSetId !== MIDNIGHT_SYMBOL_SET_ID || !mathModel(value.paytableId) ||
    typeof value.reelSetId !== 'string' || value.pointValueInCents !== 25 ||
    !natural(value.wagerPoints) || value.wagerPoints < 2 || value.wagerPoints > 2_000 ||
    !Array.isArray(value.reelStops) || value.reelStops.length !== 5 || !value.reelStops.every(natural) ||
    !reels(value.reels) || !payout(value.payout, value.reels[0].length) ||
    !natural(value.consecutiveFiveMisses) || typeof value.fiveMatchPityTriggered !== 'boolean' ||
    typeof value.isFreeSpin !== 'boolean' || !natural(value.freeSpinsAwarded) ||
    (value.collectionFreeSpinsAwarded !== undefined && !natural(value.collectionFreeSpinsAwarded)) ||
    !natural(value.specialPointsAwarded) || !natural(value.energyAwarded) ||
    typeof value.energyMultiplierApplied !== 'boolean' || !finite(value.payoutMultiplier) || value.payoutMultiplier < 1 ||
    !natural(value.monkeyPawCount) || !natural(value.moneyGrabPoints) || !natural(value.bananaBonusPoints) ||
    !record(value.sealsAwarded) || !Object.values(value.sealsAwarded).every(natural) ||
    typeof value.specialBoostApplied !== 'boolean' ||
    (value.chargedWagerCents !== undefined && !natural(value.chargedWagerCents)) ||
    (value.slotsCreditsBalance !== null && !finite(value.slotsCreditsBalance))) return false
  if (value.awardedBonusRounds !== undefined &&
    (!Array.isArray(value.awardedBonusRounds) || !value.awardedBonusRounds.every(round))) return false
  if (value.midnightBonusState != null && !demoState(value.midnightBonusState)) return false
  return true
}

async function responseJson(response: Response): Promise<unknown> {
  if (!response.ok) {
    const problem = await response.json().catch(() => null) as Problem | null
    throw new MidnightRequestError(problem?.error ?? problem?.detail ?? `Midnight Vault request failed with status ${response.status}.`, response.status, problem)
  }
  return response.json()
}

function normalizeSpin(value: MidnightSpinResult & Record<string, unknown>): MidnightSpinResult {
  return {
    ...value,
    ...normalizeState(value),
    awardedBonusRounds: (value.awardedBonusRounds ?? []) as MidnightBonusRound[],
    midnightBonusState: (value.midnightBonusState ?? null) as MidnightDemoState | null,
  }
}

export function createMidnightApi(authenticatedFetch?: MidnightAuthenticatedFetch) {
  const authenticated = (path: string, init?: RequestInit) => {
    if (!authenticatedFetch) throw new Error('Midnight Vault requires an authenticated session for paid play.')
    return authenticatedFetch(path, init)
  }
  return {
    async availability(signal: AbortSignal) {
      const response = await fetch(`/api/slots/demo/status?gameId=${MIDNIGHT_GAME_ID}`, {
        credentials: 'omit', cache: 'no-store', signal,
      })
      if (response.status !== 204) throw new Error('The Midnight Vault game service is offline.')
    },
    async state(): Promise<MidnightState> {
      const value = await responseJson(await authenticated(`/api/slots/state?gameId=${MIDNIGHT_GAME_ID}`, { method: 'GET' }))
      if (!state(value)) throw new Error('The Midnight Vault server returned an invalid bonus state.')
      return normalizeState(value)
    },
    async spin(request: MidnightSpinRequest, demo: boolean): Promise<MidnightSpinResult> {
      if (!Number.isSafeInteger(request.wagerPoints) || request.wagerPoints < 2 || request.wagerPoints > 2_000 ||
        (!request.useFreeSpin && request.wagerPoints % 2 !== 0)) {
        throw new Error('Midnight Vault received an invalid wager for this round.')
      }
      const common = { gameId: MIDNIGHT_GAME_ID, wagerPoints: request.wagerPoints, useFreeSpin: request.useFreeSpin }
      const response = demo
        ? await fetch('/api/slots/demo/spins', {
            method: 'POST', credentials: 'omit', headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ ...common, freeSpinsRemaining: request.state.freeSpinsRemaining,
              freeSpinWagerPoints: request.state.freeSpinWagerPoints, energyBalance: 0,
              sealCollections: request.state.sealCollections, freeSpinFeatureMode: request.state.freeSpinFeatureMode,
              midnightBonusState: request.demoState }),
          })
        : await authenticated('/api/slots/spins', {
            method: 'POST', body: JSON.stringify({ ...common, useSpecialBoost: false }),
          })
      const value = await responseJson(response)
      if (!isMidnightSpinResult(value)) throw new Error('The Midnight Vault server returned an invalid reel result.')
      return normalizeSpin(value as MidnightSpinResult & Record<string, unknown>)
    },
  }
}
