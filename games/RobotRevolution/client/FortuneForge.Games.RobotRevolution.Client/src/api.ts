import type { PaylinePayout } from '@fortuneforge/slot-client-core'
import { createPreparedSlotSpinStore, isPreparedSlotSpin, preparedSlotSpinHeaders, preparedSlotSpinPath, type PreparedSlotSpin } from '@fortuneforge/slot-client-core/preparedSpin'
import { record, natural, positive, money, grid, paylines } from '@fortuneforge/slot-client-core/contracts/presentationValidation'
import { FIFTH_SUN_GAME_ID } from './definition'
import { FIFTH_SUN_SYMBOL_SET } from './symbols'
import { decodeSunwheelFeature, type SunwheelFeature } from './sunwheel'

export type SunstoneFetch = (path: string, init?: RequestInit) => Promise<Response>
export type PaidEclipse = { mode: 'solar-eclipse' | 'lunar-eclipse'; remaining: number; wagerPoints: number }
export type SunstoneState = {
  freeSpinsRemaining: number; freeSpinWagerPoints: number | null; freeSpinFeatureMode: string | null
  paidMode?: PaidEclipse | null; slotsCreditsBalance: number | null; pendingOperation?: PreparedSlotSpin | null
}
export type SunstoneResult = SunstoneState & {
  spinId: string; gameId: string; wagerPoints: number; isFreeSpin: boolean; pointValueInCents: number
  reels: string[][]; payout: { totalPoints: number; paylines: PaylinePayout[] }; spinFeature?: SunwheelFeature | null
}
const store = createPreparedSlotSpinStore('sunstone-eclipse')
const wager = (value: unknown) => positive(value) && value >= 12 && value <= 300 && value % 2 === 0
const mode = (value: unknown): value is PaidEclipse => record(value) &&
  (value.mode === 'solar-eclipse' || value.mode === 'lunar-eclipse') && positive(value.remaining) && value.remaining <= 5 && wager(value.wagerPoints)

export function isSunstoneState(value: unknown): value is SunstoneState & Record<string, unknown> {
  return record(value) && natural(value.freeSpinsRemaining) &&
    (value.freeSpinWagerPoints === null || positive(value.freeSpinWagerPoints)) &&
    (value.freeSpinsRemaining === 0 || positive(value.freeSpinWagerPoints)) &&
    (value.freeSpinFeatureMode === null || typeof value.freeSpinFeatureMode === 'string' && value.freeSpinFeatureMode.split('-').every(part => ['sync', 'rows', 'paw', 'rand', 'arcade'].includes(part))) &&
    (value.paidMode == null || mode(value.paidMode) && value.freeSpinsRemaining === 0) &&
    (value.slotsCreditsBalance === null || money(value.slotsCreditsBalance)) &&
    (value.pendingOperation == null || isPreparedSlotSpin(value.pendingOperation))
}
export function isSunstoneResult(value: unknown): value is SunstoneResult {
  if (!record(value) || !isSunstoneState(value) || value.gameId !== FIFTH_SUN_GAME_ID ||
    typeof value.spinId !== 'string' || !value.spinId || !positive(value.wagerPoints) || typeof value.isFreeSpin !== 'boolean' ||
    value.pointValueInCents !== 25 || !money(value.slotsCreditsBalance) || !record(value.payout) || !natural(value.payout.totalPoints)) return false
  const rows = Array.isArray(value.reels) && Array.isArray(value.reels[0]) ? value.reels[0].length : 0
  if (!(rows === 4 || value.isFreeSpin && rows === 6) ||
    !grid(value.reels, 5, rows, symbol => typeof symbol === 'string' && Object.hasOwn(FIFTH_SUN_SYMBOL_SET.definitions, symbol)) ||
    !paylines(value.payout.paylines, value.reels, value.payout.totalPoints)) return false
  if (value.spinFeature == null) return value.isFreeSpin || value.symbolSetId === 'classic-symbols-v3'
  const feature = decodeSunwheelFeature(value.spinFeature)
  return feature !== null && feature.rows === rows && feature.heart.symbolId === value.reels[feature.heart.reel][feature.heart.row] &&
    feature.totalAwardPoints <= value.payout.totalPoints
}
export class SunstoneRequestError extends Error {
  constructor(readonly status: number, readonly problem: Record<string, unknown>) {
    super(typeof problem.error === 'string' ? problem.error : `Sunstone service returned HTTP ${status}.`)
  }
}

/** Paid intent and exact-operation recovery belong to Sunstone; replies never authorize wallet changes. */
export function createSunstoneApi(fetch: SunstoneFetch, accountId: string) {
  let prepared = false
  const pending = () => store.read(accountId)
  async function json(response: Response): Promise<unknown> {
    const value: unknown = await response.json().catch(() => null)
    if (!response.ok) throw new SunstoneRequestError(response.status, record(value) ? value : {})
    return value
  }
  async function submit(operation: PreparedSlotSpin): Promise<SunstoneResult> {
    if (!prepared) throw Error('Reconnect before recovering the saved Sunstone round.')
    try {
      const value = await json(await fetch(preparedSlotSpinPath, { method: 'POST',
        headers: { ...preparedSlotSpinHeaders(accountId, operation.operationId), 'Content-Type': 'application/json' },
        body: JSON.stringify({ gameId: FIFTH_SUN_GAME_ID, wagerPoints: operation.wagerPoints, useFreeSpin: operation.useFreeSpin, useSpecialBoost: operation.useSpecialBoost }) }))
      if (!isSunstoneResult(value) || value.wagerPoints !== operation.wagerPoints || value.isFreeSpin !== operation.useFreeSpin)
        throw Error('The Sunstone reply is invalid or mismatched. Recover the saved round before playing again.')
      store.forget(accountId, operation.operationId)
      return value
    } catch (error) {
      if (error instanceof SunstoneRequestError) {
        if (isPreparedSlotSpin(error.problem.pendingOperation)) store.remember(accountId, error.problem.pendingOperation)
        else if (error.status === 400 || error.problem.code === 'slot-prepared-start-paused' ||
          error.status === 409 && error.problem.preparedOperationPending !== true &&
          ['insufficient-slot-credits', 'free-spins-required', 'free-spins-unavailable', 'free-spin-wager-locked', 'paid-mode-wager-locked', 'legacy-free-spin-wager-unplayable'].includes(String(error.problem.code)))
          store.forget(accountId, operation.operationId)
      }
      throw error
    }
  }
  return {
    pending,
    async state(signal?: AbortSignal): Promise<SunstoneState> {
      const response = await fetch(`/api/slots/state?gameId=${FIFTH_SUN_GAME_ID}`, { method: 'GET',
        headers: preparedSlotSpinHeaders(accountId), cache: 'no-store', signal })
      const value = await json(response)
      if (!isSunstoneState(value)) throw Error('The server returned invalid Sunstone progress.')
      prepared = response.headers.get('X-FortuneForge-Prepared-Spin') === '1'
      if (value.pendingOperation) store.remember(accountId, value.pendingOperation)
      return value
    },
    async spin(wagerPoints: number, useFreeSpin: boolean) {
      if (pending()) throw Error('Recover the saved round before placing another wager.')
      if (!(useFreeSpin ? positive(wagerPoints) : wager(wagerPoints))) throw Error('Choose a Sunstone wager from R3 to R75.')
      if (!prepared) throw Error('Paid Sunstone play is unavailable. Reconnect to the game server.')
      const operation = { operationId: crypto.randomUUID(), wagerPoints, useFreeSpin, useSpecialBoost: false }
      store.remember(accountId, operation)
      return submit(operation)
    },
    async resume() { const operation = pending(); if (!operation) throw Error('No saved Sunstone round.'); return submit(operation) },
  }
}
