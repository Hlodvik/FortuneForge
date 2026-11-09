import { NEON_GAME_ID, neonSymbolIds, type NeonAuthenticatedFetch, type NeonSpinRequest, type NeonSpinResult, type NeonState } from './types.js'

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
const symbol = (value: unknown) => typeof value === 'string' && (neonSymbolIds as readonly string[]).includes(value)
const position = (value: unknown, rows: number) => record(value) && natural(value.reel) && value.reel < 5 && natural(value.row) && value.row < rows
function reels(value: unknown, rows?: number): value is NeonSpinResult['reels'] {
  return Array.isArray(value) && value.length === 5 && value.every(reel => Array.isArray(reel) &&
    (rows === undefined ? reel.length === 4 || reel.length === 7 : reel.length === rows) && reel.every(symbol)) &&
    value.every(reel => reel.length === value[0].length)
}
function payout(value: unknown, rows: number): boolean {
  return record(value) && natural(value.totalPoints) && Array.isArray(value.paylines) && value.paylines.every(line =>
    record(line) && natural(line.paylineId) && natural(line.amountPoints) && Array.isArray(line.matches) && line.matches.every(paid =>
      record(paid) && natural(paid.amountPoints) && typeof paid.multiplier === 'number' && Number.isFinite(paid.multiplier) && paid.multiplier >= 0 &&
      record(paid.match) && symbol(paid.match.symbolId) && natural(paid.match.matchLength) &&
      Array.isArray(paid.match.positions) && paid.match.positions.every(cell => position(cell, rows)) &&
      Array.isArray(paid.match.wildPositions) && paid.match.wildPositions.every(cell => position(cell, rows))))
}
export function isNeonState(value: unknown): value is NeonState {
  return record(value) && natural(value.freeSpinsRemaining) && natural(value.neonDiscoSpinsRemaining) &&
    (value.freeSpinWagerPoints === null || natural(value.freeSpinWagerPoints)) &&
    (value.freeSpinFeatureMode === null || typeof value.freeSpinFeatureMode === 'string')
}
export function isNeonSpinResult(value: unknown): value is NeonSpinResult {
  if (!record(value) || value.gameId !== NEON_GAME_ID || typeof value.spinId !== 'string' ||
    value.symbolSetId !== 'neon-arcade-symbols-v2' || value.pointValueInCents !== 1 || !natural(value.wagerPoints) ||
    !reels(value.reels) || !payout(value.payout, value.reels[0].length) || !isNeonState({ ...value }) ||
    typeof value.isFreeSpin !== 'boolean' || !natural(value.freeSpinsAwarded) || !natural(value.bananaBonusPoints) ||
    (value.slotsCreditsBalance !== null && (typeof value.slotsCreditsBalance !== 'number' || !Number.isFinite(value.slotsCreditsBalance))) ||
    !record(value.neonArcade) || typeof value.neonArcade.featureGamesEnabled !== 'boolean' || !natural(value.neonArcade.bonusSymbolCount) ||
    !Array.isArray(value.neonArcade.activators) || !value.neonArcade.activators.every(activator => record(activator) &&
      ['car', 'disco-ball', 'arcade-cabinet'].includes(String(activator.kind)) && ['6', 'POWER', 'SEAL_RAND'].includes(String(activator.symbolId)) &&
      Array.isArray(activator.positions) && activator.positions.every(cell => position(cell, (value.reels as NeonSpinResult['reels'])[0].length)))) return false
  if (value.neonDiscoCascade == null) return true
  const cascade = value.neonDiscoCascade
  if (!record(cascade) || !reels(cascade.initialReels, 4) || !Array.isArray(cascade.steps) || cascade.steps.length > 16 ||
    !natural(cascade.totalPoints) || !record(value.payout) || cascade.totalPoints !== value.payout.totalPoints ||
    JSON.stringify(cascade.initialReels) !== JSON.stringify(value.reels)) return false
  let total = 0
  for (const step of cascade.steps) {
    if (!record(step) || !reels(step.reels, 4) || !payout(step.payout, 4) || !record(step.payout) ||
      !Array.isArray(step.cleared) || step.cleared.length === 0 || !step.cleared.every(cell => position(cell, 4)) ||
      !Array.isArray(step.dropRows) || step.dropRows.length !== 5 || !step.dropRows.every(reel => Array.isArray(reel) && reel.length === 4 && reel.every(count => natural(count) && count <= 4))) return false
    total += Number(step.payout.totalPoints)
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
      const result = await responseJson(await authenticated(`/api/slots/state?gameId=${NEON_GAME_ID}`, { method: 'GET' }))
      if (!isNeonState(result)) throw new Error('The Neon Nights server returned an invalid state.')
      return result
    },
    async spin(request: NeonSpinRequest, demo: boolean): Promise<NeonSpinResult> {
      const common = { gameId: NEON_GAME_ID, wagerPoints: request.wagerPoints, useFreeSpin: request.useFreeSpin }
      const response = demo
        ? await fetch('/api/slots/demo/spins', { method: 'POST', credentials: 'omit', headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ ...common, ...request.state, energyBalance: 0, sealCollections: [] }) })
        : await authenticated('/api/slots/spins', { method: 'POST', body: JSON.stringify({ ...common, useSpecialBoost: false }) })
      const result = await responseJson(response)
      if (!isNeonSpinResult(result)) throw new Error('The Neon Nights server returned an invalid reel result.')
      return result
    },
    async trigger(kind: 'car' | 'disco-ball' | 'arcade-cabinet', wagerPoints: number): Promise<NeonState> {
      const response = await authenticated('/api/slots/admin/neon-trigger', { method: 'POST', body: JSON.stringify({ symbol: kind, wagerPoints }) })
      if (!response.ok) await responseJson(response)
      return this.state()
    },
  }
}
