export type AccountSessionEvent = 'session-changed' | 'accepted-mutation'
const listeners = new Set<(event: AccountSessionEvent) => void>()
let revision = 0
let localSessionSignal: string | null = null
export const ACCOUNT_EXPERIENCE_INVALIDATION_KEY = 'fortune-forge.game-experience-invalidation'

export function getAccountSessionRevision() { return revision }
export function getLocalAccountSessionSignal() { return localSessionSignal }
export function subscribeAccountSessionEvents(listener: (event: AccountSessionEvent) => void) {
  listeners.add(listener)
  return () => { listeners.delete(listener) }
}

/** Observers may invalidate reads; they cannot change a successful game response. */
export function publishAccountSessionEvent(event: AccountSessionEvent) {
  if (event === 'session-changed') {
    revision++
    // Authentication pages have no game consumer. Still invalidate other tabs
    // after a cookie-only login/logout; these messages contain no account data.
    localSessionSignal = `session-changed:${Date.now()}:${Math.random().toString(36).slice(2)}`
    if (typeof window !== 'undefined') {
      try { localStorage.setItem(ACCOUNT_EXPERIENCE_INVALIDATION_KEY, localSessionSignal) } catch { /* Optional transport. */ }
      try {
        if (typeof window.BroadcastChannel === 'function') {
          const channel = new window.BroadcastChannel('fortune-forge.game-experience')
          channel.postMessage(localSessionSignal); channel.close()
        }
      } catch { /* Authentication must also work without cross-tab messaging. */ }
    }
  }
  for (const listener of listeners) {
    try { listener(event) } catch { /* A cache observer must not break gameplay. */ }
  }
}
