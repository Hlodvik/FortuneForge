import { HiLoGatewayError, hiLoRanks, type HiLoActionOptions, type HiLoCard, type HiLoChoice, type HiLoGateway, type HiLoLimits, type HiLoQuote, type HiLoRequestOptions, type HiLoRound, type HiLoSession } from './contracts'

/** Cookie-bound adapter; the browser never draws cards or calculates paid outcomes. */
export class HttpHiLoGateway implements HiLoGateway {
  constructor(readonly basePath = '/api/games/hi-lo', private readonly requestFn: typeof fetch = fetch) {}

  getSession(signal?: AbortSignal) { return this.request('/session', { signal }) }

  async startRound(wagerCents: number, options: HiLoRequestOptions) {
    const session = await this.request('/rounds', post({ operationId: options.operationId, wagerCents }, options.signal))
    if (session.round?.wagerCents !== wagerCents) throw mismatch()
    return session
  }

  async act(roundId: string, choice: HiLoChoice, options: HiLoActionOptions) {
    const session = await this.request(`/rounds/${encodeURIComponent(roundId)}/actions`, post({ operationId: options.operationId, expectedRevision: options.expectedRevision, choice }, options.signal))
    if (session.round?.roundId !== roundId || session.round.revision !== options.expectedRevision + 1) throw mismatch()
    return session
  }

  private async request(path: string, init: RequestInit): Promise<HiLoSession> {
    // Avoid invoking native fetch with this gateway as its receiver.
    const request = this.requestFn
    const response = await request(`${this.basePath.replace(/\/$/, '')}${path}`, { ...init, credentials: 'include', cache: 'no-store' })
    const payload: unknown = await response.json().catch(() => undefined)
    if (!response.ok) {
      const error = record(payload) ? payload : {}
      throw new HiLoGatewayError(
        typeof error.message === 'string' ? error.message : `Hi-Lo could not complete the request (${response.status}).`,
        typeof error.code === 'string' ? error.code : 'hi-lo-request-failed',
        response.status,
      )
    }
    if (!isHiLoSession(payload)) {
      throw new HiLoGatewayError('Hi-Lo returned an incomplete table state. Retry the same action to restore it.', 'hi-lo-invalid-response', response.status)
    }
    return payload
  }
}

function post(body: object, signal?: AbortSignal): RequestInit {
  return { method: 'POST', signal, headers: { 'Content-Type': 'application/json', 'X-FortuneForge-Game': 'hi-lo' }, body: JSON.stringify(body) }
}
function mismatch() { return new HiLoGatewayError('Hi-Lo did not confirm this action. Retry the same action to restore it.', 'hi-lo-invalid-response') }
const record = (v: unknown): v is Record<string, unknown> => v !== null && typeof v === 'object' && !Array.isArray(v)
const finite = (v: unknown): v is number => typeof v === 'number' && Number.isFinite(v)
const integer = (v: unknown): v is number => typeof v === 'number' && Number.isSafeInteger(v) && v >= 0
const positiveInteger = (v: unknown): v is number => integer(v) && v > 0
const card = (v: unknown): v is HiLoCard => record(v) && hiLoRanks.includes(v.rank as HiLoCard['rank']) && ['clubs', 'diamonds', 'hearts', 'spades'].includes(String(v.suit))

function limits(v: unknown): v is HiLoLimits {
  return record(v) && positiveInteger(v.minWagerCents) && positiveInteger(v.maxWagerCents) && v.maxWagerCents >= v.minWagerCents && positiveInteger(v.maximumReturnCents) && v.maximumReturnCents >= v.maxWagerCents && finite(v.maxMultiplier) && v.maxMultiplier >= 1 && integer(v.maxSkips) && positiveInteger(v.maxStreak) && finite(v.houseEdge) && v.houseEdge >= 0 && v.houseEdge < 1
}
function quote(v: unknown): v is HiLoQuote {
  return record(v) && finite(v.probability) && v.probability >= 0 && v.probability <= 1 && finite(v.multiplier) && v.multiplier >= 0 && integer(v.projectedReturnCents) && typeof v.available === 'boolean' && (!v.available || v.probability > 0 && v.multiplier > 0)
}
function round(v: unknown, rules: HiLoLimits): v is HiLoRound {
  if (!record(v) || typeof v.roundId !== 'string' || !v.roundId || !integer(v.revision) || !['active', 'lost', 'cashedOut', 'maximumReturn'].includes(String(v.status)) || !positiveInteger(v.wagerCents) || v.wagerCents < rules.minWagerCents || v.wagerCents > rules.maxWagerCents || !card(v.currentCard) || !integer(v.currentReturnCents) || v.currentReturnCents > rules.maximumReturnCents || !finite(v.multiplier) || v.multiplier < 0 || v.multiplier > rules.maxMultiplier || !integer(v.streak) || v.streak > rules.maxStreak || !integer(v.skips) || v.skips > rules.maxSkips || !quote(v.higher) || !quote(v.lower) || typeof v.canCashOut !== 'boolean' || typeof v.canSkip !== 'boolean' || !Array.isArray(v.history) || v.history.length === 0) return false
  if (!v.history.every((entry: unknown) => record(entry) && card(entry.card) && ['start', 'higher', 'lower', 'skip'].includes(String(entry.choice)) && (entry.won === null || typeof entry.won === 'boolean'))) return false
  const first = v.history[0], latest = v.history[v.history.length - 1]
  if (first.choice !== 'start' || first.won !== null || latest.card.rank !== v.currentCard.rank || latest.card.suit !== v.currentCard.suit) return false
  if (v.status !== 'active' && (v.canCashOut || v.canSkip || v.higher.available || v.lower.available)) return false
  if (v.status === 'lost' && v.currentReturnCents !== 0) return false
  return true
}

/** Validate boundary data before it can become actionable UI state. */
export function isHiLoSession(v: unknown): v is HiLoSession {
  return record(v) && typeof v.practice === 'boolean' && (v.enabled === undefined || typeof v.enabled === 'boolean') && integer(v.walletBalanceCents) && limits(v.limits) && (v.round === null || round(v.round, v.limits))
}
