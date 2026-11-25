import type { PaylinePayout } from '@fortuneforge/slot-client-core'
import { createPreparedSlotSpinStore, isPreparedSlotSpin, preparedSlotSpinHeaders, preparedSlotSpinPath, type PreparedSlotSpin } from '@fortuneforge/slot-client-core/preparedSpin'
import { record, natural, positive, money, grid, paylines } from '@fortuneforge/slot-client-core/contracts/presentationValidation'
import { PIRATES_FORTUNE_SYMBOLS } from './symbols'

export const PIRATES_GAME_ID = 'pirates-fortune-v1'
export type PiratesFetch = (path: string, init?: RequestInit) => Promise<Response>
export type PiratesState = {
  freeSpinsRemaining: number; freeSpinWagerPoints: number | null; freeSpinFeatureMode: string | null
  specialPointsBalance: number; sealCollections: { sealId: string; count: number; requiredCount: number; averageWagerPoints: number }[]
  slotsCreditsBalance: number | null; pendingOperation?: PreparedSlotSpin | null
}
export type PiratesResult = PiratesState & {
  spinId: string; gameId: string; wagerPoints: number; isFreeSpin: boolean; pointValueInCents: number
  reels: string[][]; payout: { totalPoints: number; paylines: PaylinePayout[] }
  moneyGrabPoints: number; bananaBonusPoints: number; freeSpinsAwarded: number; collectionFreeSpinsAwarded: number
  sealsAwarded: Record<string, number>; slotsCreditsBalance: number
}
const store = createPreparedSlotSpinStore('pirates-fortune')
const tracks = ['sync', 'rows', 'paw', 'rand']
const symbol = (value: unknown) => typeof value === 'string' && Object.hasOwn(PIRATES_FORTUNE_SYMBOLS.definitions, value)
export function isPiratesState(value: unknown): value is PiratesState & Record<string, unknown> {
  return record(value) && natural(value.freeSpinsRemaining) && (value.freeSpinWagerPoints === null || positive(value.freeSpinWagerPoints)) &&
    (value.freeSpinsRemaining === 0 || positive(value.freeSpinWagerPoints)) &&
    (value.freeSpinFeatureMode === null || typeof value.freeSpinFeatureMode === 'string' && value.freeSpinFeatureMode.split('-').every(mode => tracks.includes(mode))) &&
    natural(value.specialPointsBalance) && (value.slotsCreditsBalance === null || money(value.slotsCreditsBalance)) &&
    Array.isArray(value.sealCollections) && value.sealCollections.length === 4 &&
    new Set(value.sealCollections.map(track => record(track) ? track.sealId : null)).size === 4 &&
    value.sealCollections.every(track => record(track) && tracks.includes(String(track.sealId)) && natural(track.count) &&
      positive(track.requiredCount) && track.count <= track.requiredCount && natural(track.averageWagerPoints)) &&
    (value.pendingOperation == null || isPreparedSlotSpin(value.pendingOperation))
}
export function isPiratesResult(value: unknown): value is PiratesResult {
  if (!record(value) || !isPiratesState(value) || value.gameId !== PIRATES_GAME_ID || typeof value.spinId !== 'string' || !value.spinId ||
    !positive(value.wagerPoints) || typeof value.isFreeSpin !== 'boolean' || value.pointValueInCents !== 25 || !money(value.slotsCreditsBalance) ||
    !natural(value.freeSpinsAwarded) || !natural(value.collectionFreeSpinsAwarded) || !natural(value.moneyGrabPoints) || !natural(value.bananaBonusPoints) ||
    !record(value.sealsAwarded) || !Object.values(value.sealsAwarded).every(natural) || !record(value.payout) || !natural(value.payout.totalPoints)) return false
  const rows = Array.isArray(value.reels) && Array.isArray(value.reels[0]) ? value.reels[0].length : 0
  return (rows === 4 || value.isFreeSpin && rows === 6) && grid(value.reels, 5, rows, symbol) &&
    paylines(value.payout.paylines, value.reels, value.payout.totalPoints) &&
    value.moneyGrabPoints <= value.payout.totalPoints && value.bananaBonusPoints <= value.payout.totalPoints
}
export class PiratesRequestError extends Error {
  constructor(readonly status: number, readonly problem: Record<string, unknown>) {
    super(typeof problem.error === 'string' ? problem.error : `Pirates service returned HTTP ${status}.`)
  }
}

/** The package owns command construction, reply validation and exact-operation recovery. */
export function createPiratesApi(fetch: PiratesFetch, accountId: string) {
  let prepared = false
  const pending = () => store.read(accountId)
  async function json(response: Response): Promise<unknown> {
    const value: unknown = await response.json().catch(() => null)
    if (!response.ok) throw new PiratesRequestError(response.status, record(value) ? value : {})
    return value
  }
  async function submit(operation: PreparedSlotSpin): Promise<PiratesResult> {
    if (!prepared) throw Error('The saved spin is waiting for the Pirates server. Reconnect to continue.')
    try {
      const value = await json(await fetch(preparedSlotSpinPath, { method: 'POST', headers: { ...preparedSlotSpinHeaders(accountId, operation.operationId), 'Content-Type': 'application/json' },
        body: JSON.stringify({ gameId: PIRATES_GAME_ID, wagerPoints: operation.wagerPoints, useFreeSpin: operation.useFreeSpin, useSpecialBoost: operation.useSpecialBoost }) }))
      if (!isPiratesResult(value) || value.wagerPoints !== operation.wagerPoints || value.isFreeSpin !== operation.useFreeSpin)
        throw Error('The server returned an invalid or mismatched Pirates result. Reconnect to recover the saved round.')
      store.forget(accountId, operation.operationId)
      return value
    } catch (error) {
      if (error instanceof PiratesRequestError) {
        if (isPreparedSlotSpin(error.problem.pendingOperation)) store.remember(accountId, error.problem.pendingOperation)
        else if (error.status === 400 || error.status === 409 && error.problem.preparedOperationPending !== true &&
          ['insufficient-slot-credits', 'free-spins-required', 'free-spins-unavailable', 'free-spin-wager-locked', 'insufficient-special-points', 'legacy-free-spin-wager-unplayable'].includes(String(error.problem.code)) ||
          error.problem.code === 'slot-prepared-start-paused') store.forget(accountId, operation.operationId)
      }
      throw error
    }
  }
  return {
    pending,
    async state(signal?: AbortSignal): Promise<PiratesState> {
      const response = await fetch(`/api/slots/state?gameId=${PIRATES_GAME_ID}`, { method: 'GET', headers: preparedSlotSpinHeaders(accountId), cache: 'no-store', signal })
      const value = await json(response)
      if (!isPiratesState(value)) throw Error('The server returned invalid Pirates progress.')
      prepared = response.headers.get('X-FortuneForge-Prepared-Spin') === '1'
      if (value.pendingOperation) store.remember(accountId, value.pendingOperation)
      return value
    },
    async spin(wagerPoints: number, useFreeSpin: boolean, useSpecialBoost = false) {
      if (pending()) throw Error('Resume the saved spin before placing another wager.')
      if (!positive(wagerPoints) || wagerPoints < 2 || !useFreeSpin && (wagerPoints > 2000 || wagerPoints % 2 !== 0) || useFreeSpin && useSpecialBoost)
        throw Error('Choose an available Pirates wager.')
      if (!prepared) throw Error('Paid Pirates play is unavailable. Reconnect to the game server.')
      const operation = { operationId: crypto.randomUUID(), wagerPoints, useFreeSpin, useSpecialBoost }
      store.remember(accountId, operation)
      return submit(operation)
    },
    async resume() { const operation = pending(); if (!operation) throw Error('No saved Pirates spin.'); return submit(operation) },
  }
}
