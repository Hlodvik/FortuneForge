import { isSlotSymbolId, type SlotSealCollection, type SpinResult } from '../types/slots'
import { fetchWithAccountSession } from '../../landing/services/accountsApi'

type SpinRequest = {
  gameId: string
  wagerPoints: number
  useFreeSpin: boolean
  useSpecialBoost: boolean
}

type DemoSpinRequest = Omit<SpinRequest, 'useSpecialBoost'> & {
  freeSpinsRemaining: number
  freeSpinWagerPoints: number | null
  energyBalance: number
}

export type SlotState = {
  freeSpinsRemaining: number
  freeSpinWagerPoints: number | null
  specialPointsBalance: number
  energyBalance: number
  sealCollections: SlotSealCollection[]
  freeSpinFeatureMode: string | null
}

type SpinProblem = {
  error?: string
  detail?: string
  code?: string
  available?: number
  required?: number
  freeSpinsRemaining?: number
  retryAfterMilliseconds?: number
}

export class SpinRequestError extends Error {
  readonly status: number
  readonly code?: string
  readonly available?: number
  readonly required?: number
  readonly freeSpinsRemaining?: number
  readonly retryAfterMilliseconds?: number

  constructor(message: string, status: number, problem: SpinProblem | null) {
    super(message)
    this.name = 'SpinRequestError'
    this.status = status
    this.code = problem?.code
    this.available = problem?.available
    this.required = problem?.required
    this.freeSpinsRemaining = problem?.freeSpinsRemaining
    this.retryAfterMilliseconds = problem?.retryAfterMilliseconds
  }
}

export async function requestSpin(request: SpinRequest): Promise<SpinResult> {
  const response = await fetchWithAccountSession('/api/slots/spins', {
    method: 'POST',
    body: JSON.stringify(request),
  })

  if (!response.ok) {
    const problem = (await response.json().catch(() => null)) as SpinProblem | null
    throw new SpinRequestError(
      problem?.error ?? problem?.detail ?? `Spin request failed with status ${response.status}.`,
      response.status,
      problem,
    )
  }

  const result = (await response.json()) as unknown
  if (!isSpinResult(result)) {
    throw new Error('The spin server returned an invalid reel result.')
  }

  return result
}

export async function requestDemoSpin(request: DemoSpinRequest): Promise<SpinResult> {
  const response = await fetch('/api/slots/demo/spins', {
    method: 'POST',
    credentials: 'omit',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(request),
  })

  if (!response.ok) {
    const problem = (await response.json().catch(() => null)) as SpinProblem | null
    throw new SpinRequestError(
      problem?.error ?? problem?.detail ?? `Demo spin request failed with status ${response.status}.`,
      response.status,
      problem,
    )
  }

  const result = (await response.json()) as unknown
  if (!isSpinResult(result)) {
    throw new Error('The demo spin server returned an invalid reel result.')
  }

  return result
}

export async function requestSlotState(gameId: string): Promise<SlotState> {
  const response = await fetchWithAccountSession(
    `/api/slots/state?gameId=${encodeURIComponent(gameId)}`,
    { method: 'GET' },
  )
  if (!response.ok) {
    throw new Error(`Slot state request failed with status ${response.status}.`)
  }

  const state = (await response.json()) as unknown
  if (
    !isRecord(state) ||
    typeof state.freeSpinsRemaining !== 'number' ||
    !Number.isInteger(state.freeSpinsRemaining) ||
    state.freeSpinsRemaining < 0 ||
    (state.freeSpinWagerPoints !== null && typeof state.freeSpinWagerPoints !== 'number')
    || typeof state.specialPointsBalance !== 'number'
    || !Number.isInteger(state.specialPointsBalance)
    || state.specialPointsBalance < 0
    || typeof state.energyBalance !== 'number'
    || !Number.isInteger(state.energyBalance)
    || state.energyBalance < 0
    || !Array.isArray(state.sealCollections)
    || !state.sealCollections.every(isSlotSealCollection)
    || (state.freeSpinFeatureMode !== null && typeof state.freeSpinFeatureMode !== 'string')
  ) {
    throw new Error('The slot server returned an invalid bonus state.')
  }

  return state as SlotState
}

function isSpinResult(result: unknown): result is SpinResult {
  if (!isRecord(result) || !isRecord(result.payout)) {
    return false
  }

  return (
    typeof result.spinId === 'string' &&
    typeof result.gameId === 'string' &&
    typeof result.reelSetId === 'string' &&
    typeof result.symbolSetId === 'string' &&
    typeof result.paytableId === 'string' &&
    typeof result.wagerPoints === 'number' &&
    typeof result.pointValueInCents === 'number' &&
    typeof result.consecutiveFiveMisses === 'number' &&
    Number.isInteger(result.consecutiveFiveMisses) &&
    result.consecutiveFiveMisses >= 0 &&
    typeof result.fiveMatchPityTriggered === 'boolean' &&
    typeof result.isFreeSpin === 'boolean' &&
    typeof result.freeSpinsAwarded === 'number' &&
    Number.isInteger(result.freeSpinsAwarded) &&
    result.freeSpinsAwarded >= 0 &&
    typeof result.freeSpinsRemaining === 'number' &&
    Number.isInteger(result.freeSpinsRemaining) &&
    result.freeSpinsRemaining >= 0 &&
    (result.freeSpinWagerPoints === null ||
      (typeof result.freeSpinWagerPoints === 'number' &&
        Number.isInteger(result.freeSpinWagerPoints) &&
        result.freeSpinWagerPoints >= 0)) &&
    typeof result.specialPointsAwarded === 'number' &&
    Number.isInteger(result.specialPointsAwarded) &&
    result.specialPointsAwarded >= 0 &&
    typeof result.specialPointsBalance === 'number' &&
    Number.isInteger(result.specialPointsBalance) &&
    result.specialPointsBalance >= 0 &&
    typeof result.energyAwarded === 'number' &&
    Number.isInteger(result.energyAwarded) &&
    result.energyAwarded >= 0 &&
    typeof result.energyBalance === 'number' &&
    Number.isInteger(result.energyBalance) &&
    result.energyBalance >= 0 &&
    typeof result.energyMultiplierApplied === 'boolean' &&
    typeof result.payoutMultiplier === 'number' &&
    result.payoutMultiplier >= 1 &&
    typeof result.monkeyPawCount === 'number' &&
    Number.isInteger(result.monkeyPawCount) &&
    result.monkeyPawCount >= 0 &&
    typeof result.moneyGrabPoints === 'number' &&
    result.moneyGrabPoints >= 0 &&
    typeof result.bananaBonusPoints === 'number' &&
    result.bananaBonusPoints >= 0 &&
    isRecord(result.sealsAwarded) &&
    Object.values(result.sealsAwarded).every((value) =>
      typeof value === 'number' && Number.isInteger(value) && value >= 0,
    ) &&
    Array.isArray(result.sealCollections) &&
    result.sealCollections.every(isSlotSealCollection) &&
    (result.freeSpinFeatureMode === null || typeof result.freeSpinFeatureMode === 'string') &&
    typeof result.specialBoostApplied === 'boolean' &&
    (result.slotsCreditsBalance === null || typeof result.slotsCreditsBalance === 'number') &&
    Array.isArray(result.reels) &&
    result.reels.length > 0 &&
    result.reels.every(
      (reel) => Array.isArray(reel) && reel.length > 0 && reel.every(isSlotSymbolId),
    ) &&
    Array.isArray(result.reelStops) &&
    result.reelStops.length === result.reels.length &&
    result.reelStops.every((stop) => Number.isInteger(stop)) &&
    typeof result.payout.totalPoints === 'number' &&
    Array.isArray(result.payout.paylines) &&
    result.payout.paylines.every(isPaylinePayout)
  )
}

function isSlotSealCollection(value: unknown): value is SlotSealCollection {
  return (
    isRecord(value) &&
    typeof value.sealId === 'string' &&
    typeof value.count === 'number' &&
    Number.isInteger(value.count) &&
    value.count >= 0 &&
    typeof value.averageWagerPoints === 'number' &&
    Number.isInteger(value.averageWagerPoints) &&
    value.averageWagerPoints >= 0 &&
    typeof value.requiredCount === 'number' &&
    Number.isInteger(value.requiredCount) &&
    value.requiredCount > 0
  )
}

function isPaylinePayout(value: unknown): boolean {
  return (
    isRecord(value) &&
    Number.isInteger(value.paylineId) &&
    typeof value.amountPoints === 'number' &&
    Array.isArray(value.matches) &&
    value.matches.every(isPaidMatch)
  )
}

function isPaidMatch(value: unknown): boolean {
  return (
    isRecord(value) &&
    typeof value.multiplier === 'number' &&
    typeof value.amountPoints === 'number' &&
    isSymbolMatch(value.match)
  )
}

function isSymbolMatch(value: unknown): boolean {
  return (
    isRecord(value) &&
    Number.isInteger(value.paylineId) &&
    isSlotSymbolId(value.symbolId) &&
    Number.isInteger(value.matchLength) &&
    Array.isArray(value.positions) &&
    value.positions.every(isGridPosition) &&
    Array.isArray(value.wildPositions) &&
    value.wildPositions.every(isGridPosition)
  )
}

function isGridPosition(value: unknown): boolean {
  return isRecord(value) && Number.isInteger(value.reel) && Number.isInteger(value.row)
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}
