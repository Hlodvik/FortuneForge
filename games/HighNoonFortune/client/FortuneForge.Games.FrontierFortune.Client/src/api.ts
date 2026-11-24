import type { FrontierCommand, FrontierFetch, FrontierState } from './types.js'
import { FRONTIER_RULES, OUTLAW_RULES } from './rules.js'

const symbols = new Set(FRONTIER_RULES.symbols.map(symbol => symbol.id))
const handSymbols = new Set(OUTLAW_RULES.hand.symbols.map(symbol => symbol.id))
const integer = (value: unknown, max = Number.MAX_SAFE_INTEGER): value is number => Number.isSafeInteger(value) && (value as number) >= 0 && (value as number) <= max
const string = (value: unknown): value is string => typeof value === 'string' && value.length > 0 && value.length <= 160
const rows = (value: unknown): value is (number | null)[] => Array.isArray(value) && value.length === FRONTIER_RULES.columns && value.every(row => row === null || integer(row, FRONTIER_RULES.rows - 1))
const holds = (value: unknown): value is boolean[] => Array.isArray(value) && value.length === FRONTIER_RULES.columns && value.every(held => typeof held === 'boolean')

export class FrontierRequestError extends Error {
  constructor(message: string, public readonly status: number, public readonly state: FrontierState | null = null) { super(message); this.name = 'FrontierRequestError' }
  get definitive() { return this.status >= 400 && this.status < 500 }
}
export function parseFrontierState(value: unknown): FrontierState {
  const state = value as FrontierState
  if ((value as { engineVersion?: string })?.engineVersion === 'frontier-fortune-engine-v1') throw new Error('An earlier frontier round is awaitin’ settlement. Recover it with account support before ridin’ on.')
  const wagering = state?.wagering
  if (!state || state.gameId !== FRONTIER_RULES.gameId || state.engineVersion !== OUTLAW_RULES.engineVersion
    || !integer(state.balanceCents) || !integer(state.revision) || !integer(state.lastWinPoints)
    || !Array.isArray(state.reels) || state.reels.length !== FRONTIER_RULES.columns
    || state.reels.some(column => !Array.isArray(column) || column.length !== FRONTIER_RULES.rows || column.some(symbol => !symbols.has(symbol)))
    || !wagering || !integer(wagering.pointValueInCents) || wagering.pointValueInCents === 0
    || !integer(wagering.minWagerPoints) || wagering.minWagerPoints === 0 || !integer(wagering.maxWagerPoints)
    || wagering.maxWagerPoints < wagering.minWagerPoints || !integer(wagering.stepPoints) || wagering.stepPoints === 0) throw new Error('The frontier sent an unreadable state. Reload before playing.')
  const ante = state.ante
  if (!ante || !integer(ante.requiredChips) || ante.requiredChips !== OUTLAW_RULES.ante.requiredChips
    || !integer(ante.chips) || !integer(ante.wagerPointsTotal) || !integer(ante.fundedWagerPoints)
    || typeof ante.ready !== 'boolean' || ante.ready !== (ante.chips >= ante.requiredChips)) throw new Error('The ante ledger could not be verified. Reload the latest table.')
  const hand = state.handRound ?? null
  if (hand !== null && (!hand || !string(hand.roundId) || !integer(hand.wagerPoints) || hand.wagerPoints < wagering.minWagerPoints
    || !integer(hand.dealsRemaining, OUTLAW_RULES.hand.deals - 1) || !['choose', 'hold', 'complete'].includes(hand.phase)
    || !integer(hand.awardPoints) || hand.category !== null && !Object.hasOwn(OUTLAW_RULES.hand.awards, hand.category)
    || !Array.isArray(hand.hand) || hand.hand.length !== FRONTIER_RULES.columns
    || hand.hand.some(card => !card || typeof card.held !== 'boolean' || card.symbol !== null && !handSymbols.has(card.symbol)))) throw new Error('Your saved hand could not be verified. Reload before dealing.')
  const wins = state.wins ?? []
  if (!Array.isArray(wins) || wins.some(win => !integer(win.line, FRONTIER_RULES.paylines.length) || win.line === 0
    || !symbols.has(win.symbol) || !integer(win.count, FRONTIER_RULES.columns) || win.count < 3 || !integer(win.awardPoints)
    || !Array.isArray(win.cells) || win.cells.some(cell => !integer(cell.column, FRONTIER_RULES.columns - 1) || !integer(cell.row, FRONTIER_RULES.rows - 1)))) throw new Error('The frontier sent an unreadable win report.')
  if (hand?.phase === 'choose' && state.reels.some(column => column.some(symbol => !handSymbols.has(symbol)))) throw new Error('The dealt cards could not be verified. Reload the latest hand.')
  const pending = state.pendingOperation ?? null
  if (pending !== null && (!pending || !string(pending.operationId) || !['spin', 'deal-in', 'deal', 'confirm'].includes(pending.kind)
    || (pending.kind === 'spin' ? !integer(pending.wagerPoints) || (pending.wagerPoints ?? 0) < wagering.minWagerPoints || (pending.wagerPoints ?? 0) > wagering.maxWagerPoints
      || ((pending.wagerPoints ?? 0) - wagering.minWagerPoints) % wagering.stepPoints !== 0
      : !integer(pending.expectedRevision) || (pending.kind !== 'deal-in' && !string(pending.roundId))
        || pending.kind === 'deal' && !holds(pending.held) || pending.kind === 'confirm' && !rows(pending.selectedRows)))) throw new Error('The pending action could not be verified. Reload before playing.')
  return { ...state, handRound: hand, pendingOperation: pending, wins }
}
export async function readFrontier(fetcher: FrontierFetch, signal?: AbortSignal) { return requestState(fetcher, '/api/slots/frontier-fortune/state', { signal }) }
export async function sendFrontier(fetcher: FrontierFetch, command: FrontierCommand) {
  const { operationId, kind, wagerPoints, expectedRevision, roundId, held, selectedRows } = command
  return requestState(fetcher, `/api/slots/frontier-fortune/${kind === 'spin' ? 'spins' : 'hand-actions'}`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(kind === 'spin' ? { operationId, wagerPoints } : { operationId, expectedRevision, kind, roundId, held, selectedRows }),
  })
}
export async function recoverFrontierFailure(cause: unknown, command: FrontierCommand, fetcher: FrontierFetch) {
  const message = cause instanceof Error ? cause.message : 'The trail went quiet. Recover this same action.'
  if (!(cause instanceof FrontierRequestError) || !cause.definitive) return { message, state: null, retryCommand: command, requiresReload: false }
  try { const state = cause.state ?? await readFrontier(fetcher); return { message, state, retryCommand: state.pendingOperation, requiresReload: false } }
  catch { return { message: `${message} Reload the latest table before ridin’ on.`, state: null, retryCommand: null, requiresReload: true } }
}
async function requestState(fetcher: FrontierFetch, path: string, init: RequestInit = {}) {
  const response = await fetcher(path, init)
  const body = await response.json().catch(() => null)
  if (!response.ok || body?.accepted === false) {
    const message = typeof body?.error === 'string' ? body.error : typeof body?.message === 'string' ? body.message : 'The trail went quiet. Recover this same action.'
    let state: FrontierState | null = null
    if (body?.gameId) { try { state = parseFrontierState(body) } catch { /* A partial reply requires a state refresh. */ } }
    throw new FrontierRequestError(message, response.ok ? 409 : response.status, state)
  }
  return parseFrontierState(body)
}
