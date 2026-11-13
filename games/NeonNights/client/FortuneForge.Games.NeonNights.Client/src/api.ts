import type { GridPosition } from '@fortuneforge/slot-client-core'
import { stepDiscoMultiplier } from './roundState.js'
import { NEON_GAME_ID, NEON_CAR_SYMBOL_SET_ID, NEON_DISCO_SYMBOL_SET_ID, discoMultipliers, neonBaseSymbolIds, neonCarSymbolIds, neonDiscoSymbolIds, type NeonAuthenticatedFetch, type NeonCarRespinOffer, type NeonCarRespinResult, type NeonDiscoMultiplier, type NeonNitroChoice, type NeonPendingChoiceResponse, type NeonSpinRequest, type NeonSpinResult, type NeonState, type NeonSymbolId } from './types.js'

type Problem = { error?: string; detail?: string; code?: string; available?: number; required?: number; freeSpinsRemaining?: number; retryAfterMilliseconds?: number }
export class NeonRequestError extends Error {
  readonly status: number
  readonly code?: string
  readonly available?: number
  readonly freeSpinsRemaining?: number
  readonly retryAfterMilliseconds?: number
  constructor(message: string, status: number, problem: Problem | null) {
    super(message)
    this.name = 'NeonRequestError'
    this.status = status
    this.code = problem?.code
    this.available = problem?.available
    this.freeSpinsRemaining = problem?.freeSpinsRemaining
    this.retryAfterMilliseconds = problem?.retryAfterMilliseconds
  }
}
const record = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === 'object' && !Array.isArray(value)
const natural = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value >= 0
const discoMultiplier = (value: unknown): value is NeonDiscoMultiplier => discoMultipliers.some(tier => tier === value)
const normalizeDiscoMultiplier = (value: unknown) =>
  record(value) && value.neonDiscoMultiplier === undefined ? { ...value, neonDiscoMultiplier: 1 } : value
const symbol = (value: unknown, allowed: readonly string[]): value is NeonSymbolId => typeof value === 'string' && allowed.includes(value)
const position = (value: unknown, rows: number): value is GridPosition => record(value) && natural(value.reel) && value.reel < 5 && natural(value.row) && value.row < rows
const reelIndex = (value: unknown): value is number => natural(value) && value < 5
const sameReel = (first: readonly NeonSymbolId[], second: readonly NeonSymbolId[]) =>
  first.length === second.length && first.every((cell, row) => cell === second[row])
const sameBoard = (first: NeonSpinResult['reels'], second: NeonSpinResult['reels']) =>
  first.length === second.length && first.every((reel, column) => sameReel(reel, second[column]))
function reels(value: unknown, rows?: number, allowed: readonly string[] = neonBaseSymbolIds): value is NeonSpinResult['reels'] {
  return Array.isArray(value) && value.length === 5 && value.every(reel => Array.isArray(reel) &&
    (rows === undefined ? reel.length === 4 || reel.length === 7 : reel.length === rows) && reel.every(cell => symbol(cell, allowed))) &&
    value.every(reel => reel.length === value[0].length)
}
type ValidatedPayout = { totalPoints: number; paylines: { paylineId: number; amountPoints: number; matches: {
  amountPoints: number; multiplier: number; match: { symbolId: NeonSymbolId; matchLength: number; positions: GridPosition[]; wildPositions: GridPosition[] }
}[] }[] }
function payout(value: unknown, rows: number, allowed: readonly string[] = neonBaseSymbolIds): value is ValidatedPayout {
  return record(value) && natural(value.totalPoints) && Array.isArray(value.paylines) && value.paylines.every(line =>
    record(line) && natural(line.paylineId) && natural(line.amountPoints) && Array.isArray(line.matches) && line.matches.every(paid =>
      record(paid) && natural(paid.amountPoints) && typeof paid.multiplier === 'number' && Number.isFinite(paid.multiplier) && paid.multiplier >= 0 &&
      record(paid.match) && symbol(paid.match.symbolId, allowed) && natural(paid.match.matchLength) &&
      Array.isArray(paid.match.positions) && paid.match.positions.every(cell => position(cell, rows)) &&
      Array.isArray(paid.match.wildPositions) && paid.match.wildPositions.every(cell => position(cell, rows))))
}
const cellKey = ({ reel, row }: GridPosition) => `${reel}:${row}`
function clusterCells(paid: ValidatedPayout, board: NeonSpinResult['reels']): Set<string> | null {
  const all = new Set<string>()
  let total = 0
  for (const line of paid.paylines) {
    if (line.matches.length !== 1 || line.amountPoints <= 0) return null
    const award = line.matches[0]
    const match = award.match
    const cells = new Set(match.positions.map(cellKey))
    if (match.matchLength < 4 || match.matchLength !== match.positions.length || cells.size !== match.positions.length ||
      match.wildPositions.length !== 0 || award.amountPoints !== line.amountPoints) return null
    const pending = [match.positions[0]]
    const connected = new Set<string>()
    while (pending.length > 0) {
      const cell = pending.pop()!
      const key = cellKey(cell)
      if (connected.has(key)) continue
      if (board[cell.reel][cell.row] !== match.symbolId || all.has(key)) return null
      connected.add(key)
      for (const neighbour of [
        { reel: cell.reel - 1, row: cell.row }, { reel: cell.reel + 1, row: cell.row },
        { reel: cell.reel, row: cell.row - 1 }, { reel: cell.reel, row: cell.row + 1 },
      ]) {
        if (board[neighbour.reel]?.[neighbour.row] !== match.symbolId) continue
        if (!cells.has(cellKey(neighbour))) return null
        pending.push(neighbour)
      }
    }
    if (connected.size !== cells.size) return null
    for (const key of cells) all.add(key)
    total += line.amountPoints
  }
  return all.size > 0 && total === paid.totalPoints ? all : null
}
function isCarRespinOffer(value: unknown): value is NeonCarRespinOffer {
  return record(value) && typeof value.spinId === 'string' && value.spinId.length > 0 &&
    reels(value.reels, 7, neonCarSymbolIds) && natural(value.roundWinPoints) && Array.isArray(value.eligibleReels) &&
    value.eligibleReels.every(index => natural(index) && index < 5) &&
    new Set(value.eligibleReels).size === value.eligibleReels.length
}
export function isNeonState(value: unknown): value is NeonState {
  return record(value) && natural(value.freeSpinsRemaining) && natural(value.neonDiscoSpinsRemaining) && discoMultiplier(value.neonDiscoMultiplier) &&
    (value.freeSpinWagerPoints === null || natural(value.freeSpinWagerPoints)) &&
    (value.freeSpinFeatureMode === null || typeof value.freeSpinFeatureMode === 'string') &&
    (value.neonCarRoundWinPoints == null || natural(value.neonCarRoundWinPoints)) &&
    (value.neonDiscoRoundWinPoints == null || natural(value.neonDiscoRoundWinPoints)) &&
    (value.neonCarRespinOffer == null || isCarRespinOffer(value.neonCarRespinOffer)) &&
    (value.pendingNeonNitroChoice == null || isNitroChoice(value.pendingNeonNitroChoice))
}
function validNitroWild(value: unknown, settled: NeonSpinResult['reels'], allowPending = false): boolean {
  if (value == null) return settled.slice(0, -1).every(reel => !reel.includes('NITRO'))
  if (!record(value) || !reels(value.initialReels, 7, neonCarSymbolIds) || !position(value.trigger, 7)) return false

  const initial = value.initialReels
  const firstNitroReel = initial.slice(0, -1).findIndex(reel => reel.includes('NITRO'))
  const eligibleReel = firstNitroReel + 1
  if (firstNitroReel < 0 || value.trigger.reel !== firstNitroReel ||
    value.trigger.row !== initial[firstNitroReel].indexOf('NITRO') || value.eligibleReel !== eligibleReel) return false

  const secondRow = initial[eligibleReel].indexOf('NITRO')
  let expandedReel: number | null = null
  if (value.secondNitro != null) {
    if (!position(value.secondNitro, 7) || secondRow < 0 || value.secondNitro.reel !== eligibleReel || value.secondNitro.row !== secondRow) return false
    if (value.requiresChoice === true) {
      if (!allowPending || eligibleReel !== 4) return false
    } else if (eligibleReel < 4) expandedReel = eligibleReel + 1
    else if (reelIndex(value.expandedReel)) expandedReel = value.expandedReel
    else return false
  } else {
    // Older stored responses expanded the second bottle reel and did not identify its position.
    if (value.requiresChoice === true || allowPending) return false
    if (secondRow >= 0) expandedReel = eligibleReel
  }
  return value.expandedReel === expandedReel && settled.every((reel, reelIndex) =>
    reel.every((symbolId, rowIndex) => symbolId === (reelIndex === expandedReel ? 'ACE' : initial[reelIndex][rowIndex])))
}
function isNitroChoice(value: unknown): value is NeonNitroChoice {
  if (!record(value) || typeof value.spinId !== 'string' || value.spinId.length === 0 ||
    !natural(value.wagerPoints) || value.wagerPoints === 0 || !natural(value.freeSpinsRemaining) ||
    !reels(value.initialReels, 7, neonCarSymbolIds) || !Array.isArray(value.eligibleReels) || value.eligibleReels.length !== 5 ||
    !value.eligibleReels.every(reelIndex) || new Set(value.eligibleReels).size !== 5 ||
    !record(value.neonNitroWild) || value.neonNitroWild.requiresChoice !== true ||
    !reels(value.neonNitroWild.initialReels, 7, neonCarSymbolIds) || !sameBoard(value.neonNitroWild.initialReels, value.initialReels) ||
    !validNitroWild(value.neonNitroWild, value.initialReels, true)) return false
  return value.sourceKind === 'spin' || (value.sourceKind === 'car-respin' && reelIndex(value.reelIndex) && natural(value.roundWinPoints))
}

function readPendingChoice(value: unknown, sourceKind: NeonNitroChoice['sourceKind']): NeonPendingChoiceResponse {
  if (!record(value) || !isNitroChoice(value.pendingNeonNitroChoice) || value.pendingNeonNitroChoice.sourceKind !== sourceKind)
    throw new Error('The Neon Nights server returned an invalid Nitro choice.')
  return { pendingNeonNitroChoice: value.pendingNeonNitroChoice }
}
export function isNeonSpinResult(value: unknown): value is NeonSpinResult {
  if (!record(value)) return false
  const disco = value.symbolSetId === NEON_DISCO_SYMBOL_SET_ID
  const car = value.symbolSetId === NEON_CAR_SYMBOL_SET_ID
  const allowed = disco ? neonDiscoSymbolIds : car ? neonCarSymbolIds : neonBaseSymbolIds
  const state = { ...value }
  if (!record(value) || value.gameId !== NEON_GAME_ID || typeof value.spinId !== 'string' ||
    (!disco && !car && value.symbolSetId !== 'neon-arcade-symbols-v3') || value.pointValueInCents !== 1 || !natural(value.wagerPoints) ||
    !reels(value.reels, disco ? 4 : car ? 7 : undefined, allowed) || !payout(value.payout, value.reels[0].length, allowed) || !isNeonState(state) ||
    typeof value.isFreeSpin !== 'boolean' || !natural(value.freeSpinsAwarded) || !natural(value.bananaBonusPoints) ||
    (value.slotsCreditsBalance !== null && (typeof value.slotsCreditsBalance !== 'number' || !Number.isFinite(value.slotsCreditsBalance))) ||
    !record(value.neonArcade) || typeof value.neonArcade.featureGamesEnabled !== 'boolean' || !natural(value.neonArcade.bonusSymbolCount) ||
    !Array.isArray(value.neonArcade.activators) || !value.neonArcade.activators.every(activator => record(activator) &&
      ((activator.kind === 'car' && activator.symbolId === '6') || (activator.kind === 'disco-ball' && activator.symbolId === 'POWER')) &&
      Array.isArray(activator.positions) && activator.positions.every(cell => position(cell, (value.reels as NeonSpinResult['reels'])[0].length)))) return false
  if (car ? value.isFreeSpin !== true || !validNitroWild(value.neonNitroWild, value.reels) : value.neonNitroWild != null) return false
  if (state.pendingNeonNitroChoice != null) return false
  const offer = state.neonCarRespinOffer
  if (offer && (!car || offer.spinId !== value.spinId || !sameBoard(offer.reels, value.reels))) return false
  if (!disco) return value.neonDiscoCascade == null
  if (value.isFreeSpin || value.freeSpinsAwarded !== 0 || value.bananaBonusPoints !== 0 ||
    value.neonArcade.bonusSymbolCount !== 0 || value.neonArcade.activators.length !== 0) return false
  const cascade = value.neonDiscoCascade
  if (!record(cascade) || !reels(cascade.initialReels, 4, allowed) || !Array.isArray(cascade.steps) || cascade.steps.length > 16 ||
    !natural(cascade.totalPoints) || !record(value.payout) || cascade.totalPoints !== value.payout.totalPoints ||
    JSON.stringify(cascade.initialReels) !== JSON.stringify(value.reels)) return false
  let total = 0
  let board = cascade.initialReels
  let expectedMultiplier: NeonDiscoMultiplier | undefined
  for (const step of cascade.steps) {
    if (!record(step) || !discoMultiplier(step.multiplier) || (expectedMultiplier !== undefined && step.multiplier !== expectedMultiplier) ||
      !reels(step.reels, 4, allowed) || !payout(step.payout, 4, allowed) ||
      !Array.isArray(step.cleared) || step.cleared.length === 0 || !step.cleared.every(cell => position(cell, 4)) ||
      !Array.isArray(step.dropRows) || step.dropRows.length !== 5 || !step.dropRows.every(reel => Array.isArray(reel) && reel.length === 4 && reel.every(count => natural(count) && count <= 4))) return false
    const paidCells = clusterCells(step.payout, board)
    if (!paidCells || paidCells.size !== step.cleared.length || !step.cleared.every(cell => paidCells.has(cellKey(cell))) ||
      new Set(step.cleared.map(cellKey)).size !== paidCells.size) return false
    total += step.payout.totalPoints
    board = step.reels
    expectedMultiplier = stepDiscoMultiplier(step.multiplier, 1)
  }
  return total === cascade.totalPoints
}
function isCarRespinResult(value: unknown, spinId: string, selectedReel: number): value is NeonCarRespinResult {
  if (!record(value) || value.sourceSpinId !== spinId || value.reelIndex !== selectedReel || !natural(value.stopIndex) ||
    !reels(value.initialReels, 7, neonCarSymbolIds) || !reels(value.reels, 7, neonCarSymbolIds) ||
    !payout(value.payout, 7, neonCarSymbolIds) || !natural(value.originalPayoutPoints) ||
    value.improvementPoints !== Math.max(0, value.payout.totalPoints - value.originalPayoutPoints) ||
    (value.roundWinPoints != null && !natural(value.roundWinPoints)) ||
    typeof value.slotsCreditsBalance !== 'number' || !Number.isFinite(value.slotsCreditsBalance) ||
    typeof value.alreadyApplied !== 'boolean') return false

  const nitro = value.neonNitroWild
  if (nitro == null) return sameBoard(value.initialReels, value.reels)
  return record(nitro) && reels(nitro.initialReels, 7, neonCarSymbolIds) &&
    sameBoard(nitro.initialReels, value.initialReels) && validNitroWild(nitro, value.reels)
}
async function responseJson(response: Response): Promise<unknown> {
  if (!response.ok) {
    const problem = await response.json().catch(() => null) as Problem | null
    throw new NeonRequestError(problem?.error ?? problem?.detail ?? `Neon Nights request failed with status ${response.status}.`, response.status, problem)
  }
  return response.json()
}
export function createNeonApi(authenticatedFetch?: NeonAuthenticatedFetch) {
  const authenticated = (path: string, init?: RequestInit) => {
    if (!authenticatedFetch) throw new Error('Neon Nights requires an authenticated session for paid play.')
    return authenticatedFetch(path, init)
  }
  return {
    async availability(signal: AbortSignal) {
      const response = await fetch(`/api/slots/demo/status?gameId=${NEON_GAME_ID}`, { credentials: 'omit', cache: 'no-store', signal })
      if (response.status !== 204) throw new Error('The Neon Nights game service is offline.')
    },
    async state(): Promise<NeonState> {
      const result = normalizeDiscoMultiplier(await responseJson(await authenticated(`/api/slots/state?gameId=${NEON_GAME_ID}`, { method: 'GET' })))
      if (!isNeonState(result)) throw new Error('The Neon Nights server returned an invalid state.')
      return result
    },
    async spin(request: NeonSpinRequest, demo: boolean): Promise<NeonSpinResult | NeonPendingChoiceResponse> {
      const common = { gameId: NEON_GAME_ID, wagerPoints: request.wagerPoints, useFreeSpin: request.useFreeSpin }
      const response = demo
        ? await fetch('/api/slots/demo/spins', { method: 'POST', credentials: 'omit', headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ ...common, ...request.state, energyBalance: 0, sealCollections: [] }) })
        : await authenticated('/api/slots/spins', { method: 'POST', body: JSON.stringify({ ...common, useSpecialBoost: false }) })
      const result = normalizeDiscoMultiplier(await responseJson(response))
      if (!demo && response.status === 202) return readPendingChoice(result, 'spin')
      if (!isNeonSpinResult(result)) throw new Error('The Neon Nights server returned an invalid reel result.')
      return result
    },
    async respin(offer: NeonCarRespinOffer, reelIndex: number): Promise<NeonCarRespinResult | NeonPendingChoiceResponse> {
      const response = await authenticated('/api/slots/neon/car-respin', { method: 'POST', body: JSON.stringify({ spinId: offer.spinId, reelIndex }) })
      const result = await responseJson(response)
      if (response.status === 202) {
        const pending = readPendingChoice(result, 'car-respin')
        const choice = pending.pendingNeonNitroChoice
        if (choice.spinId !== offer.spinId || choice.sourceKind !== 'car-respin' || choice.reelIndex !== reelIndex ||
          !choice.initialReels.every((reel, index) => index === reelIndex || sameReel(reel, offer.reels[index])))
          throw new Error('The Neon Nights server returned a Nitro choice for a different respin.')
        return pending
      }
      if (!isCarRespinResult(result, offer.spinId, reelIndex) ||
        !result.initialReels.every((reel, index) => index === reelIndex || sameReel(reel, offer.reels[index])))
        throw new Error('The Neon Nights server returned an invalid respin result.')
      return result
    },
    async chooseNitro(choice: NeonNitroChoice, reelIndex: number): Promise<NeonSpinResult | NeonCarRespinResult> {
      const path = choice.sourceKind === 'spin' ? '/api/slots/neon/nitro-choice' : '/api/slots/neon/car-respin/nitro-choice'
      const response = await authenticated(path, { method: 'POST', body: JSON.stringify({ spinId: choice.spinId, reelIndex }) })
      const result = normalizeDiscoMultiplier(await responseJson(response))
      const valid = choice.sourceKind === 'spin' ? isNeonSpinResult(result) && result.spinId === choice.spinId
        : isCarRespinResult(result, choice.spinId, choice.reelIndex)
      if (!valid || !record(result) || !record(result.neonNitroWild) || result.neonNitroWild.expandedReel !== reelIndex ||
        !reels(result.neonNitroWild.initialReels, 7, neonCarSymbolIds) || !sameBoard(result.neonNitroWild.initialReels, choice.initialReels))
        throw new Error('The Neon Nights server returned an invalid Nitro result.')
      return result as NeonSpinResult | NeonCarRespinResult
    },
    async dismissRespin(spinId: string): Promise<void> {
      const response = await authenticated('/api/slots/neon/car-respin/dismiss', { method: 'POST', body: JSON.stringify({ spinId }) })
      if (!response.ok) await responseJson(response)
    },
    async trigger(kind: 'car' | 'disco-ball', wagerPoints: number): Promise<NeonState> {
      const response = await authenticated('/api/slots/admin/neon-trigger', { method: 'POST', body: JSON.stringify({ symbol: kind, wagerPoints }) })
      if (!response.ok) await responseJson(response)
      return this.state()
    },
  }
}
