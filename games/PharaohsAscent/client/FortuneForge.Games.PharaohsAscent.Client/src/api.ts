import { createPreparedSlotSpinStore, isPreparedSlotSpin, preparedSlotSpinHeaders, preparedSlotSpinPath, type PreparedSlotSpin } from '@fortuneforge/slot-client-core/preparedSpin'
import { PHARAOHS_ASCENT_EXPERIENCE_SET as experience } from './manifest'
import { decodePharaohFeature, decodePharaohState } from './pharaoh'
import { decodeTombState, isTombAction, type TombAction } from './tomb'
import { PHARAOH_GAME_ID, type PharaohAuthenticatedFetch, type PharaohSlotState, type PharaohSpinResult } from './types'

const record = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === 'object' && !Array.isArray(value)
const natural = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value >= 0
const money = (value: unknown): value is number => typeof value === 'number' && Number.isFinite(value) && value >= 0 &&
  Number.isSafeInteger(Math.round(value * 100)) && Math.abs(value * 100 - Math.round(value * 100)) < .000001
const fundedWager = (value: unknown): value is number => natural(value) && value > 0
const paidWager = (value: unknown): value is number => fundedWager(value) && experience.rules.wagerOptions.includes(value)
// Saved attempts and confirmed receipts retain their original terms. The server
// decides whether they were prepared under historical admission limits.
const pendingSpin = (value: unknown): value is PreparedSlotSpin => isPreparedSlotSpin(value)
const spins = createPreparedSlotSpinStore(PHARAOH_GAME_ID)
type SavedAction = Readonly<{ id: string; action: TombAction }>
const actions = new Map<string, SavedAction>()
const actionKey = (account: string) => `fortuneforge:${PHARAOH_GAME_ID}-action:${account}`
const actionBody = (action: TombAction): TombAction => ({ action: action.action, expectedRevision: action.expectedRevision,
  ...(action.action === 'open-room' ? { choiceId: action.choiceId } : {}) })

export class PharaohRequestError extends Error {
  readonly code?: string
  readonly pendingOperation?: PreparedSlotSpin
  readonly preparedOperationPending: boolean
  constructor(message: string, readonly status: number, problem: Record<string, unknown> | null = null) {
    super(message)
    this.name = 'PharaohRequestError'
    this.code = typeof problem?.code === 'string' ? problem.code : undefined
    this.pendingOperation = pendingSpin(problem?.pendingOperation) ? problem.pendingOperation : undefined
    this.preparedOperationPending = problem?.preparedOperationPending === true || this.pendingOperation !== undefined
  }
}
const malformed = () => new PharaohRequestError('The game reply could not be verified. Resume the saved play to recover its confirmed result.', 502,
  { code: 'invalid-pharaoh-reply', preparedOperationPending: true })

/** Reject incomplete or contradictory snapshots rather than supplying invented progress. */
export function decodePharaohSlotState(value: unknown): PharaohSlotState | null {
  if (!record(value)) return null
  const feature = decodePharaohState(value.gameFeatureState)
  const tomb = decodeTombState(value.gameFeatureState)
  if (!feature || !tomb || !natural(value.freeSpinsRemaining) ||
    !(value.freeSpinWagerPoints === null || fundedWager(value.freeSpinWagerPoints)) ||
    !(value.freeSpinFeatureMode === null || typeof value.freeSpinFeatureMode === 'string') ||
    !natural(value.specialPointsBalance) || !natural(value.energyBalance) || !Array.isArray(value.sealCollections) ||
    !(value.slotsCreditsBalance === null || money(value.slotsCreditsBalance)) || typeof value.featureActionRequired !== 'boolean' ||
    !(value.pendingOperation === undefined || value.pendingOperation === null || pendingSpin(value.pendingOperation))) return null
  if (value.freeSpinsRemaining !== feature.freeSpinsRemaining || value.freeSpinWagerPoints !== feature.freeSpinWagerPoints ||
    value.freeSpinFeatureMode !== feature.freeSpinFeatureMode || value.energyBalance !== feature.energyBalance ||
    JSON.stringify(value.sealCollections) !== JSON.stringify(feature.collections) ||
    value.featureActionRequired !== Boolean(tomb.activeRun || tomb.lastRun || tomb.pendingAction)) return null
  return value as PharaohSlotState
}

export function decodePharaohSpinResult(value: unknown): PharaohSpinResult | null {
  if (!record(value) || !decodePharaohSlotState(value) || value.gameId !== PHARAOH_GAME_ID ||
    typeof value.spinId !== 'string' || value.spinId.length === 0 || typeof value.reelSetId !== 'string' ||
    value.symbolSetId !== experience.symbols.serverSymbolSetId || typeof value.paytableId !== 'string' ||
    typeof value.isFreeSpin !== 'boolean' || !fundedWager(value.wagerPoints) ||
    value.pointValueInCents !== experience.rules.pointValueInCents || !natural(value.chargedWagerCents) ||
    value.chargedWagerCents !== (value.isFreeSpin ? 0 : value.wagerPoints * experience.rules.pointValueInCents) ||
    !natural(value.freeSpinsAwarded) || !Array.isArray(value.reelStops) || value.reelStops.length !== 5 || !value.reelStops.every(natural) ||
    !Array.isArray(value.reels) || value.reels.length !== 5 || !Array.isArray(value.reels[0])) return null
  const rows = value.reels[0].length
  if (rows !== 4 && rows !== 6 || !value.reels.every(column => Array.isArray(column) && column.length === rows &&
    column.every(symbol => typeof symbol === 'string' && Object.hasOwn(experience.symbols.definitions, symbol))) ||
    !record(value.payout) || !natural(value.payout.totalPoints) || !Array.isArray(value.payout.paylines)) return null
  let total = 0
  for (const line of value.payout.paylines) {
    if (!record(line) || !natural(line.paylineId) || line.paylineId === 0 || !natural(line.amountPoints) ||
      !Array.isArray(line.matches) || line.matches.length === 0) return null
    let lineTotal = 0
    for (const award of line.matches) {
      if (!record(award) || !natural(award.amountPoints) || !record(award.match) || !Array.isArray(award.match.positions) ||
        award.match.positions.length === 0 || !award.match.positions.every(cell => record(cell) && natural(cell.reel) && cell.reel < 5 && natural(cell.row) && cell.row < rows)) return null
      lineTotal += award.amountPoints
    }
    if (lineTotal !== line.amountPoints) return null
    total += line.amountPoints
  }
  if (total !== value.payout.totalPoints || !Number.isSafeInteger(total) ||
    value.spinFeature != null && !decodePharaohFeature(value.spinFeature)) return null
  return { ...value, spinFeature: value.spinFeature ?? null } as unknown as PharaohSpinResult
}

async function json(response: Response): Promise<unknown> { try { return await response.json() } catch { return null } }
function problem(response: Response, value: unknown): PharaohRequestError {
  const data = record(value) ? value : null
  const message = typeof data?.error === 'string' ? data.error : typeof data?.detail === 'string' ? data.detail : `The game request failed (${response.status}).`
  return new PharaohRequestError(message, response.status, data)
}

/** Account-scoped paid operations survive lost replies and never become replacement wagers. */
export function createPharaohApi(authenticatedFetch?: PharaohAuthenticatedFetch, accountId?: string) {
  let preparedSupported = false
  let inFlight: Promise<PharaohSpinResult> | null = null
  let actionFlight: { key: string; result: Promise<PharaohSlotState> } | null = null
  function transport(): PharaohAuthenticatedFetch {
    if (!authenticatedFetch || !accountId?.trim()) throw new PharaohRequestError('Sign in to play Pharaoh’s Ascent.', 401)
    return authenticatedFetch
  }
  function pending(): PreparedSlotSpin | undefined {
    if (!accountId) return undefined
    const saved = spins.read(accountId)
    if (saved && !pendingSpin(saved)) throw new PharaohRequestError('The saved wager needs recovery before another play.', 409,
      { code: 'invalid-saved-operation', preparedOperationPending: true })
    return saved
  }
  function savedAction(): SavedAction | undefined {
    if (!accountId) return undefined
    const cached = actions.get(accountId)
    if (cached) return cached
    try {
      const stored: unknown = JSON.parse(sessionStorage.getItem(actionKey(accountId)) ?? 'null')
      if (record(stored) && typeof stored.id === 'string' && /^[A-Za-z0-9_-]{16,128}$/.test(stored.id) && isTombAction(stored.action)) {
        const saved = stored as SavedAction
        actions.set(accountId, saved)
        return saved
      }
    } catch { /* Private browsing may disable session storage. */ }
    return undefined
  }
  function rememberAction(saved: SavedAction) {
    actions.set(accountId!, saved)
    try { sessionStorage.setItem(actionKey(accountId!), JSON.stringify(saved)) } catch { /* Retain the in-memory operation. */ }
  }
  function forgetAction() {
    actions.delete(accountId!)
    try { sessionStorage.removeItem(actionKey(accountId!)) } catch { /* Storage may be disabled. */ }
  }
  function observe(state: PharaohSlotState) {
    if (state.pendingOperation) spins.remember(accountId!, state.pendingOperation)
    const saved = savedAction()
    if (saved && state.gameFeatureState.tomb.revision > saved.action.expectedRevision) forgetAction()
  }
  async function getState(signal?: AbortSignal): Promise<PharaohSlotState> {
    const response = await transport()(`/api/slots/state?gameId=${PHARAOH_GAME_ID}`, {
      headers: preparedSlotSpinHeaders(accountId), cache: 'no-store', signal,
    })
    const raw = await json(response)
    if (!response.ok) throw problem(response, raw)
    const state = decodePharaohSlotState(raw)
    if (!state) throw malformed()
    preparedSupported = response.headers.get('X-FortuneForge-Prepared-Spin') === '1'
    observe(state)
    return state
  }
  async function postSpin(initial: PreparedSlotSpin): Promise<PharaohSpinResult> {
    const fetch = transport()
    if (!preparedSupported) await getState()
    if (!preparedSupported) throw new PharaohRequestError('Paid play recovery is temporarily unavailable. Please reconnect.', 503,
      { code: 'prepared-spin-unavailable' })
    const operation = pending() ?? initial
    spins.remember(accountId!, operation)
    const response = await fetch(preparedSlotSpinPath, {
      method: 'POST', headers: { ...preparedSlotSpinHeaders(accountId, operation.operationId), 'Content-Type': 'application/json' },
      body: JSON.stringify({ gameId: PHARAOH_GAME_ID, wagerPoints: operation.wagerPoints, useFreeSpin: operation.useFreeSpin, useSpecialBoost: operation.useSpecialBoost }),
    })
    const raw = await json(response)
    if (!response.ok) {
      const error = problem(response, raw)
      if (error.pendingOperation) spins.remember(accountId!, error.pendingOperation)
      else if (record(raw) && raw.preparedOperationPending === false ||
        ['slot-prepared-start-paused', 'free-spins-required', 'free-spins-unavailable', 'free-spin-wager-locked', 'tomb-expedition-active'].includes(error.code ?? ''))
        spins.forget(accountId!, operation.operationId)
      throw error
    }
    const result = decodePharaohSpinResult(raw)
    if (!result || result.wagerPoints !== operation.wagerPoints || result.isFreeSpin !== operation.useFreeSpin) throw malformed()
    spins.forget(accountId!, operation.operationId)
    observe(result)
    return result
  }
  function run(operation: PreparedSlotSpin): Promise<PharaohSpinResult> {
    if (inFlight) return inFlight
    inFlight = postSpin(operation).finally(() => { inFlight = null })
    return inFlight
  }
  function spin(wagerPoints: number, useFreeSpin: boolean): Promise<PharaohSpinResult> {
    transport()
    if (savedAction() || actionFlight) throw new PharaohRequestError('Resume the saved expedition action before spinning.', 409)
    const saved = pending()
    if (saved) return run(saved)
    if (!(useFreeSpin ? fundedWager(wagerPoints) : paidWager(wagerPoints))) throw new PharaohRequestError('Choose an available wager.', 400)
    return run({ operationId: crypto.randomUUID(), wagerPoints, useFreeSpin, useSpecialBoost: false })
  }
  function resumeSpin(): Promise<PharaohSpinResult> {
    transport()
    const saved = pending()
    if (!saved) throw new PharaohRequestError('No saved spin needs recovery.', 409)
    return run(saved)
  }
  function action(intent: TombAction): Promise<PharaohSlotState> {
    const fetch = transport()
    if (!isTombAction(intent)) throw new PharaohRequestError('Choose an available expedition action.', 400)
    if (pending() || inFlight) throw new PharaohRequestError('Resume the saved spin before entering the tomb.', 409)
    const body = actionBody(intent)
    const key = JSON.stringify(body)
    if (actionFlight) {
      if (actionFlight.key === key) return actionFlight.result
      throw new PharaohRequestError('Wait for your expedition action to settle.', 409)
    }
    const saved = savedAction()
    if (saved && JSON.stringify(actionBody(saved.action)) !== key) throw new PharaohRequestError('Resume the saved expedition action first.', 409)
    const operation = saved ?? { id: crypto.randomUUID(), action: body }
    rememberAction(operation)
    const result = (async () => {
      const response = await fetch('/api/slots/pharaohs-ascent/actions', {
        method: 'POST', headers: { ...preparedSlotSpinHeaders(accountId, operation.id), 'Content-Type': 'application/json' }, body: key,
      })
      const raw = await json(response)
      if (!response.ok) {
        if (response.status === 400 || response.status === 409 && record(raw) && raw.code !== 'tomb-operation-pending') forgetAction()
        throw problem(response, raw)
      }
      const state = decodePharaohSlotState(raw)
      if (!state || state.gameFeatureState.tomb.revision !== intent.expectedRevision + 1) throw malformed()
      forgetAction()
      observe(state)
      return state
    })().finally(() => { actionFlight = null })
    actionFlight = { key, result }
    return result
  }
  return { getState, spin, resumeSpin, action, pending, pendingAction: () => savedAction()?.action ?? null }
}
