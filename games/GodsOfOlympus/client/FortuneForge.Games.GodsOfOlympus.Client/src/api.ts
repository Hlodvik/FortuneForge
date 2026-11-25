import type { PaylinePayout } from '@fortuneforge/slot-client-core'
import { createPreparedSlotSpinStore, isPreparedSlotSpin, preparedSlotSpinHeaders, preparedSlotSpinPath, type PreparedSlotSpin } from '@fortuneforge/slot-client-core/preparedSpin'
import { record, natural, positive, money, grid, paylines, position } from '@fortuneforge/slot-client-core/contracts/presentationValidation'
import { GODS_OF_OLYMPUS_SYMBOLS } from './symbols'
import { GODS_TUMBLE_SYMBOLS, type GodsTumbleResult } from './tumble'

export const OLYMPUS_GAME_ID = 'gods-of-olympus-v1'
export type OlympusFetch = (path: string, init?: RequestInit) => Promise<Response>
export type OlympusState = {
  freeSpinsRemaining: number; freeSpinWagerPoints: number | null; freeSpinFeatureMode: string | null
  specialPointsBalance: number; sealCollections: { sealId: string; count: number; requiredCount: number; averageWagerPoints: number }[]
  slotsCreditsBalance: number | null; pendingOperation?: PreparedSlotSpin | null
}
export type OlympusResult = OlympusState & {
  olympusTumble?: GodsTumbleResult; spinId: string; gameId: string; wagerPoints: number; isFreeSpin: boolean; pointValueInCents: number
  reels: string[][]; payout: { totalPoints: number; paylines: PaylinePayout[] }
  moneyGrabPoints: number; bananaBonusPoints: number; freeSpinsAwarded: number; collectionFreeSpinsAwarded: number
  sealsAwarded: Record<string, number>; slotsCreditsBalance: number
}
const store = createPreparedSlotSpinStore('gods-of-olympus')
const tracks = ['sync', 'rows', 'paw', 'rand']
const symbol = (value: unknown) => typeof value === 'string' && Object.hasOwn(GODS_OF_OLYMPUS_SYMBOLS.definitions, value)
export function isOlympusState(value: unknown): value is OlympusState & Record<string, unknown> {
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
function isTumble(value: unknown, initial: string[][], total: number): value is GodsTumbleResult {
  if (!record(value) || !grid(value.initialReels, 6, 5, cell => typeof cell === 'string' && GODS_TUMBLE_SYMBOLS.includes(cell)) ||
    JSON.stringify(value.initialReels) !== JSON.stringify(initial) || !Array.isArray(value.steps) || value.steps.length > 12 || value.totalPoints !== total) return false
  let previous = initial, sum = 0
  for (const step of value.steps) {
    if (!record(step) || !grid(step.reels, 6, 5, cell => typeof cell === 'string' && GODS_TUMBLE_SYMBOLS.includes(cell)) ||
      !grid<number>(step.dropRows, 6, 5, cell => natural(cell) && cell <= 5) || !positive(step.amountPoints) ||
      !Array.isArray(step.awards) || step.awards.length === 0 || !Array.isArray(step.cleared)) return false
    const winners = new Set<string>()
    let awarded = 0
    for (const award of step.awards) {
      if (!record(award) || typeof award.symbol !== 'string' || !GODS_TUMBLE_SYMBOLS.includes(award.symbol) || award.symbol.startsWith('X') ||
        !positive(award.amountPoints) || !Array.isArray(award.positions) || award.positions.length < 8) return false
      awarded += award.amountPoints
      for (const cell of award.positions) {
        if (!position(cell, previous) || previous[cell.reel][cell.row] !== award.symbol || winners.has(`${cell.reel}:${cell.row}`)) return false
        winners.add(`${cell.reel}:${cell.row}`)
      }
    }
    if (awarded !== step.amountPoints || step.cleared.length !== winners.size) return false
    const cleared = new Set<string>()
    for (const cell of step.cleared) {
      if (!position(cell, previous) || !winners.has(`${cell.reel}:${cell.row}`) || cleared.has(`${cell.reel}:${cell.row}`)) return false
      cleared.add(`${cell.reel}:${cell.row}`)
    }
    for (let column = 0; column < 6; column++) {
      const survivors = previous[column].map((symbol, row) => ({ symbol, row })).filter(cell => !cleared.has(`${column}:${cell.row}`))
      const gaps = 5 - survivors.length
      for (let row = 0; row < 5; row++) {
        const drop = step.dropRows[column][row]
        if (row < gaps ? drop !== gaps : drop !== row - survivors[row - gaps].row || step.reels[column][row] !== survivors[row - gaps].symbol) return false
      }
    }
    sum += step.amountPoints; previous = step.reels
  }
  return value.baseTotalPoints == null && value.multiplier == null ? sum === total
    : value.baseTotalPoints === sum && positive(value.multiplier) && value.multiplier <= 25 && sum * value.multiplier === total
}
export function isOlympusResult(value: unknown): value is OlympusResult {
  if (!record(value) || !isOlympusState(value) || value.gameId !== OLYMPUS_GAME_ID || typeof value.spinId !== 'string' || !value.spinId ||
    !positive(value.wagerPoints) || typeof value.isFreeSpin !== 'boolean' || value.pointValueInCents !== 25 || !money(value.slotsCreditsBalance) ||
    !natural(value.freeSpinsAwarded) || !record(value.payout) || !natural(value.payout.totalPoints)) return false
  if (value.olympusTumble != null) return !value.isFreeSpin && grid(value.reels, 6, 5, symbol) &&
    Array.isArray(value.payout.paylines) && value.payout.paylines.length === 0 && isTumble(value.olympusTumble, value.reels, value.payout.totalPoints)
  const rows = Array.isArray(value.reels) && Array.isArray(value.reels[0]) ? value.reels[0].length : 0
  return value.isFreeSpin && (rows === 4 || rows === 6) && grid(value.reels, 5, rows, symbol) && paylines(value.payout.paylines, value.reels, value.payout.totalPoints)
}
export class OlympusRequestError extends Error {
  constructor(readonly status: number, readonly problem: Record<string, unknown>) {
    super(typeof problem.error === 'string' ? problem.error : `Olympus service returned HTTP ${status}.`)
  }
}

/** The package owns command construction, reply validation and exact-operation recovery. */
export function createOlympusApi(fetch: OlympusFetch, accountId: string) {
  let prepared = false
  const pending = () => store.read(accountId)
  async function json(response: Response): Promise<unknown> {
    const value: unknown = await response.json().catch(() => null)
    if (!response.ok) throw new OlympusRequestError(response.status, record(value) ? value : {})
    return value
  }
  async function submit(operation: PreparedSlotSpin): Promise<OlympusResult> {
    if (!prepared) throw Error('The saved spin is waiting for the Olympus server. Reconnect to continue.')
    try {
      const value = await json(await fetch(preparedSlotSpinPath, { method: 'POST', headers: { ...preparedSlotSpinHeaders(accountId, operation.operationId), 'Content-Type': 'application/json' },
        body: JSON.stringify({ gameId: OLYMPUS_GAME_ID, wagerPoints: operation.wagerPoints, useFreeSpin: operation.useFreeSpin, useSpecialBoost: operation.useSpecialBoost }) }))
      if (!isOlympusResult(value) || value.wagerPoints !== operation.wagerPoints || value.isFreeSpin !== operation.useFreeSpin)
        throw Error('The server returned an invalid or mismatched Olympus result. Reconnect to recover the saved round.')
      store.forget(accountId, operation.operationId)
      return value
    } catch (error) {
      if (error instanceof OlympusRequestError) {
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
    async state(signal?: AbortSignal): Promise<OlympusState> {
      const response = await fetch(`/api/slots/state?gameId=${OLYMPUS_GAME_ID}`, { method: 'GET', headers: preparedSlotSpinHeaders(accountId), cache: 'no-store', signal })
      const value = await json(response)
      if (!isOlympusState(value)) throw Error('The server returned invalid Olympus progress.')
      prepared = response.headers.get('X-FortuneForge-Prepared-Spin') === '1'
      if (value.pendingOperation) store.remember(accountId, value.pendingOperation)
      return value
    },
    async spin(wagerPoints: number, useFreeSpin: boolean, useSpecialBoost = false) {
      if (pending()) throw Error('Resume the saved spin before placing another wager.')
      if (!useFreeSpin && useSpecialBoost || !positive(wagerPoints) || wagerPoints < 2 || !useFreeSpin && (wagerPoints > 2000 || wagerPoints % 2 !== 0) || useFreeSpin && useSpecialBoost)
        throw Error('Choose an available Olympus wager.')
      if (!prepared) throw Error('Paid Olympus play is unavailable. Reconnect to the game server.')
      const operation = { operationId: crypto.randomUUID(), wagerPoints, useFreeSpin, useSpecialBoost }
      store.remember(accountId, operation)
      return submit(operation)
    },
    async resume() { const operation = pending(); if (!operation) throw Error('No saved Olympus spin.'); return submit(operation) },
  }
}
