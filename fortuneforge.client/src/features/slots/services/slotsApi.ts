// Edited: 2026-11-18T19:58:40-06:00 | Computer: Vinnabok
// Edited: 2026-11-18T18:00:18-06:00 | Computer: Vinnabok
// Edited: 2026-11-18T16:27:57-06:00 | Computer: Vinnabok
import {
  arcaneWandPaylineId,
  isSlotSymbolId,
  type SlotSealCollection,
  type SpinResult,
} from '../types/slots'
import type {
  SlotPaidModeState,
  SlotPotionBottle,
  SlotPotionPreview,
  SlotPotionState,
  SlotTomeState,
  SlotTumbleRules,
} from '@fortuneforge/slot-game-runtime/experience'
import type { SlotSymbolSet } from '../config/symbolSets'
import { fetchWithAccountSession } from '../../account/services/accountsApi'
import { createPreparedSlotSpinStore, isPreparedSlotSpin, preparedSlotSpinHeaders, preparedSlotSpinPath, type PreparedSlotSpin } from '@fortuneforge/slot-client-core/preparedSpin'

type SpinRequest = {
  gameId: string
  wagerPoints: number
  useFreeSpin: boolean
  useSpecialBoost: boolean
}

const paidSpinPath = '/api/slots/spins'
const preparedRoutes = new Map([
  ['arcane-archives-v1', { path: '/api/slots/arcane/prepared-spins', store: createPreparedSlotSpinStore('arcane'), enabled: false }],
  ['wukong-journey-v1', { path: preparedSlotSpinPath, store: createPreparedSlotSpinStore('wukong'), enabled: false }],
  ['rainbow-realm-fruits-v1', { path: preparedSlotSpinPath, store: createPreparedSlotSpinStore('rainbow-realm'), enabled: false }],
  ['pirates-fortune-v1', { path: preparedSlotSpinPath, store: createPreparedSlotSpinStore('pirates-fortune'), enabled: false }],
  ['gods-of-olympus-v1', { path: preparedSlotSpinPath, store: createPreparedSlotSpinStore('gods-of-olympus'), enabled: false }],
  ['reel-riches-v1', { path: preparedSlotSpinPath, store: createPreparedSlotSpinStore('reel-riches'), enabled: false }],
  ['high-noon-fortune-v1', { path: preparedSlotSpinPath, store: createPreparedSlotSpinStore('high-noon-fortune'), enabled: false }],
  ['royal-draw-v1', { path: preparedSlotSpinPath, store: createPreparedSlotSpinStore('royal-draw'), enabled: false }],
  ['cosmic-fortune-v1', { path: preparedSlotSpinPath, store: createPreparedSlotSpinStore('cosmic-fortune'), enabled: false }],
  ['ocean-odyssey-v1', { path: preparedSlotSpinPath, store: createPreparedSlotSpinStore('ocean-odyssey'), enabled: false }],
  ['samurai-fortune-v1', { path: preparedSlotSpinPath, store: createPreparedSlotSpinStore('samurai-fortune'), enabled: false }],
  ['candy-carnival-v1', { path: preparedSlotSpinPath, store: createPreparedSlotSpinStore('candy-carnival'), enabled: false }],
  ['phantom-manor-v1', { path: preparedSlotSpinPath, store: createPreparedSlotSpinStore('phantom-manor'), enabled: false }],
  ['nordic-legends-v1', { path: preparedSlotSpinPath, store: createPreparedSlotSpinStore('nordic-legends'), enabled: false }],
  ['desert-treasures-v1', { path: preparedSlotSpinPath, store: createPreparedSlotSpinStore('desert-treasures'), enabled: false }],
  ['robot-revolution-v1', { path: preparedSlotSpinPath, store: createPreparedSlotSpinStore('robot-revolution'), enabled: false }],
])

export function readPendingSlotSpin(gameId: string, accountId?: string): PreparedSlotSpin | undefined {
  return accountId ? preparedRoutes.get(gameId)?.store.read(accountId) : undefined
}

type DemoSpinRequest = Omit<SpinRequest, 'useSpecialBoost'> & {
  paidMode?: SlotPaidModeState | null
  freeSpinsRemaining: number
  freeSpinWagerPoints: number | null
  energyBalance: number
  sealCollections: SlotSealCollection[]
  freeSpinFeatureMode: string | null
  olympusTumbleShowcase?: boolean
}

export type SlotState = {
  paidMode?: SlotPaidModeState | null
  freeSpinsRemaining: number
  freeSpinWagerPoints: number | null
  specialPointsBalance: number
  energyBalance: number
  sealCollections: SlotSealCollection[]
  freeSpinFeatureMode: string | null
  slotsCreditsBalance?: number | null
  arcaneTome?: SlotTomeState | null
  arcanePotions?: SlotPotionState | null
}

type SpinProblem = {
  error?: string
  detail?: string
  code?: string
  available?: number
  required?: number
  freeSpinsRemaining?: number
  retryAfterMilliseconds?: number
  pendingOperation?: unknown
  preparedOperationPending?: boolean
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

export async function requestDemoAvailability(gameId: string, signal?: AbortSignal): Promise<void> {
  const response = await fetch(
    `/api/slots/demo/status?gameId=${encodeURIComponent(gameId)}`,
    {
      method: 'GET',
      credentials: 'omit',
      cache: 'no-store',
      signal,
    },
  )

  if (response.status !== 204) {
    throw new Error(`Game service availability check failed with status ${response.status}.`)
  }
}

export async function requestSpin(
  request: SpinRequest,
  tumbleRules?: SlotTumbleRules,
  symbolDefinitions?: SlotSymbolSet['definitions'],
  accountId?: string,
): Promise<SpinResult> {
  const route = preparedRoutes.get(request.gameId)
  let prepared = readPendingSlotSpin(request.gameId, accountId)
  if (prepared && route?.path === preparedSlotSpinPath && !route.enabled) {
    throw new SpinRequestError('The saved spin is waiting for the game server.', 503, null)
  }
  if (route && accountId && !prepared && route.enabled) {
    const { wagerPoints, useFreeSpin, useSpecialBoost } = request
    prepared = { operationId: crypto.randomUUID(), wagerPoints, useFreeSpin, useSpecialBoost }
    route.store.remember(accountId, prepared)
  }
  let response = route && prepared && accountId
    ? await submitPreparedSpin(route.path, request.gameId, prepared, accountId)
    : await fetchWithAccountSession(paidSpinPath, {
      method: 'POST', headers: preparedSlotSpinHeaders(accountId), body: JSON.stringify(request),
    })
  let problem = response.ok ? null : await response.json().catch(() => null) as SpinProblem | null

  // A different tab or a previous page may already have a prepared round. The
  // server returns its original terms only to the authenticated player.
  if (route && accountId && response.status === 409 && isPreparedSlotSpin(problem?.pendingOperation) &&
      problem.pendingOperation.operationId !== prepared?.operationId) {
    prepared = problem.pendingOperation
    route.store.remember(accountId, prepared)
    if (route.path === preparedSlotSpinPath) {
      throw new SpinRequestError(problem.error ?? 'Resume the saved spin first.', response.status, problem)
    }
    response = await submitPreparedSpin(route.path, request.gameId, prepared, accountId)
    problem = response.ok ? null : await response.json().catch(() => null) as SpinProblem | null
  }

  if (!response.ok) {
    if (route && accountId && prepared && (response.status === 400 || problem?.code === 'slot-prepared-start-paused' ||
        response.status === 409 && !problem?.preparedOperationPending && !isPreparedSlotSpin(problem?.pendingOperation)))
      route.store.forget(accountId, prepared.operationId)
    throw new SpinRequestError(
      problem?.error ?? problem?.detail ?? `Spin request failed with status ${response.status}.`,
      response.status,
      problem,
    )
  }

  const result = (await response.json()) as unknown
  if (!isSpinResult(result, symbolDefinitions, tumbleRules)) {
    throw new Error('The spin server returned an invalid reel result.')
  }
  if (result.gameId !== request.gameId || prepared &&
      (result.wagerPoints !== prepared.wagerPoints || result.isFreeSpin !== prepared.useFreeSpin)) {
    throw new Error('The spin result does not match the requested round.')
  }

  const settledTerms = prepared ?? request
  if (tumbleRules && !settledTerms.useFreeSpin) {
    validateGodsTumbleResult(result, { gameId: request.gameId, wagerPoints: settledTerms.wagerPoints }, false, tumbleRules)
  }
  if (route && accountId && prepared) route.store.forget(accountId, prepared.operationId)

  return result
}

function submitPreparedSpin(path: string, gameId: string, spin: PreparedSlotSpin, accountId: string): Promise<Response> {
  const { operationId, ...terms } = spin
  return fetchWithAccountSession(path, {
    method: 'POST',
    headers: preparedSlotSpinHeaders(accountId, operationId),
    body: JSON.stringify({ gameId, ...terms }),
  })
}


export async function requestDemoSpin(
  request: DemoSpinRequest,
  tumbleRules?: SlotTumbleRules,
  symbolDefinitions?: SlotSymbolSet['definitions'],
): Promise<SpinResult> {
  const response = await fetch('/api/slots/demo/spins', {
    method: 'POST',
    credentials: 'omit',
    headers: { 'Content-Type': 'application/json' },
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
  if (!isSpinResult(result, symbolDefinitions, tumbleRules)) {
    throw new Error('The spin server returned an invalid reel result.')
  }

  if (request.olympusTumbleShowcase !== undefined) {
    if (!tumbleRules) throw new Error('The tumble spin is missing its game-owned rules.')
    validateGodsTumbleResult(result, request, true, tumbleRules)
  }

  return result
}

export async function requestSlotState(gameId: string, accountId?: string): Promise<SlotState> {
  const response = await fetchWithAccountSession(
    `/api/slots/state?gameId=${encodeURIComponent(gameId)}`,
    { method: 'GET', headers: preparedSlotSpinHeaders(accountId) },
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
    || (state.arcaneTome != null && !isTomeState(state.arcaneTome))
    || (state.arcanePotions != null && !isPotionState(state.arcanePotions))
    || (state.paidMode != null && !isPaidModeState(state.paidMode))
    || (state.freeSpinFeatureMode !== null && typeof state.freeSpinFeatureMode !== 'string')
    || (state.slotsCreditsBalance != null && (typeof state.slotsCreditsBalance !== 'number' ||
      !Number.isFinite(state.slotsCreditsBalance) || state.slotsCreditsBalance < 0))
    || (state.pendingOperation != null && !isPreparedSlotSpin(state.pendingOperation))
  ) {
    throw new Error('The slot server returned an invalid bonus state.')
  }

  const route = preparedRoutes.get(gameId)
  if (route) {
    route.enabled = response.headers?.get('X-FortuneForge-Prepared-Spin') === '1'
    if (route.enabled && accountId && isPreparedSlotSpin(state.pendingOperation) && !route.store.read(accountId))
      route.store.remember(accountId, state.pendingOperation)
  }

  return state as SlotState
}

export async function requestArcaneTomePage(page: string, activate: boolean): Promise<SlotTomeState> {
  const response = await fetchWithAccountSession('/api/slots/arcane/tome', {
    method: 'POST',
    body: JSON.stringify({ page, activate }),
  })
  if (!response.ok) {
    const problem = (await response.json().catch(() => null)) as SpinProblem | null
    throw new Error(problem?.detail ?? problem?.error ?? 'The Tome Page could not be updated.')
  }
  const state: unknown = await response.json()
  if (!isTomeState(state)) throw new Error('The spin server returned invalid Tome state.')
  return state
}

export async function requestArcanePotion(potionId: number): Promise<SlotPotionState> {
  const response = await fetchWithAccountSession('/api/slots/arcane/potions/activate', {
    method: 'POST',
    body: JSON.stringify({ potionId }),
  })
  if (!response.ok) {
    const problem = (await response.json().catch(() => null)) as SpinProblem | null
    throw new Error(problem?.detail ?? problem?.error ?? 'The potion could not be used.')
  }
  const state: unknown = await response.json()
  if (!isPotionState(state)) throw new Error('The spin server returned invalid potion state.')
  return state
}

function isPotionPreview(value: unknown): value is SlotPotionPreview {
  return isRecord(value) &&
    typeof value.ingredientId === 'string' && typeof value.name === 'string' &&
    typeof value.effect === 'string' && isNonNegativeInteger(value.potency)
}

function isPaidModeState(value: unknown): value is SlotPaidModeState {
  return isRecord(value) && typeof value.mode === 'string' && value.mode.length > 0 &&
    isPositiveInteger(value.remaining) && isPositiveInteger(value.wagerPoints)
}

function isPotionBottle(value: unknown): value is SlotPotionBottle {
  return isRecord(value) && isPositiveInteger(value.id) &&
    (value.variantId === undefined || (typeof value.variantId === 'string' && value.variantId.length > 0)) &&
    (value.lockedWagerPoints === undefined || isPositiveInteger(value.lockedWagerPoints)) && isPotionPreview(value)
}

function isPotionState(value: unknown): value is SlotPotionState {
  return isRecord(value) && isPositiveInteger(value.capacity) && isPositiveInteger(value.majority) &&
    value.majority <= value.capacity && Array.isArray(value.pot) && value.pot.length < value.capacity &&
    value.pot.every(ingredient => typeof ingredient === 'string') &&
    Array.isArray(value.bottles) && value.bottles.every(isPotionBottle) &&
    (value.preview === null || isPotionPreview(value.preview)) &&
    (value.active === null || (isRecord(value.active) && isPotionBottle(value.active.bottle) &&
      isPositiveInteger(value.active.spinsRemaining)))
}

function isIngredientCounts(value: unknown): value is Readonly<Record<string, number>> {
  return isRecord(value) && Object.values(value).every(isNonNegativeInteger)
}

function isTomeState(value: unknown): value is SlotTomeState {
  return isRecord(value) &&
    typeof value.selectedPage === 'string' &&
    (value.activePage === null || typeof value.activePage === 'string') &&
    isNonNegativeInteger(value.boostedSpinsRemaining) && value.boostedSpinsRemaining <= 5 &&
    typeof value.grimoireReady === 'boolean' &&
    isIngredientCounts(value.ingredients) &&
    Array.isArray(value.pages) && value.pages.length > 0 &&
    value.pages.every(page => isRecord(page) && typeof page.id === 'string' &&
      typeof page.label === 'string' && Array.isArray(page.ingredients) &&
      page.ingredients.length === 2 && page.ingredients.every(id => typeof id === 'string')) &&
    value.pages.some(page => page.id === value.selectedPage) &&
    (value.activePage === null ? value.boostedSpinsRemaining === 0
      : value.boostedSpinsRemaining > 0 && value.pages.some(page => page.id === value.activePage))
}

function isSpinResult(
  result: unknown,
  symbolDefinitions?: SlotSymbolSet['definitions'],
  tumbleRules?: SlotTumbleRules,
): result is SpinResult {
  if (!isRecord(result) || !isRecord(result.payout)) {
    return false
  }

  return (
    typeof result.spinId === 'string' &&
    (result.paidMode == null || isPaidModeState(result.paidMode)) &&
    typeof result.gameId === 'string' &&
    typeof result.reelSetId === 'string' &&
    typeof result.symbolSetId === 'string' &&
    typeof result.paytableId === 'string' &&
    typeof result.wagerPoints === 'number' &&
    typeof result.pointValueInCents === 'number' &&
    (result.chargedWagerCents === undefined ||
      (typeof result.chargedWagerCents === 'number' &&
        Number.isInteger(result.chargedWagerCents) && result.chargedWagerCents >= 0)) &&
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
      (reel) => Array.isArray(reel) && reel.length > 0 && reel.every(symbol =>
        isKnownResultSymbol(symbol, symbolDefinitions) || (!symbolDefinitions &&
          result.olympusTumble != null && tumbleRules?.symbols.includes(symbol) === true)),
    ) &&
    Array.isArray(result.reelStops) &&
    result.reelStops.length === result.reels.length &&
    result.reelStops.every((stop) => Number.isInteger(stop)) &&
    (result.arcaneTome == null || isTomeState(result.arcaneTome)) &&
    (result.arcanePotions == null || isPotionState(result.arcanePotions)) &&
    (result.arcaneIngredientsAwarded == null || isIngredientCounts(result.arcaneIngredientsAwarded)) &&
    typeof result.payout.totalPoints === 'number' &&
    (result.lightningCollection == null || (result.gameId === 'arcane-archives-v1' &&
      validLightningCollection(result.lightningCollection, result.reels, result.payout.totalPoints))) &&
    Array.isArray(result.payout.paylines) &&
    result.payout.paylines.every(line => isPaylinePayout(line, symbolDefinitions)) &&
    (result.arcaneWandCollection == null || (result.gameId === 'arcane-archives-v1' &&
      validArcaneWandCollection(result.arcaneWandCollection, result.reels, result.wagerPoints,
        result.payout.totalPoints, result.payout.paylines)))
  )
}

function validArcaneWandCollection(
  value: unknown,
  reels: unknown[][],
  wagerPoints: number,
  totalPayout: number,
  paylines: unknown[],
): boolean {
  if (!isRecord(value) || !isRecord(value.wand) || !isPositiveInteger(value.totalPoints) ||
      !Array.isArray(value.cells) || value.cells.length === 0) return false
  const { reel, row } = value.wand
  if (!isNonNegativeInteger(reel) || !isNonNegativeInteger(row) || reels[reel]?.[row] !== 'WAND') return false

  const positions = new Set<string>()
  let collected = 0
  for (const cell of value.cells) {
    if (!isRecord(cell) || !isNonNegativeInteger(cell.reel) || !isNonNegativeInteger(cell.row) ||
        !isPositiveInteger(cell.multiplier) || !isPositiveInteger(cell.amountPoints) ||
        (cell.potionBonusPoints != null && !isNonNegativeInteger(cell.potionBonusPoints)) ||
        cell.amountPoints !== wagerPoints * cell.multiplier + (cell.potionBonusPoints ?? 0)) return false
    const symbol = reels[cell.reel]?.[cell.row]
    const key = [cell.reel, cell.row].join(':')
    if ((symbol !== 'POWER' && symbol !== 'FORTUNE_RUNE' && symbol !== 'BOLT') ||
        symbol !== cell.symbolId || positions.has(key)) return false
    positions.add(key)
    collected += cell.amountPoints
  }
  const wandLine = paylines.find(line => isRecord(line) && line.paylineId === arcaneWandPaylineId)
  return collected === value.totalPoints && collected <= totalPayout &&
    isRecord(wandLine) && wandLine.amountPoints === collected
}

function validLightningCollection(value: unknown, reels: unknown[][], totalPayout: number): boolean {
  if (!isRecord(value) || !isRecord(value.collector)) return false
  const { collector, cells, totalPoints } = value
  if (!isNonNegativeInteger(collector.reel) || !isNonNegativeInteger(collector.row) ||
    reels[collector.reel]?.[collector.row] !== 'BOLT' || !Array.isArray(cells) || cells.length === 0 ||
    !isNonNegativeInteger(totalPoints) || totalPoints > totalPayout) return false

  const positions = new Set<string>()
  let collected = 0
  for (const cell of cells) {
    if (!isRecord(cell) || !isNonNegativeInteger(cell.reel) || !isNonNegativeInteger(cell.row) ||
      !isNonNegativeInteger(cell.amountPoints) || cell.amountPoints === 0) return false
    const symbol = reels[cell.reel]?.[cell.row]
    const key = `${cell.reel}:${cell.row}`
    if ((symbol !== '7' && symbol !== 'POWER' && symbol !== 'SEAL_RAND') || positions.has(key)) return false
    positions.add(key)
    collected += cell.amountPoints
  }
  return collected === totalPoints
}

function validateGodsTumbleResult(
  result: SpinResult,
  request: Pick<SpinRequest, 'gameId' | 'wagerPoints'>,
  isDemo: boolean,
  rules: SlotTumbleRules,
) {
  const balance = result.slotsCreditsBalance
  if (result.gameId !== request.gameId || result.wagerPoints !== request.wagerPoints ||
    (isDemo ? balance !== null : balance === null || !Number.isFinite(balance) || balance < 0) ||
    !validGodsTumbleResult(result, rules)
  ) {
    throw new Error('The spin server returned an invalid Olympus tumble result.')
  }
}

function validGodsTumbleResult(result: SpinResult, rules: SlotTumbleRules): boolean {
  const tumble: unknown = result.olympusTumble
  if (!isRecord(tumble)) return false
  const { initialReels, steps, totalPoints, baseTotalPoints, multiplier } = tumble
  if (!validGodsTumbleReels(initialReels, rules) ||
    !Array.isArray(steps) || steps.length > 12 ||
    !isNonNegativeInteger(totalPoints) || totalPoints !== result.payout.totalPoints ||
    result.payout.paylines.length !== 0 ||
    result.isFreeSpin || result.freeSpinsAwarded !== 0 || result.freeSpinsRemaining !== 0 ||
    !validGodsTumbleReels(result.reels, rules) ||
    !result.reels.every((reel, column) => reel.every((symbol, row) => symbol === initialReels[column][row]))
  ) return false

  let reels = initialReels
  let total = 0
  let landedMultiplier = initialReels.flat().reduce((sum, symbol) =>
    sum + (rules.multiplierValues[symbol] ?? 0), 0)
  for (const step of steps) {
    if (!isRecord(step) || !validGodsTumbleReels(step.reels, rules) || !isNonNegativeInteger(step.amountPoints) ||
      !Array.isArray(step.awards) || step.awards.length === 0 || step.awards.length > 3 ||
      !Array.isArray(step.cleared) || step.cleared.length > rules.columns * rules.rows ||
      !Array.isArray(step.dropRows) || step.dropRows.length !== rules.columns ||
      !step.dropRows.every(reel => Array.isArray(reel) && reel.length === rules.rows &&
        reel.every(rows => isNonNegativeInteger(rows) && rows <= rules.rows))
    ) return false

    const awardedCells = new Set<number>()
    let stepPoints = 0
    for (const award of step.awards) {
      if (!isRecord(award) || !isNonNegativeInteger(award.amountPoints) ||
        !Array.isArray(award.positions) || award.positions.length < 8 ||
        award.positions.length > rules.columns * rules.rows
      ) return false
      for (const position of award.positions) {
        if (!isGodsTumblePosition(position, rules) || reels[position.reel][position.row] !== award.symbol) return false
        const key = godsTumbleCellKey(position, rules)
        if (awardedCells.has(key)) return false
        awardedCells.add(key)
      }
      stepPoints += award.amountPoints
    }
    if (stepPoints !== step.amountPoints || step.cleared.length !== awardedCells.size ||
      !step.cleared.every(position => isGodsTumblePosition(position, rules) && awardedCells.delete(godsTumbleCellKey(position, rules)))
    ) return false
    for (let reel = 0; reel < rules.columns; reel++) {
      const refills = step.cleared.filter(position => position.reel === reel).length
      for (let row = 0; row < refills; row++) {
        const symbol = step.reels[reel][row]
        landedMultiplier += rules.multiplierValues[symbol] ?? 0
      }
    }
    total += stepPoints
    reels = step.reels
  }
  if (multiplier === undefined && baseTotalPoints === undefined) return total === totalPoints
  return isNonNegativeInteger(baseTotalPoints) && baseTotalPoints === total &&
    typeof multiplier === 'number' && Number.isInteger(multiplier) &&
    multiplier === (total > 0 && landedMultiplier > 0 ? Math.min(landedMultiplier, rules.multiplierCap) : 1) &&
    totalPoints === baseTotalPoints * multiplier
}

function validGodsTumbleReels(value: unknown, rules: SlotTumbleRules): value is SpinResult['reels'] {
  return Array.isArray(value) && value.length === rules.columns &&
    value.every(reel => Array.isArray(reel) && reel.length === rules.rows &&
      reel.every(symbol => rules.symbols.includes(symbol)))
}

function isGodsTumblePosition(
  value: unknown,
  rules: SlotTumbleRules,
): value is { reel: number; row: number } {
  return isRecord(value) && isNonNegativeInteger(value.reel) && value.reel < rules.columns &&
    isNonNegativeInteger(value.row) && value.row < rules.rows
}

function godsTumbleCellKey(
  { reel, row }: { reel: number; row: number },
  rules: SlotTumbleRules,
): number {
  return reel * rules.rows + row
}

function isPositiveInteger(value: unknown): value is number {
  return isNonNegativeInteger(value) && value > 0
}

function isNonNegativeInteger(value: unknown): value is number {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0
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

function isKnownResultSymbol(value: unknown, symbolDefinitions?: SlotSymbolSet['definitions']): boolean {
  return typeof value === 'string' && (symbolDefinitions
    ? Object.hasOwn(symbolDefinitions, value) : isSlotSymbolId(value))
}

function isPaylinePayout(value: unknown, symbolDefinitions?: SlotSymbolSet['definitions']): boolean {
  return (
    isRecord(value) &&
    Number.isInteger(value.paylineId) &&
    typeof value.amountPoints === 'number' &&
    Array.isArray(value.matches) &&
    value.matches.every(match => isPaidMatch(match, symbolDefinitions))
  )
}

function isPaidMatch(value: unknown, symbolDefinitions?: SlotSymbolSet['definitions']): boolean {
  return (
    isRecord(value) &&
    typeof value.multiplier === 'number' &&
    typeof value.amountPoints === 'number' &&
    isSymbolMatch(value.match, symbolDefinitions)
  )
}

function isSymbolMatch(value: unknown, symbolDefinitions?: SlotSymbolSet['definitions']): boolean {
  return (
    isRecord(value) &&
    Number.isInteger(value.paylineId) &&
    isKnownResultSymbol(value.symbolId, symbolDefinitions) &&
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
