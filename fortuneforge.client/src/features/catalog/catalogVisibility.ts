import { useEffect, useState } from 'react'

export type CatalogueVisibility = { version: number; games: Array<{ id: string; route: string; visible: boolean }> }
let lastKnown: CatalogueVisibility | null = null
let lastError = ''
let pending: Promise<CatalogueVisibility> | null = null
let refreshedAt = 0
let channel: BroadcastChannel | null = null
let poll: ReturnType<typeof setInterval> | undefined
const listeners = new Set<() => void>()
const changed = () => listeners.forEach(listener => listener())
const refresh = () => { void loadCatalogueVisibility(true).catch(() => {}) }
const focus = () => { if (document.visibilityState !== 'hidden') refresh() }

export function catalogueRouteVisible(state: CatalogueVisibility | null, route?: string | null) {
  if (!state) return false
  if (!route) return true
  const canonical = route.replace(/\/demo\/?$/, '')
  return state.games.find(item => item.route === canonical)?.visible === true
}

export async function loadCatalogueVisibility(force = false): Promise<CatalogueVisibility> {
  if (!force && lastKnown && Date.now() - refreshedAt < 10_000) return lastKnown
  if (pending) return pending
  pending = fetch('/api/catalog/visibility', { cache: 'no-store', headers: { Accept: 'application/json' } }).then(async response => {
    if (!response.ok) throw new Error('Catalogue availability could not be checked. Please reload to try again.')
    const value: unknown = await response.json()
    if (!value || typeof value !== 'object' || !('version' in value) || typeof value.version !== 'number' || !Number.isSafeInteger(value.version) || value.version < 0 || !('games' in value) || !Array.isArray(value.games) || !value.games.every(item => item && typeof item.id === 'string' && typeof item.route === 'string' && typeof item.visible === 'boolean')) throw new Error('Catalogue availability could not be verified.')
    const next = value as CatalogueVisibility
    if (!lastKnown || next.version >= lastKnown.version) lastKnown = next
    refreshedAt = Date.now(); lastError = ''; changed()
    return lastKnown!
  }).catch(reason => { lastError = reason instanceof Error ? reason.message : 'Catalogue availability could not be checked.'; changed(); throw reason }).finally(() => { pending = null })
  return pending
}

/** Immediate local update from the authoritative admin receipt; other tabs refetch. */
export function notifyCatalogueChanged(state: CatalogueVisibility) {
  if (!lastKnown || state.version >= lastKnown.version) lastKnown = state
  lastError = ''; refreshedAt = Date.now(); changed()
  connectChannel()?.postMessage('refresh')
}
function connectChannel() {
  if (!channel && typeof window !== 'undefined' && typeof BroadcastChannel !== 'undefined') {
    channel = new BroadcastChannel('fortune-forge-catalogue')
    channel.addEventListener('message', event => { if (event.data === 'refresh') refresh() })
  }
  return channel
}

/** One shared poll, regardless of how many catalogue cards are mounted. */
export function useCatalogueVisibility() {
  const [, update] = useState(0)
  useEffect(() => {
    const listener = () => update(value => value + 1)
    listeners.add(listener); connectChannel()
    void loadCatalogueVisibility().then(listener).catch(() => {})
    if (listeners.size === 1) {
      poll = setInterval(focus, 30_000)
      window.addEventListener('focus', focus)
      document.addEventListener('visibilitychange', focus)
    }
    return () => {
      listeners.delete(listener)
      if (!listeners.size) {
        if (poll) clearInterval(poll)
        poll = undefined
        window.removeEventListener('focus', focus)
        document.removeEventListener('visibilitychange', focus)
      }
    }
  }, [])
  return { loading: lastKnown === null && !lastError, error: lastError, isVisible: (route?: string | null) => catalogueRouteVisible(lastKnown, route) }
}
