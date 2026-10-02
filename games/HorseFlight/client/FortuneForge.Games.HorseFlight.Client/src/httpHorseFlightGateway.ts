import { HorseFlightGatewayError, type HorseFlightGateway, type HorseFlightRequestOptions, type HorseFlightResult, type HorseFlightStart, type HorseFlightStatus } from './contracts'
export class HttpHorseFlightGateway implements HorseFlightGateway {
  constructor(private readonly basePath = '/api/games/horse-flight', private readonly requestFn: typeof fetch = fetch) {}
  getStatus(signal?: AbortSignal) { return this.request('/status', { signal }, isStatus) }
  start(options: HorseFlightRequestOptions = {}) { return this.request('/runs', post({}, options), isStart) }
  complete(runId: string, totalTicks: number, jumpTicks: readonly number[], options: HorseFlightRequestOptions = {}, rightClickTicks: readonly number[] = []) { return this.request(`/runs/${encodeURIComponent(runId)}/complete`, post({ totalTicks, jumpTicks, rightClickTicks }, options), isResult) }
  private async request<T>(path: string, init: RequestInit, check: (value: unknown) => value is T): Promise<T> { const response = await this.requestFn(`${this.basePath}${path}`, init); const value: unknown = await response.json().catch(() => null); if (!response.ok) throw new HorseFlightGatewayError(record(value) && typeof value.message === 'string' ? value.message : `Horse Flight failed (${response.status}).`, record(value) && typeof value.code === 'string' ? value.code : 'horse-flight-request-failed', response.status); if (!check(value)) throw new HorseFlightGatewayError('Horse Flight returned an invalid response.', 'horse-flight-invalid-response', response.status); return value }
}
function post(body: object, options: HorseFlightRequestOptions): RequestInit { return { method: 'POST', headers: { 'content-type': 'application/json', 'Idempotency-Key': options.idempotencyKey ?? key() }, body: JSON.stringify(body), signal: options.signal } }
function key() { const random = typeof crypto?.randomUUID === 'function' ? crypto.randomUUID().replaceAll('-', '') : `${Date.now().toString(36)}${Math.random().toString(36).slice(2)}`; return `horse-flight-${random}` }
function record(value: unknown): value is Record<string, unknown> { return typeof value === 'object' && value !== null }
function finite(value: unknown): value is number { return typeof value === 'number' && Number.isFinite(value) }
function id(value: unknown): value is string { return typeof value === 'string' && value.length > 0 }
function phase(value: unknown): value is HorseFlightResult['phase'] { return value === 'obstacle-collision' || value === 'fell' }
function isStatus(value: unknown): value is HorseFlightStatus { return record(value) && typeof value.available === 'boolean' && value.tickMilliseconds === 20 && finite(value.balance) && typeof value.mode === 'string' }
function isStart(value: unknown): value is HorseFlightStart { return record(value) && id(value.runId) && finite(value.seed) && value.tickMilliseconds === 20 }
function isResult(value: unknown): value is HorseFlightResult { return record(value) && id(value.runId) && finite(value.balance) && Number.isInteger(value.score) && phase(value.phase) && Number.isInteger(value.totalTicks) && Array.isArray(value.jumpTicks) && value.jumpTicks.every(tick => Number.isInteger(tick)) && (!('rightClickTicks' in value) || Array.isArray(value.rightClickTicks) && value.rightClickTicks.every(tick => Number.isInteger(tick))) }
