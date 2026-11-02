import { fetchWithAccountSession } from '../account/services/accountsApi'
import { ACCOUNT_EXPERIENCE_INVALIDATION_KEY, getAccountSessionRevision, getLocalAccountSessionSignal, subscribeAccountSessionEvents } from '../account/services/accountSessionEvents'
import { CANONICAL_EXPERIENCE_GAME_IDS } from './gameExperienceIdentity'

export type ExperienceRow = Readonly<{
  gameId: string; state: 'known' | 'unknown'; hasPlayed: boolean | null
  provenance: readonly string[]; coverage: 'complete' | 'partial' | 'unavailable'; firstPlayedAtUtc: string | null
}>
export type ExperienceResponse = Readonly<{
  schemaVersion: 1; userId: string; generatedAtUtc: string; games: readonly ExperienceRow[]
}>
export type ExperienceSnapshot = Readonly<{
  status: 'loading' | 'ready' | 'error' | 'guest'; data: ExperienceResponse | null; error: string | null
}>
const empty: ExperienceSnapshot = Object.freeze({ status: 'loading', data: null, error: null })
let snapshot = empty, loadedAt = 0, generation = 0, mutationRevision = 0
let snapshotRevision = getAccountSessionRevision()
let pending: Promise<void> | null = null, abort: AbortController | null = null
let refreshAfterPending = false
let disconnect: (() => void) | null = null, timer: ReturnType<typeof setTimeout> | null = null
const listeners = new Set<() => void>()
const invalidationKey = ACCOUNT_EXPERIENCE_INVALIDATION_KEY
const tokenKey = 'fortune-forge.account-token'
const changed = () => listeners.forEach(listener => listener())
const record = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null
const withoutNegativeProof = (row: ExperienceRow): ExperienceRow => row.hasPlayed === false
  ? { ...row, state: 'unknown', hasPlayed: null, provenance: [], coverage: 'partial', firstPlayedAtUtc: null }
  : row

function invalidateMutation() {
  mutationRevision++; loadedAt = 0
  if (snapshot.data?.games.some(row => row.hasPlayed === false)) {
    snapshot = { ...snapshot, data: { ...snapshot.data, games: snapshot.data.games.map(withoutNegativeProof) } }
    changed()
  }
}

export function parseExperienceResponse(value: unknown): ExperienceResponse {
  if (!record(value) || value.schemaVersion !== 1 || typeof value.userId !== 'string' || !value.userId ||
      typeof value.generatedAtUtc !== 'string' || !Number.isFinite(Date.parse(value.generatedAtUtc)) || !Array.isArray(value.games)) throw new Error('Player game experience returned an invalid response.')
  const ids = new Set<string>()
  for (const row of value.games) {
    if (!record(row) || typeof row.gameId !== 'string' || ids.has(row.gameId) ||
        !CANONICAL_EXPERIENCE_GAME_IDS.includes(row.gameId) ||
        !['complete', 'partial', 'unavailable'].includes(String(row.coverage)) ||
        !Array.isArray(row.provenance) || !row.provenance.every(item => typeof item === 'string') ||
        !(row.firstPlayedAtUtc === null || typeof row.firstPlayedAtUtc === 'string' && Number.isFinite(Date.parse(row.firstPlayedAtUtc))) ||
        !(row.state === 'known' && typeof row.hasPlayed === 'boolean' || row.state === 'unknown' && row.hasPlayed === null) ||
        row.state === 'known' && row.provenance.length === 0 ||
        row.hasPlayed === false && (row.coverage !== 'complete' || row.firstPlayedAtUtc !== null)) throw new Error('Player game experience returned unproven or invalid knowledge.')
    ids.add(row.gameId)
  }
  if (ids.size !== CANONICAL_EXPERIENCE_GAME_IDS.length) throw new Error('Player game experience omitted a playable game.')
  return value as unknown as ExperienceResponse
}

export const getExperienceSnapshot = () => snapshotRevision === getAccountSessionRevision() ? snapshot : empty
export const getServerExperienceSnapshot = () => empty

export function loadGameExperiences(force = false): Promise<void> {
  if (snapshotRevision !== getAccountSessionRevision()) invalidateIdentity()
  if (pending) return pending
  if (!force && loadedAt && Date.now() - loadedAt < 30_000) return Promise.resolve()
  const version = ++generation, session = getAccountSessionRevision(), previous = snapshot.data, mutation = mutationRevision
  const controller = new AbortController(); abort = controller
  snapshot = empty; changed()
  const timeout = setTimeout(() => controller.abort(), 5_000)
  let task!: Promise<void>
  task = (async () => {
    try {
      const response = await fetchWithAccountSession('/api/games/experience', { method: 'GET', cache: 'no-store', signal: controller.signal })
      if (version !== generation || session !== getAccountSessionRevision()) return
      if (response.status === 401) {
        snapshot = { status: 'guest', data: null, error: null }
      } else {
        if (!response.ok) throw new Error('Player game experience is temporarily unavailable.')
        const next = parseExperienceResponse(await response.json())
        if (version !== generation || session !== getAccountSessionRevision()) return
        // A delayed/incomplete index read cannot undo accepted positive evidence.
        const positives = previous?.userId === next.userId ? new Map(previous.games.filter(row => row.hasPlayed === true).map(row => [row.gameId, row])) : new Map<string, ExperienceRow>()
        snapshot = { status: 'ready', data: { ...next, games: next.games.map(row =>
          positives.get(row.gameId) ?? (mutation === mutationRevision ? row : withoutNegativeProof(row))) }, error: null }
      }
      loadedAt = Date.now(); changed()
    } catch (reason) {
      if (version !== generation || session !== getAccountSessionRevision()) return
      snapshot = { status: 'error', data: null, error: reason instanceof Error ? reason.message : 'Player game experience is temporarily unavailable.' }
      loadedAt = 0; changed()
    } finally {
      clearTimeout(timeout)
      if (pending === task) {
        pending = null; abort = null
        // The debounce may already have fired while this older batch was busy.
        if (refreshAfterPending && timer === null) refreshGameExperiences()
      }
    }
  })()
  pending = task
  return task
}

function invalidateIdentity() {
  generation++; abort?.abort(); abort = null; pending = null; loadedAt = 0
  refreshAfterPending = false
  if (timer) clearTimeout(timer)
  timer = null
  snapshotRevision = getAccountSessionRevision()
  snapshot = empty; changed()
}

export function refreshGameExperiences() {
  if (pending) { refreshAfterPending = true; return }
  refreshAfterPending = false
  void loadGameExperiences(true)
}

export function subscribeGameExperiences(listener: () => void) {
  listeners.add(listener)
  if (listeners.size === 1) disconnect = connect()
  return () => {
    listeners.delete(listener)
    if (!listeners.size) {
      disconnect?.(); disconnect = null
      // A cookie-only login in another tab can happen while no observer is
      // connected. Never reuse that unobserved account's cache on reconnect.
      invalidateIdentity()
    }
  }
}

function connect() {
  let channel: BroadcastChannel | null = null
  try {
    if (typeof window.BroadcastChannel === 'function') channel = new window.BroadcastChannel('fortune-forge.game-experience')
  } catch { /* Optional metadata must work when this transport is disabled. */ }
  const schedule = () => {
    if (timer) return
    timer = setTimeout(() => { timer = null; refreshGameExperiences() }, 150)
  }
  const announce = () => {
    try { channel?.postMessage('refresh') } catch { /* Storage/focus remain available. */ }
    try { localStorage.setItem(invalidationKey, `refresh:${Date.now()}`) } catch { /* Optional cross-tab invalidation only. */ }
  }
  const unsubscribe = subscribeAccountSessionEvents(event => {
    if (event === 'session-changed') { invalidateIdentity(); refreshGameExperiences() }
    else { invalidateMutation(); schedule(); announce() }
  })
  let lastRemoteSessionSignal: string | null = null
  const remoteSession = (signal: string) => {
    if (signal === getLocalAccountSessionSignal() || signal === lastRemoteSessionSignal) return
    lastRemoteSessionSignal = signal
    invalidateIdentity(); refreshGameExperiences()
  }
  const focus = () => { if (document.visibilityState !== 'hidden') { invalidateIdentity(); refreshGameExperiences() } }
  const storage = (event: StorageEvent) => {
    if (event.key === tokenKey || event.key === null) { invalidateIdentity(); refreshGameExperiences() }
    else if (event.key === invalidationKey) {
      if (event.newValue?.startsWith('session-changed:')) remoteSession(event.newValue)
      else { invalidateMutation(); schedule() }
    }
  }
  const message = (event: MessageEvent) => {
    if (typeof event.data === 'string' && event.data.startsWith('session-changed:')) remoteSession(event.data)
    else if (event.data === 'refresh') { invalidateMutation(); schedule() }
  }
  window.addEventListener('focus', focus); document.addEventListener('visibilitychange', focus)
  window.addEventListener('storage', storage); channel?.addEventListener('message', message)
  const poll = setInterval(() => { if (document.visibilityState !== 'hidden') refreshGameExperiences() }, 30_000)
  return () => {
    unsubscribe(); clearInterval(poll); if (timer) clearTimeout(timer); timer = null
    window.removeEventListener('focus', focus); document.removeEventListener('visibilitychange', focus)
    window.removeEventListener('storage', storage); channel?.removeEventListener('message', message); channel?.close()
  }
}
