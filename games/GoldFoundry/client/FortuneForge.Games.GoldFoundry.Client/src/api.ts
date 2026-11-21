import { createPreparedSlotSpinStore, isPreparedSlotSpin, preparedSlotSpinHeaders, preparedSlotSpinPath, type PreparedSlotSpin } from '@fortuneforge/slot-client-core/preparedSpin'
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

type Problem = { error?: string; detail?: string; code?: string; available?: number; required?: number; retryAfterMilliseconds?: number; pendingOperation?: unknown; preparedOperationPending?: boolean }
const preparedSpins = createPreparedSlotSpinStore('gold-foundry')

export class GoldFoundryRequestError extends Error {
  readonly status: number
  readonly code?: string
  readonly available?: number
  readonly required?: number
  readonly retryAfterMilliseconds?: number
  readonly problem: Problem | null
  constructor(message: string, status: number, problem: Problem | null) {
    super(message)
    this.name = 'GoldFoundryRequestError'
    this.status = status
    this.code = problem?.code
    this.available = problem?.available
    this.required = problem?.required
    this.retryAfterMilliseconds = problem?.retryAfterMilliseconds
    this.problem = problem
  }
}

const record = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === 'object' && !Array.isArray(value)
const natural = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value >= 0
const finite = (value: unknown): value is number => typeof value === 'number' && Number.isFinite(value)
const symbol = (value: unknown) => typeof value === 'string' && (goldFoundrySymbolIds as readonly string[]).includes(value)
const optionalNatural = (value: unknown) => value === undefined || natural(value)
const validWager = (value: unknown): value is number => natural(value) && value >= 2 && value <= 2_000 && value % 2 === 0
const savedSpin = (value: unknown): value is PreparedSlotSpin =>
  isPreparedSlotSpin(value) && !value.useFreeSpin && !value.useSpecialBoost && validWager(value.wagerPoints)

function validState(value: unknown): value is Record<string, unknown> {
  return record(value) && natural(value.freeSpinsRemaining) && value.freeSpinsRemaining === 0 &&
    value.freeSpinWagerPoints === null && value.specialPointsBalance === 0 && natural(value.energyBalance) &&
    Array.isArray(value.sealCollections) && value.sealCollections.length === 0 && value.freeSpinFeatureMode === null &&
    optionalNatural(value.crucibleWageredCents) && optionalNatural(value.cruciblePourValueCents) &&
    (value.pendingOperation == null || savedSpin(value.pendingOperation)) &&
    (value.slotsCreditsBalance == null || finite(value.slotsCreditsBalance) && value.slotsCreditsBalance >= 0)
}

function normalizeState(value: Record<string, unknown>): GoldFoundryState {
  return {
    freeSpinsRemaining: 0, freeSpinWagerPoints: null,
    specialPointsBalance: value.specialPointsBalance as number,
    energyBalance: value.energyBalance as number,
    sealCollections: [], freeSpinFeatureMode: null,
    crucibleWageredCents: (value.crucibleWageredCents as number | undefined) ?? 0,
    cruciblePourValueCents: (value.cruciblePourValueCents as number | undefined) ?? 0,
    ...(savedSpin(value.pendingOperation) && { pendingOperation: value.pendingOperation }),
    ...(finite(value.slotsCreditsBalance) && { slotsCreditsBalance: value.slotsCreditsBalance }),
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

export function createGoldFoundryApi(authenticatedFetch?: GoldFoundryAuthenticatedFetch, accountId?: string) {
  let preparedEnabled = false
  const authenticated = (path: string, init?: RequestInit) => {
    if (!authenticatedFetch) throw new Error('Gold Foundry requires an authenticated session for paid play.')
    return authenticatedFetch(path, init)
  }
  const pendingSpin = () => accountId ? preparedSpins.read(accountId) : undefined
  async function state(): Promise<GoldFoundryState> {
    const response = await authenticated(`/api/slots/state?gameId=${GOLD_FOUNDRY_GAME_ID}`, { method: 'GET', headers: preparedSlotSpinHeaders(accountId) })
    const value = await json(response)
    if (!validState(value)) throw new Error('The Gold Foundry server returned an invalid crucible state.')
    preparedEnabled = response.headers.get('X-FortuneForge-Prepared-Spin') === '1'
    if (preparedEnabled && accountId && savedSpin(value.pendingOperation) && !pendingSpin())
      preparedSpins.remember(accountId, value.pendingOperation)
    return normalizeState(value)
  }
  async function submit(wagerPoints: number, operation?: PreparedSlotSpin, deferAcknowledgement = false) {
    if (operation && !preparedEnabled) throw new Error('The saved spin is waiting for the Gold Foundry server.')
    try {
      const response = await authenticated(operation ? preparedSlotSpinPath : '/api/slots/spins', {
        method: 'POST',
        headers: preparedSlotSpinHeaders(accountId, operation?.operationId),
        body: JSON.stringify({ gameId: GOLD_FOUNDRY_GAME_ID, wagerPoints, useFreeSpin: false, useSpecialBoost: false }),
      })
      const value = await json(response)
      if (!isGoldFoundrySpinResult(value) || value.wagerPoints !== wagerPoints)
        throw new Error('The Gold Foundry server returned an invalid reel result.')
      if (operation && accountId && !deferAcknowledgement) preparedSpins.forget(accountId, operation.operationId)
      return value
    } catch (error) {
      if (operation && accountId && error instanceof GoldFoundryRequestError) {
        const replacement = error.problem?.pendingOperation
        if (savedSpin(replacement)) preparedSpins.remember(accountId, replacement)
        else if (error.status === 400 || error.code === 'slot-prepared-start-paused' ||
          (error.status === 409 && error.problem?.preparedOperationPending !== true)) preparedSpins.forget(accountId, operation.operationId)
      }
      throw error
    }
  }
  return {
    async availability(signal: AbortSignal) {
      const response = await fetch(`/api/slots/demo/status?gameId=${GOLD_FOUNDRY_GAME_ID}`, {
        credentials: 'omit', cache: 'no-store', signal,
      })
      if (response.status !== 204) throw new Error('The Gold Foundry game service is offline.')
    },
    state,
    pendingSpin,
    async resumeSpin() {
      const operation = pendingSpin()
      if (!operation) return undefined
      const result = await submit(operation.wagerPoints, operation, true)
      const latest = await state()
      if (accountId) {
        preparedSpins.forget(accountId, operation.operationId)
        if (latest.pendingOperation) preparedSpins.remember(accountId, latest.pendingOperation)
      }
      return { result, state: latest }
    },
    async spin(request: GoldFoundrySpinRequest, demo: boolean): Promise<GoldFoundrySpinResult> {
      if (!validWager(request.wagerPoints))
        throw new Error('Gold Foundry received an invalid wager for this round.')
      if (!demo) {
        const saved = pendingSpin()
        if (saved) return submit(saved.wagerPoints, saved)
        const operation = preparedEnabled && accountId ? { operationId: crypto.randomUUID(), wagerPoints: request.wagerPoints,
          useFreeSpin: false, useSpecialBoost: false } : undefined
        if (operation && accountId) preparedSpins.remember(accountId, operation)
        return submit(request.wagerPoints, operation)
      }
      const common = { gameId: GOLD_FOUNDRY_GAME_ID, wagerPoints: request.wagerPoints, useFreeSpin: false }
      const response = await fetch('/api/slots/demo/spins', {
            method: 'POST', credentials: 'omit', headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ ...common, freeSpinsRemaining: 0, freeSpinWagerPoints: null,
              energyBalance: request.state.energyBalance, sealCollections: [], freeSpinFeatureMode: null,
              crucibleWageredCents: request.state.crucibleWageredCents }),
          })
      const value = await json(response)
      if (!isGoldFoundrySpinResult(value)) throw new Error('The Gold Foundry server returned an invalid reel result.')
      return value
    },
  }
}
