import type { PaylinePayout } from '@fortuneforge/slot-client-core'
import {
  createPreparedSlotSpinStore, isPreparedSlotSpin, preparedSlotSpinHeaders, type PreparedSlotSpin,
} from '@fortuneforge/slot-client-core/preparedSpin'
import type { SlotPotionBottle, SlotPotionPreview, SlotPotionState, SlotTomeState } from './arcaneFeatures.js'
import { ARCANE_ARCHIVES_SYMBOLS } from './symbols.js'
import {
  ARCANE_GAME_ID, ARCANE_LIGHTNING_PAYLINE_ID, ARCANE_WAND_PAYLINE_ID,
  type ArcaneAuthenticatedFetch, type ArcaneSpinResult, type ArcaneSpinTerms, type ArcaneState,
} from './types.js'

const preparedPath = '/api/slots/arcane/prepared-spins'
// Retain the existing Arcane storage namespace so pending rounds survive the client migration.
const savedSpins = createPreparedSlotSpinStore('arcane')
const record = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === 'object' && !Array.isArray(value)
const natural = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value >= 0
const positive = (value: unknown): value is number => natural(value) && value > 0
const money = (value: unknown): value is number => typeof value === 'number' && Number.isFinite(value) && value >= 0
const symbol = (value: unknown): value is string => typeof value === 'string' && Object.hasOwn(ARCANE_ARCHIVES_SYMBOLS.definitions, value)
const counts = (value: unknown): value is Readonly<Record<string, number>> => record(value) && Object.values(value).every(natural)

type ArcaneProblem = {
  error?: string; detail?: string; code?: string; available?: number; required?: number
  freeSpinsRemaining?: number; retryAfterMilliseconds?: number; pendingOperation?: unknown; preparedOperationPending?: boolean
}
export class ArcaneRequestError extends Error {
  readonly status: number
  readonly code?: string
  readonly available?: number
  readonly required?: number
  readonly retryAfterMilliseconds?: number
  readonly pendingOperation?: PreparedSlotSpin
  readonly preparedOperationPending: boolean
  constructor(message: string, status: number, problem: ArcaneProblem | null) {
    super(message); this.name = 'ArcaneRequestError'; this.status = status
    this.code = problem?.code; this.available = problem?.available; this.required = problem?.required
    this.retryAfterMilliseconds = problem?.retryAfterMilliseconds
    this.pendingOperation = isPreparedSlotSpin(problem?.pendingOperation) ? problem.pendingOperation : undefined
    this.preparedOperationPending = problem?.preparedOperationPending === true
  }
}

export function isArcaneTomeState(value: unknown): value is SlotTomeState {
  if (!record(value) || typeof value.selectedPage !== 'string' ||
    !(value.activePage === null || typeof value.activePage === 'string') ||
    !natural(value.boostedSpinsRemaining) || value.boostedSpinsRemaining > 5 ||
    typeof value.grimoireReady !== 'boolean' || !counts(value.ingredients) ||
    !Array.isArray(value.pages) || value.pages.length === 0) return false
  const pages = value.pages
  return pages.every(page => record(page) && typeof page.id === 'string' && typeof page.label === 'string' &&
    Array.isArray(page.ingredients) && page.ingredients.length === 2 && page.ingredients.every(symbol)) &&
    pages.some(page => page.id === value.selectedPage) &&
    (value.activePage === null ? value.boostedSpinsRemaining === 0
      : value.boostedSpinsRemaining > 0 && pages.some(page => page.id === value.activePage))
}
function potionPreview(value: unknown): value is SlotPotionPreview & Record<string, unknown> {
  return record(value) && symbol(value.ingredientId) && typeof value.name === 'string' &&
    typeof value.effect === 'string' && natural(value.potency)
}
function potionBottle(value: unknown): value is SlotPotionBottle {
  return record(value) && positive(value.id) && potionPreview(value) &&
    (value.variantId === undefined || typeof value.variantId === 'string' && value.variantId.length > 0) &&
    (value.lockedWagerPoints === undefined || positive(value.lockedWagerPoints))
}
export function isArcanePotionState(value: unknown): value is SlotPotionState {
  return record(value) && positive(value.capacity) && positive(value.majority) && value.majority <= value.capacity &&
    Array.isArray(value.pot) && value.pot.length < value.capacity && value.pot.every(symbol) &&
    Array.isArray(value.bottles) && value.bottles.every(potionBottle) &&
    (value.preview === null || potionPreview(value.preview)) &&
    (value.active === null || record(value.active) && potionBottle(value.active.bottle) && positive(value.active.spinsRemaining))
}
export function isArcaneState(value: unknown): value is ArcaneState & Record<string, unknown> {
  return record(value) && natural(value.freeSpinsRemaining) &&
    (value.freeSpinWagerPoints === null || positive(value.freeSpinWagerPoints)) &&
    (value.freeSpinFeatureMode === null || typeof value.freeSpinFeatureMode === 'string') &&
    natural(value.specialPointsBalance) && natural(value.energyBalance) &&
    (value.arcaneTome == null || isArcaneTomeState(value.arcaneTome)) &&
    (value.arcanePotions == null || isArcanePotionState(value.arcanePotions)) &&
    (value.slotsCreditsBalance == null || money(value.slotsCreditsBalance)) &&
    (value.pendingOperation == null || isPreparedSlotSpin(value.pendingOperation))
}
function position(value: unknown, reels: string[][]): value is { reel: number; row: number } & Record<string, unknown> {
  return record(value) && natural(value.reel) && natural(value.row) && value.reel < reels.length && value.row < reels[value.reel].length
}
function validPayline(value: unknown, reels: string[][]): value is PaylinePayout {
  return record(value) && natural(value.paylineId) && natural(value.amountPoints) && Array.isArray(value.matches) &&
    value.matches.every(paid => record(paid) && money(paid.multiplier) && natural(paid.amountPoints) &&
      record(paid.match) && paid.match.paylineId === value.paylineId && symbol(paid.match.symbolId) &&
      positive(paid.match.matchLength) && Array.isArray(paid.match.positions) &&
      paid.match.positions.length === paid.match.matchLength && paid.match.positions.every(cell => position(cell, reels)) &&
      Array.isArray(paid.match.wildPositions) && paid.match.wildPositions.every(cell => position(cell, reels)))
}
function validCollectors(value: Record<string, unknown>, reels: string[][], paylines: PaylinePayout[], total: number): boolean {
  const wand = value.arcaneWandCollection
  if (wand != null) {
    if (!record(wand) || !position(wand.wand, reels) || reels[wand.wand.reel][wand.wand.row] !== 'WAND' ||
      !Array.isArray(wand.cells) || wand.cells.length === 0 || !positive(wand.totalPoints) || wand.totalPoints > total) return false
    let sum = 0
    const seen = new Set<string>()
    for (const cell of wand.cells) {
      if (!record(cell) || !position(cell, reels) || !['POWER', 'FORTUNE_RUNE', 'BOLT'].includes(reels[cell.reel][cell.row]) ||
        cell.symbolId !== reels[cell.reel][cell.row] || !positive(cell.multiplier) || !positive(cell.amountPoints) ||
        cell.potionBonusPoints != null && !natural(cell.potionBonusPoints) || seen.has(`${cell.reel}:${cell.row}`)) return false
      seen.add(`${cell.reel}:${cell.row}`); sum += cell.amountPoints
    }
    if (sum !== wand.totalPoints || !paylines.some(line => line.paylineId === ARCANE_WAND_PAYLINE_ID && line.amountPoints === sum)) return false
  }
  const lightning = value.lightningCollection
  if (lightning != null) {
    if (!record(lightning) || !position(lightning.collector, reels) || reels[lightning.collector.reel][lightning.collector.row] !== 'BOLT' ||
      !Array.isArray(lightning.cells) || lightning.cells.length === 0 || !positive(lightning.totalPoints) || lightning.totalPoints > total) return false
    let sum = 0
    const seen = new Set<string>()
    for (const cell of lightning.cells) {
      if (!record(cell) || !position(cell, reels) || !positive(cell.amountPoints) ||
        !['7', 'POWER', 'SEAL_RAND'].includes(reels[cell.reel][cell.row]) || seen.has(`${cell.reel}:${cell.row}`)) return false
      seen.add(`${cell.reel}:${cell.row}`); sum += cell.amountPoints
    }
    if (sum !== lightning.totalPoints || !paylines.some(line => line.paylineId === ARCANE_LIGHTNING_PAYLINE_ID && line.amountPoints === sum)) return false
  }
  return true
}
export function isArcaneSpinResult(value: unknown): value is ArcaneSpinResult {
  if (!record(value) || !isArcaneState(value) || value.gameId !== ARCANE_GAME_ID || typeof value.spinId !== 'string' || value.spinId.length === 0 ||
    typeof value.reelSetId !== 'string' || typeof value.symbolSetId !== 'string' || typeof value.paytableId !== 'string' ||
    !positive(value.wagerPoints) || !positive(value.pointValueInCents) || typeof value.isFreeSpin !== 'boolean' ||
    !natural(value.freeSpinsAwarded) || !natural(value.specialPointsAwarded) || !money(value.slotsCreditsBalance) ||
    (value.chargedWagerCents !== undefined && !natural(value.chargedWagerCents)) ||
    !Array.isArray(value.reels) || value.reels.length !== 5 ||
    !value.reels.every(reel => Array.isArray(reel) && (reel.length === 4 || reel.length === 6) && reel.every(symbol)) ||
    !value.reels.every((reel, _index, all) => reel.length === all[0].length) ||
    !Array.isArray(value.reelStops) || value.reelStops.length !== 5 || !value.reelStops.every(natural) ||
    !record(value.payout) || !natural(value.payout.totalPoints) || !Array.isArray(value.payout.paylines) ||
    !value.payout.paylines.every(line => validPayline(line, value.reels as string[][])) ||
    (value.arcaneIngredientsAwarded != null && !counts(value.arcaneIngredientsAwarded))) return false
  const paylines = value.payout.paylines as PaylinePayout[]
  return paylines.reduce((sum, line) => sum + line.amountPoints, 0) === value.payout.totalPoints &&
    validCollectors(value, value.reels as string[][], paylines, value.payout.totalPoints)
}

/** Arcane owns its paid transport and game validation; authentication is injected by the platform. */
export function createArcaneApi(authenticatedFetch?: ArcaneAuthenticatedFetch, accountId?: string) {
  let preparedSupported = false
  let uncertainLegacySpin = false
  const pendingSpin = () => accountId ? savedSpins.read(accountId) : undefined
  const authenticated = (path: string, init?: RequestInit) => {
    if (!authenticatedFetch || !accountId) throw new Error('Arcane Archives requires an authenticated account for paid play.')
    return authenticatedFetch(path, init)
  }
  async function responseJson(response: Response): Promise<unknown> {
    if (!response.ok) {
      const problem = await response.json().catch(() => null) as ArcaneProblem | null
      throw new ArcaneRequestError(problem?.error ?? problem?.detail ?? `Arcane request failed with status ${response.status}.`, response.status, problem)
    }
    return response.json()
  }
  async function submit(terms: ArcaneSpinTerms, saved?: PreparedSlotSpin, canAdoptPending = true): Promise<ArcaneSpinResult> {
    let response: Response | undefined
    try {
      response = await authenticated(saved ? preparedPath : '/api/slots/spins', {
        method: 'POST', headers: preparedSlotSpinHeaders(accountId, saved?.operationId),
        body: JSON.stringify({ gameId: ARCANE_GAME_ID, wagerPoints: terms.wagerPoints,
          useFreeSpin: terms.useFreeSpin, useSpecialBoost: terms.useSpecialBoost }),
      })
      const result = await responseJson(response)
      if (!isArcaneSpinResult(result) || result.wagerPoints !== terms.wagerPoints || result.isFreeSpin !== terms.useFreeSpin)
        throw new Error('The Arcane server returned an invalid or mismatched settled round. Reload to recover.')
      if (saved && accountId) savedSpins.forget(accountId, saved.operationId)
      return result
    } catch (error) {
      if (accountId && error instanceof ArcaneRequestError && error.pendingOperation) {
        const pending = error.pendingOperation
        savedSpins.remember(accountId, pending)
        if (canAdoptPending && pending.operationId !== saved?.operationId)
          return submit(pending, pending, false)
      } else if (saved && accountId && error instanceof ArcaneRequestError &&
        (error.status === 400 || error.code === 'slot-prepared-start-paused' || error.status === 409 && !error.preparedOperationPending)) {
        savedSpins.forget(accountId, saved.operationId)
      }
      // A lost reply without an idempotent operation cannot safely be turned into another paid POST.
      if (!saved && !(error instanceof ArcaneRequestError)) uncertainLegacySpin = true
      throw error
    }
  }
  const requireNoPendingRound = () => {
    if (pendingSpin() || uncertainLegacySpin) throw new ArcaneRequestError('Recover the current Arcane round before changing the workbench.', 409, null)
  }
  return {
    pendingSpin,
    uncertainSpin: () => uncertainLegacySpin,
    async state(signal?: AbortSignal): Promise<ArcaneState> {
      const response = await authenticated(`/api/slots/state?gameId=${ARCANE_GAME_ID}`, {
        method: 'GET', headers: preparedSlotSpinHeaders(accountId), signal, cache: 'no-store',
      })
      const value = await responseJson(response)
      if (!isArcaneState(value)) throw new Error('The Arcane server returned an invalid workbench state.')
      preparedSupported = response.headers.get('X-FortuneForge-Prepared-Spin') === '1'
      if (accountId && value.pendingOperation && !pendingSpin()) savedSpins.remember(accountId, value.pendingOperation)
      return { ...value, arcaneTome: value.arcaneTome ?? null, arcanePotions: value.arcanePotions ?? null }
    },
    async resumeSpin(): Promise<ArcaneSpinResult | undefined> {
      const saved = pendingSpin()
      return saved ? submit(saved, saved) : undefined
    },
    async spin(terms: ArcaneSpinTerms): Promise<ArcaneSpinResult> {
      if (!positive(terms.wagerPoints)) throw new Error('Select a valid Arcane wager.')
      if (uncertainLegacySpin) throw new Error('Reload Arcane Archives to recover the settled balance before another round.')
      const saved = pendingSpin()
      if (saved) return submit(saved, saved)
      const operation = preparedSupported && accountId ? { ...terms, operationId: crypto.randomUUID() } : undefined
      if (operation && accountId) savedSpins.remember(accountId, operation)
      return submit(terms, operation)
    },
    async changeTomePage(page: string, activate: boolean): Promise<SlotTomeState> {
      requireNoPendingRound()
      const value = await responseJson(await authenticated('/api/slots/arcane/tome', {
        method: 'POST', headers: preparedSlotSpinHeaders(accountId), body: JSON.stringify({ page, activate }),
      }))
      if (!isArcaneTomeState(value)) throw new Error('The Arcane server returned invalid Tome state.')
      return value
    },
    async activatePotion(potionId: number): Promise<SlotPotionState> {
      requireNoPendingRound()
      const value = await responseJson(await authenticated('/api/slots/arcane/potions/activate', {
        method: 'POST', headers: preparedSlotSpinHeaders(accountId), body: JSON.stringify({ potionId }),
      }))
      if (!isArcanePotionState(value)) throw new Error('The Arcane server returned invalid potion state.')
      return value
    },
  }
}
