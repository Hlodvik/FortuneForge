import {
  GOLD_FOUNDRY_GAME_ID,
  GOLD_FOUNDRY_PAYTABLE_ID,
  GOLD_FOUNDRY_SYMBOL_SET_ID,
  goldFoundrySymbolIds,
  type GoldFoundryAuthenticatedFetch,
  type GoldFoundrySpinRequest,
  type GoldFoundrySpinResult,
  type GoldFoundryState,
} from './types.js'

type Problem = { error?: string; detail?: string; code?: string; available?: number; required?: number; retryAfterMilliseconds?: number }

export class GoldFoundryRequestError extends Error {
  readonly status: number
  readonly code?: string
  readonly available?: number
  readonly required?: number
  readonly retryAfterMilliseconds?: number
  constructor(message: string, status: number, problem: Problem | null) {
    super(message)
    this.name = 'GoldFoundryRequestError'
    this.status = status
    this.code = problem?.code
    this.available = problem?.available
    this.required = problem?.required
    this.retryAfterMilliseconds = problem?.retryAfterMilliseconds
  }
}

const record = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === 'object' && !Array.isArray(value)
const natural = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value >= 0
const finite = (value: unknown): value is number => typeof value === 'number' && Number.isFinite(value)
const symbol = (value: unknown) => typeof value === 'string' && (goldFoundrySymbolIds as readonly string[]).includes(value)
const optionalNatural = (value: unknown) => value === undefined || natural(value)

function validState(value: unknown): value is Record<string, unknown> {
  return record(value) && natural(value.freeSpinsRemaining) && value.freeSpinsRemaining === 0 &&
    value.freeSpinWagerPoints === null && value.specialPointsBalance === 0 && natural(value.energyBalance) &&
    Array.isArray(value.sealCollections) && value.sealCollections.length === 0 && value.freeSpinFeatureMode === null &&
    optionalNatural(value.crucibleWageredCents) && optionalNatural(value.cruciblePourValueCents)
}

function normalizeState(value: Record<string, unknown>): GoldFoundryState {
  return {
    freeSpinsRemaining: 0, freeSpinWagerPoints: null,
    specialPointsBalance: value.specialPointsBalance as number,
    energyBalance: value.energyBalance as number,
    sealCollections: [], freeSpinFeatureMode: null,
    crucibleWageredCents: (value.crucibleWageredCents as number | undefined) ?? 0,
    cruciblePourValueCents: (value.cruciblePourValueCents as number | undefined) ?? 0,
  }
}

function validPosition(value: unknown, rows: number) {
  return record(value) && natural(value.reel) && value.reel < 5 && natural(value.row) && value.row < rows
}

function validReels(value: unknown): value is GoldFoundrySpinResult['reels'] {
  return Array.isArray(value) && value.length === 5 && value.every(reel =>
    Array.isArray(reel) && reel.length === 4 && reel.every(symbol))
}

function validPayout(value: unknown, rows: number) {
  return record(value) && natural(value.totalPoints) && Array.isArray(value.paylines) && value.paylines.every(line =>
    record(line) && natural(line.paylineId) && natural(line.amountPoints) && Array.isArray(line.matches) && line.matches.every(paid =>
      record(paid) && natural(paid.amountPoints) && finite(paid.multiplier) && paid.multiplier >= 0 &&
      record(paid.match) && natural(paid.match.paylineId) && symbol(paid.match.symbolId) &&
      natural(paid.match.matchLength) && Array.isArray(paid.match.positions) && paid.match.positions.every(cell => validPosition(cell, rows)) &&
      Array.isArray(paid.match.wildPositions) && paid.match.wildPositions.every(cell => validPosition(cell, rows))))
}

export function isGoldFoundrySpinResult(value: unknown): value is GoldFoundrySpinResult {
  return record(value) && value.gameId === GOLD_FOUNDRY_GAME_ID && typeof value.spinId === 'string' &&
    value.reelSetId === 'gold-foundry-reels-v1' && value.symbolSetId === GOLD_FOUNDRY_SYMBOL_SET_ID &&
    value.paytableId === GOLD_FOUNDRY_PAYTABLE_ID && natural(value.wagerPoints) && value.wagerPoints >= 2 &&
    value.wagerPoints <= 2_000 && value.pointValueInCents === 25 && optionalNatural(value.chargedWagerCents) &&
    Array.isArray(value.reelStops) && value.reelStops.length === 5 && value.reelStops.every(natural) &&
    validReels(value.reels) && validPayout(value.payout, 4) && natural(value.consecutiveFiveMisses) &&
    typeof value.fiveMatchPityTriggered === 'boolean' && value.isFreeSpin === false && value.freeSpinsAwarded === 0 &&
    value.freeSpinsRemaining === 0 && value.freeSpinWagerPoints === null && value.specialPointsAwarded === 0 &&
    value.specialPointsBalance === 0 && natural(value.energyAwarded) && natural(value.energyBalance) &&
    value.energyMultiplierApplied === false && optionalNatural(value.crucibleWageredCents) &&
    optionalNatural(value.cruciblePourValueCents) && optionalNatural(value.cruciblePayoutCents) &&
    (value.cruciblePoured === undefined || typeof value.cruciblePoured === 'boolean') && value.payoutMultiplier === 1 &&
    natural(value.monkeyPawCount) && natural(value.moneyGrabPoints) && natural(value.bananaBonusPoints) &&
    record(value.sealsAwarded) && Object.keys(value.sealsAwarded).length === 0 &&
    Array.isArray(value.sealCollections) && value.sealCollections.length === 0 && value.freeSpinFeatureMode === null &&
    value.specialBoostApplied === false && (value.slotsCreditsBalance === null || finite(value.slotsCreditsBalance))
}

async function json(response: Response): Promise<unknown> {
  if (!response.ok) {
    const problem = await response.json().catch(() => null) as Problem | null
    throw new GoldFoundryRequestError(problem?.error ?? problem?.detail ??
      `Gold Foundry request failed with status ${response.status}.`, response.status, problem)
  }
  return response.json()
}

export function createGoldFoundryApi(authenticatedFetch?: GoldFoundryAuthenticatedFetch) {
  const authenticated = (path: string, init?: RequestInit) => {
    if (!authenticatedFetch) throw new Error('Gold Foundry requires an authenticated session for paid play.')
    return authenticatedFetch(path, init)
  }
  return {
    async availability(signal: AbortSignal) {
      const response = await fetch(`/api/slots/demo/status?gameId=${GOLD_FOUNDRY_GAME_ID}`, {
        credentials: 'omit', cache: 'no-store', signal,
      })
      if (response.status !== 204) throw new Error('The Gold Foundry game service is offline.')
    },
    async state(): Promise<GoldFoundryState> {
      const value = await json(await authenticated(`/api/slots/state?gameId=${GOLD_FOUNDRY_GAME_ID}`, { method: 'GET' }))
      if (!validState(value)) throw new Error('The Gold Foundry server returned an invalid crucible state.')
      return normalizeState(value)
    },
    async spin(request: GoldFoundrySpinRequest, demo: boolean): Promise<GoldFoundrySpinResult> {
      if (!Number.isSafeInteger(request.wagerPoints) || request.wagerPoints < 2 || request.wagerPoints > 2_000 || request.wagerPoints % 2 !== 0)
        throw new Error('Gold Foundry received an invalid wager for this round.')
      const common = { gameId: GOLD_FOUNDRY_GAME_ID, wagerPoints: request.wagerPoints, useFreeSpin: false }
      const response = demo
        ? await fetch('/api/slots/demo/spins', {
            method: 'POST', credentials: 'omit', headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ ...common, freeSpinsRemaining: 0, freeSpinWagerPoints: null,
              energyBalance: request.state.energyBalance, sealCollections: [], freeSpinFeatureMode: null,
              crucibleWageredCents: request.state.crucibleWageredCents }),
          })
        : await authenticated('/api/slots/spins', {
            method: 'POST', body: JSON.stringify({ ...common, useSpecialBoost: false }),
          })
      const value = await json(response)
      if (!isGoldFoundrySpinResult(value)) throw new Error('The Gold Foundry server returned an invalid reel result.')
      return value
    },
  }
}
