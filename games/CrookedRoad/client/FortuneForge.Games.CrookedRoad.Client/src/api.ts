import { createPreparedSlotSpinStore, isPreparedSlotSpin, preparedSlotSpinHeaders, preparedSlotSpinPath, type PreparedSlotSpin } from '@fortuneforge/slot-client-core/preparedSpin'
import { decodeRoadFeature, decodeRoadState, roadPaths, type RoadAction, type RoadBonus } from './road.js'
import { CROOKED_ROAD_GAME_ID, CROOKED_ROAD_REEL_SET_ID, CROOKED_ROAD_RULES as rules, CROOKED_ROAD_SYMBOL_SET_ID, type CrookedRoadAuthenticatedFetch, type CrookedRoadPayline, type CrookedRoadPosition, type CrookedRoadReels, type CrookedRoadSpinResult, type CrookedRoadState } from './types.js'

const preparedSpins = createPreparedSlotSpinStore('crooked-road')
const statePath = `/api/slots/state?gameId=${CROOKED_ROAD_GAME_ID}`
const actionPath = '/api/slots/crooked-road/actions'
const record = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === 'object' && !Array.isArray(value)
const natural = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value >= 0
const wager = (value: unknown, funded: boolean): value is number => natural(value) && value >= rules.minimumWagerPoints &&
  value <= rules.maximumWagerPoints && (funded || (value - rules.minimumWagerPoints) % rules.wagerIncrementPoints === 0)
const pendingSpin = (value: unknown): value is PreparedSlotSpin => isPreparedSlotSpin(value) && !value.useSpecialBoost && wager(value.wagerPoints, value.useFreeSpin)
const sameBoard = (a: readonly (readonly string[])[], b: readonly (readonly string[])[]) => a.length === b.length &&
  a.every((reel, column) => reel.length === b[column]?.length && reel.every((symbol, row) => symbol === b[column]?.[row]))
const board = (value: unknown): value is CrookedRoadReels => Array.isArray(value) && value.length === 5 && value.every(reel =>
  Array.isArray(reel) && reel.length === 3 && reel.every(symbol => rules.symbols.some(candidate => candidate.id === symbol)))
const money = (value: unknown): value is number => typeof value === 'number' && Number.isFinite(value) && value >= 0 &&
  Number.isSafeInteger(Math.round(value * 100)) && Math.abs(value * 100 - Math.round(value * 100)) < 0.000001

type Problem = Record<string, unknown>
export class CrookedRoadRequestError extends Error {
  readonly status: number
  readonly code?: string
  readonly available?: number
  readonly required?: number
  readonly retryAfterMilliseconds?: number
  readonly pendingOperation?: PreparedSlotSpin
  readonly preparedOperationPending: boolean
  constructor(message: string, status: number, problem: Problem | null = null) {
    super(message)
    this.name = 'CrookedRoadRequestError'
    this.status = status
    this.code = typeof problem?.code === 'string' ? problem.code : undefined
    this.available = money(problem?.available) ? problem.available : undefined
    this.required = money(problem?.required) ? problem.required : undefined
    this.retryAfterMilliseconds = natural(problem?.retryAfterMilliseconds) ? problem.retryAfterMilliseconds : undefined
    this.pendingOperation = pendingSpin(problem?.pendingOperation) ? problem.pendingOperation : undefined
    this.preparedOperationPending = problem?.preparedOperationPending === true || this.pendingOperation !== undefined
  }
}
const malformed = () => new CrookedRoadRequestError('The game reply could not be verified. Retry the same play to recover its result.', 502, { code: 'invalid-crooked-road-reply', preparedOperationPending: true })

function validBonus(value: RoadBonus, event: boolean, completed = false): boolean {
  if (!wager(value.fundedWagerPoints, true) || value.id.length > 128 || value.step >= value.totalSteps && !completed ||
      event && value.step === 0 || value.kind !== 'fort' && completed !== (event && value.step === 3)) return false
  if (value.kind === 'well') return value.held === null && value.respinsRemaining === 0 && value.doors.length === 0 &&
    (value.step === 0 ? value.board === null : value.board !== null && value.choice !== null)
  if (value.kind === 'fort') return value.board === null && value.choice === null && value.doors.length === 0 &&
    value.held !== null && value.held.every(coin => coin === 0 || rules.fortCoinValues.includes(coin)) &&
    (completed ? value.respinsRemaining === 0 : value.respinsRemaining > 0 && value.held.some(coin => coin === 0) && value.awardPoints === 0)
  return value.board === null && value.held === null && value.respinsRemaining === 0 && value.doors.length === 3 &&
    value.doors.every((door, index) => door.index === index && (event
      ? door.selected === door.revealed && (door.revealed ? natural(door.prizePoints) : door.prizePoints === null)
      : !door.revealed && door.prizePoints === null && door.selected === (value.choice === index))) &&
    (!event || value.choice === null && value.doors.filter(door => door.revealed).length === 1)
}

/** Checks a native public snapshot. It never supplies missing authoritative state. */
export function decodeCrookedRoadState(value: unknown): CrookedRoadState | null {
  if (!record(value)) return null
  const road = decodeRoadState(value.gameFeatureState)
  if (!road || typeof value.featureActionRequired !== 'boolean' || !natural(value.freeSpinsRemaining) ||
      value.slotsCreditsBalance !== null && !money(value.slotsCreditsBalance) ||
      value.pendingOperation !== undefined && value.pendingOperation !== null && !pendingSpin(value.pendingOperation)) return null
  const bonus = road.activeBonus
  if (road.funding.wagerPointsTotal < road.funding.spins * rules.minimumWagerPoints ||
      road.funding.wagerPointsTotal > road.funding.spins * rules.maximumWagerPoints ||
      road.tickets.some(ticket => ticket.id.length > 128 || !wager(ticket.fundedWagerPoints, true)) ||
      bonus && (!validBonus(bonus, false) || road.tickets.some(ticket => ticket.id === bonus.id))) return null
  const reachable = Object.keys(roadPaths(road.board))
  if (reachable.length !== road.reachableDestinations.length || !reachable.every(id => road.reachableDestinations.includes(id as 'well' | 'fort' | 'hill'))) return null
  const required = !!bonus && (bonus.kind === 'well' || bonus.kind === 'hill') && bonus.choice === null
  const remaining = bonus ? bonus.kind === 'fort' ? bonus.respinsRemaining : bonus.totalSteps - bonus.step : 0
  if (value.featureActionRequired !== required || value.freeSpinsRemaining !== remaining ||
      value.freeSpinWagerPoints !== (bonus?.fundedWagerPoints ?? null) || value.freeSpinFeatureMode !== (bonus?.kind ?? null)) return null
  if (value.specialPointsBalance !== 0 || value.energyBalance !== 0 || value.paidMode !== null ||
      !Array.isArray(value.sealCollections) || value.sealCollections.length !== 0) return null
  return value as unknown as CrookedRoadState
}

const samePositions = (value: unknown, expected: readonly CrookedRoadPosition[]): boolean => Array.isArray(value) &&
  value.length === expected.length && value.every((cell, index) => record(cell) && cell.reel === expected[index]!.reel && cell.row === expected[index]!.row)

// Display verification follows the single JSON contract; settlement stays wholly server-owned.
function expectedLines(reels: CrookedRoadReels, wagerPoints: number): CrookedRoadPayline[] {
  return rules.paylines.flatMap((route, index) => {
    let best = { units: 0, symbol: '2', length: 0 }
    for (const symbol of rules.symbols) {
      if (symbol.pays.length !== 3) continue
      let length = 0
      while (length < 5 && (reels[length]![route[length]!] === symbol.id || reels[length]![route[length]!] === 'ACE')) length++
      const units = length >= 3 ? symbol.pays[length - 3]! : 0
      if (units > best.units) best = { units, symbol: symbol.id, length }
    }
    const amountPoints = Math.floor(wagerPoints * best.units * rules.payoutScaleNumerator / (rules.payoutUnits * rules.payoutScaleDenominator))
    if (!amountPoints) return []
    const positions = Array.from({ length: best.length }, (_, reel) => ({ reel, row: route[reel]! }))
    return [{ paylineId: index + 1, amountPoints, matches: [{ amountPoints, multiplier: best.units,
      match: { paylineId: index + 1, symbolId: best.symbol as CrookedRoadPayline['matches'][number]['match']['symbolId'], matchLength: best.length,
        positions, wildPositions: positions.filter(cell => reels[cell.reel]![cell.row] === 'ACE') } }] }]
  })
}
function lineMatches(value: unknown, expected: CrookedRoadPayline): boolean {
  if (!record(value) || value.paylineId !== expected.paylineId || value.amountPoints !== expected.amountPoints ||
      !Array.isArray(value.matches) || value.matches.length !== 1) return false
  const paid = value.matches[0], wanted = expected.matches[0]!
  return record(paid) && paid.amountPoints === wanted.amountPoints && paid.multiplier === wanted.multiplier && record(paid.match) &&
    paid.match.paylineId === wanted.match.paylineId && paid.match.symbolId === wanted.match.symbolId &&
    paid.match.matchLength === wanted.match.matchLength && samePositions(paid.match.positions, wanted.match.positions) &&
    samePositions(paid.match.wildPositions, wanted.match.wildPositions)
}

export function decodeCrookedRoadSpinResult(value: unknown): CrookedRoadSpinResult | null {
  const state = decodeCrookedRoadState(value)
  if (!state || !record(value) || typeof value.spinId !== 'string' || !/^[a-f\d]{8}-[a-f\d]{4}-[a-f\d]{4}-[a-f\d]{4}-[a-f\d]{12}$/i.test(value.spinId) ||
      value.gameId !== CROOKED_ROAD_GAME_ID || value.reelSetId !== CROOKED_ROAD_REEL_SET_ID || value.symbolSetId !== CROOKED_ROAD_SYMBOL_SET_ID ||
      value.paytableId !== rules.modelId || value.pointValueInCents !== rules.pointValueInCents || typeof value.isFreeSpin !== 'boolean' ||
      !wager(value.wagerPoints, value.isFreeSpin) || value.chargedWagerCents !== (value.isFreeSpin ? 0 : value.wagerPoints * rules.pointValueInCents) ||
      !board(value.reels) || !Array.isArray(value.reelStops) || value.reelStops.length !== 5 || !value.reelStops.every(stop => stop === 0) ||
      !record(value.payout) || !natural(value.payout.totalPoints) || !Array.isArray(value.payout.paylines)) return null
  if (['consecutiveFiveMisses', 'freeSpinsAwarded', 'collectionFreeSpinsAwarded', 'specialPointsAwarded', 'energyAwarded', 'monkeyPawCount', 'moneyGrabPoints', 'bananaBonusPoints'].some(key => value[key] !== 0) ||
      ['fiveMatchPityTriggered', 'energyMultiplierApplied', 'specialBoostApplied'].some(key => value[key] !== false) ||
      value.payoutMultiplier !== 1 || !record(value.sealsAwarded) || Object.keys(value.sealsAwarded).length !== 0) return null
  if (!value.isFreeSpin) {
    if (value.spinFeature !== null || state.gameFeatureState.activeBonus !== null) return null
    const lines = expectedLines(value.reels, value.wagerPoints)
    if (value.payout.totalPoints !== lines.reduce((sum, line) => sum + line.amountPoints, 0) ||
        value.payout.paylines.length !== lines.length || !value.payout.paylines.every((line, index) => lineMatches(line, lines[index]!))) return null
  } else {
    const event = decodeRoadFeature(value.spinFeature)
    if (!event || !validBonus(event.bonus, true, event.completed) || event.bonus.fundedWagerPoints !== value.wagerPoints || value.payout.paylines.length !== 0 ||
        state.gameFeatureState.lastTravel?.destination !== event.kind) return null
    const active = state.gameFeatureState.activeBonus
    if (event.completed ? active !== null : !active || active.id !== event.bonus.id || active.kind !== event.kind ||
        active.step !== event.bonus.step || active.awardPoints !== event.bonus.awardPoints || active.fundedWagerPoints !== event.bonus.fundedWagerPoints) return null
    if (event.bonus.awardPoints < value.payout.totalPoints || event.bonus.step === 1 && event.bonus.awardPoints !== value.payout.totalPoints ||
        event.kind === 'well' && (!event.bonus.board || !sameBoard(event.bonus.board, value.reels)) ||
        event.kind === 'fort' && (event.bonus.awardPoints !== value.payout.totalPoints || !value.reels.every(reel => reel.every(symbol => symbol === 'POWER'))) ||
        event.kind === 'hill' && (event.bonus.doors.find(door => door.revealed)?.prizePoints !== value.payout.totalPoints || !value.reels.every(reel => reel.every(symbol => symbol === 'POWER')))) return null
  }
  return value as unknown as CrookedRoadSpinResult
}

async function json(response: Response): Promise<unknown> { try { return await response.json() } catch { return null } }
function problemError(response: Response, value: unknown): CrookedRoadRequestError {
  const problem = record(value) ? value : null
  const message = typeof problem?.error === 'string' ? problem.error : typeof problem?.detail === 'string' ? problem.detail : `The game request failed (${response.status}).`
  return new CrookedRoadRequestError(message, response.status, problem)
}
const definitelyUnadmitted = new Set(['slot-prepared-start-paused', 'free-spins-required', 'free-spins-unavailable', 'free-spin-wager-locked', 'feature-action-required', 'road-ticket-bank-full'])

function actionBody(intent: RoadAction): RoadAction {
  const invalid = () => new CrookedRoadRequestError('Choose an available road action and refresh its current state.', 400, { code: 'invalid-road-action' })
  if (!natural(intent.expectedRevision)) throw invalid()
  const base = { expectedRevision: intent.expectedRevision, action: intent.action }
  if (intent.action === 'set-off') {
    if (!rules.road.destinations.some(destination => destination.id === intent.destination)) throw invalid()
    return { ...base, destination: intent.destination }
  }
  if (intent.action === 'choose-spring' || intent.action === 'choose-door') {
    if (!natural(intent.choice) || intent.choice > 2) throw invalid()
    return { ...base, choice: intent.choice }
  }
  if (!natural(intent.cellIndex) || intent.cellIndex > 8) throw invalid()
  if (intent.action === 'remove') return { ...base, cellIndex: intent.cellIndex }
  if ((intent.action !== 'place' && intent.action !== 'rotate') || !natural(intent.rotation) || intent.rotation > 3) throw invalid()
  if (intent.action === 'rotate') return { ...base, cellIndex: intent.cellIndex, rotation: intent.rotation }
  if (!natural(intent.handIndex) || intent.handIndex > 1) throw invalid()
  return { ...base, handIndex: intent.handIndex, cellIndex: intent.cellIndex, rotation: intent.rotation }
}

/** Paid transport only. A lost reply always retains its exact account-scoped operation. */
export function createCrookedRoadApi(authenticatedFetch?: CrookedRoadAuthenticatedFetch, accountId?: string) {
  let preparedSupported = false
  let inFlight: { id: string; result: Promise<CrookedRoadSpinResult> } | undefined
  let unresolvedAction: { key: string; id: string; revision: number } | undefined
  function requireTransport(): CrookedRoadAuthenticatedFetch {
    if (!authenticatedFetch || !accountId?.trim()) throw new CrookedRoadRequestError('Sign in to play The Crooked Road.', 401, { code: 'authentication-required' })
    return authenticatedFetch
  }
  function pending(): PreparedSlotSpin | undefined {
    if (!accountId) return undefined
    const saved = preparedSpins.read(accountId)
    if (saved && !pendingSpin(saved)) throw new CrookedRoadRequestError('The saved play requires account recovery before another wager.', 409, { code: 'invalid-saved-operation', preparedOperationPending: true })
    return saved
  }
  function rememberServerPending(operation?: PreparedSlotSpin | null) { if (accountId && operation) preparedSpins.remember(accountId, operation) }
  async function getState(signal?: AbortSignal): Promise<CrookedRoadState> {
    const response = await requireTransport()(statePath, { headers: preparedSlotSpinHeaders(accountId), cache: 'no-store', signal })
    const raw = await json(response)
    if (!response.ok) throw problemError(response, raw)
    const state = decodeCrookedRoadState(raw)
    if (!state) throw malformed()
    preparedSupported = response.headers.get('X-FortuneForge-Prepared-Spin') === '1'
    rememberServerPending(state.pendingOperation)
    if (unresolvedAction && state.gameFeatureState.revision > unresolvedAction.revision) unresolvedAction = undefined
    return state
  }
  async function postSpin(operation: PreparedSlotSpin): Promise<CrookedRoadSpinResult> {
    const fetch = requireTransport()
    if (!preparedSupported) await getState()
    if (!preparedSupported) throw new CrookedRoadRequestError('Prepared play recovery is temporarily unavailable. Please retry shortly.', 503, { code: 'prepared-spin-unavailable', preparedOperationPending: !!pending() })
    // Bootstrap may discover an operation admitted by another tab or an earlier mount.
    operation = pending() ?? operation
    preparedSpins.remember(accountId!, operation)
    const response = await fetch(preparedSlotSpinPath, { method: 'POST', headers: { ...preparedSlotSpinHeaders(accountId, operation.operationId), 'Content-Type': 'application/json' },
      body: JSON.stringify({ gameId: CROOKED_ROAD_GAME_ID, wagerPoints: operation.wagerPoints, useFreeSpin: operation.useFreeSpin, useSpecialBoost: false }) })
    const raw = await json(response)
    if (!response.ok) {
      const error = problemError(response, raw)
      if (error.pendingOperation) rememberServerPending(error.pendingOperation)
      else if (!error.preparedOperationPending && (definitelyUnadmitted.has(error.code ?? '') && (response.status === 409 || response.status === 503) ||
        error.code === 'insufficient-slot-credits' && response.status === 409 && record(raw) && raw.preparedOperationPending === false)) preparedSpins.forget(accountId!, operation.operationId)
      throw error
    }
    const result = decodeCrookedRoadSpinResult(raw)
    if (!result || result.wagerPoints !== operation.wagerPoints || result.isFreeSpin !== operation.useFreeSpin) throw malformed()
    preparedSpins.forget(accountId!, operation.operationId)
    return result
  }
  function run(operation: PreparedSlotSpin): Promise<CrookedRoadSpinResult> {
    if (inFlight) return inFlight.result
    const result = postSpin(operation).finally(() => { if (inFlight?.id === operation.operationId) inFlight = undefined })
    inFlight = { id: operation.operationId, result }
    return result
  }
  function spin(wagerPoints: number, useFreeSpin: boolean): Promise<CrookedRoadSpinResult> {
    requireTransport()
    const saved = pending()
    if (saved) return run(saved)
    if (unresolvedAction) throw new CrookedRoadRequestError('Refresh the road to recover its previous action before playing.', 409, { code: 'road-action-pending' })
    if (typeof useFreeSpin !== 'boolean' || !wager(wagerPoints, useFreeSpin)) throw new CrookedRoadRequestError('Choose an available Crooked Road wager.', 400, { code: 'invalid-wager' })
    return run({ operationId: crypto.randomUUID(), wagerPoints, useFreeSpin, useSpecialBoost: false })
  }
  function resumeSpin(): Promise<CrookedRoadSpinResult> {
    requireTransport()
    const saved = pending()
    if (!saved) throw new CrookedRoadRequestError('No prepared play needs recovery.', 409, { code: 'no-pending-operation' })
    return run(saved)
  }
  async function action(intent: RoadAction): Promise<CrookedRoadState> {
    const fetch = requireTransport()
    if (pending() || inFlight) throw new CrookedRoadRequestError('Recover the prepared play before changing the road.', 409, { code: 'road-spin-pending', preparedOperationPending: true })
    const key = JSON.stringify(actionBody(intent))
    if (unresolvedAction && unresolvedAction.key !== key) throw new CrookedRoadRequestError('Refresh the road to recover its previous action.', 409, { code: 'road-action-pending' })
    const operation = unresolvedAction ?? { key, id: crypto.randomUUID(), revision: intent.expectedRevision }
    unresolvedAction = operation
    const response = await fetch(actionPath, { method: 'POST', headers: { ...preparedSlotSpinHeaders(accountId, operation.id), 'Content-Type': 'application/json' }, body: key })
    const raw = await json(response)
    if (!response.ok) {
      const error = problemError(response, raw)
      rememberServerPending(error.pendingOperation)
      if (response.status === 400 || response.status === 409) unresolvedAction = undefined
      throw error
    }
    const state = decodeCrookedRoadState(raw)
    if (!state || state.gameFeatureState.revision !== intent.expectedRevision + 1) throw malformed()
    unresolvedAction = undefined
    rememberServerPending(state.pendingOperation)
    return state
  }
  return { getState, spin, resumeSpin, action, pending }
}
