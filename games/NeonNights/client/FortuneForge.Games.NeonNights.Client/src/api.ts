import type { GridPosition } from '@fortuneforge/slot-client-core'
import { stepDiscoMultiplier } from './roundState.js'
import { NEON_GAME_ID, NEON_DISCO_SYMBOL_SET_ID, discoMultipliers, neonBaseSymbolIds, neonDiscoSymbolIds, type NeonAuthenticatedFetch, type NeonDiscoMultiplier, type NeonSpinRequest, type NeonSpinResult, type NeonState, type NeonSymbolId } from './types.js'

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
export function isNeonState(value: unknown): value is NeonState {
  return record(value) && natural(value.freeSpinsRemaining) && natural(value.neonDiscoSpinsRemaining) && discoMultiplier(value.neonDiscoMultiplier) &&
    (value.freeSpinWagerPoints === null || natural(value.freeSpinWagerPoints)) &&
    (value.freeSpinFeatureMode === null || typeof value.freeSpinFeatureMode === 'string')
}
export function isNeonSpinResult(value: unknown): value is NeonSpinResult {
  if (!record(value)) return false
  const disco = value.symbolSetId === NEON_DISCO_SYMBOL_SET_ID
  const allowed = disco ? neonDiscoSymbolIds : neonBaseSymbolIds
  if (!record(value) || value.gameId !== NEON_GAME_ID || typeof value.spinId !== 'string' ||
    (!disco && value.symbolSetId !== 'neon-arcade-symbols-v3') || value.pointValueInCents !== 1 || !natural(value.wagerPoints) ||
    !reels(value.reels, disco ? 4 : undefined, allowed) || !payout(value.payout, value.reels[0].length, allowed) || !isNeonState({ ...value }) ||
    typeof value.isFreeSpin !== 'boolean' || !natural(value.freeSpinsAwarded) || !natural(value.bananaBonusPoints) ||
    (value.slotsCreditsBalance !== null && (typeof value.slotsCreditsBalance !== 'number' || !Number.isFinite(value.slotsCreditsBalance))) ||
    !record(value.neonArcade) || typeof value.neonArcade.featureGamesEnabled !== 'boolean' || !natural(value.neonArcade.bonusSymbolCount) ||
    !Array.isArray(value.neonArcade.activators) || !value.neonArcade.activators.every(activator => record(activator) &&
      ((activator.kind === 'car' && activator.symbolId === '6') || (activator.kind === 'disco-ball' && activator.symbolId === 'POWER')) &&
      Array.isArray(activator.positions) && activator.positions.every(cell => position(cell, (value.reels as NeonSpinResult['reels'])[0].length)))) return false
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
    async spin(request: NeonSpinRequest, demo: boolean): Promise<NeonSpinResult> {
      const common = { gameId: NEON_GAME_ID, wagerPoints: request.wagerPoints, useFreeSpin: request.useFreeSpin }
      const response = demo
        ? await fetch('/api/slots/demo/spins', { method: 'POST', credentials: 'omit', headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ ...common, ...request.state, energyBalance: 0, sealCollections: [] }) })
        : await authenticated('/api/slots/spins', { method: 'POST', body: JSON.stringify({ ...common, useSpecialBoost: false }) })
      const result = normalizeDiscoMultiplier(await responseJson(response))
      if (!isNeonSpinResult(result)) throw new Error('The Neon Nights server returned an invalid reel result.')
      return result
    },
    async trigger(kind: 'car' | 'disco-ball', wagerPoints: number): Promise<NeonState> {
      const response = await authenticated('/api/slots/admin/neon-trigger', { method: 'POST', body: JSON.stringify({ symbol: kind, wagerPoints }) })
      if (!response.ok) await responseJson(response)
      return this.state()
    },
  }
}
