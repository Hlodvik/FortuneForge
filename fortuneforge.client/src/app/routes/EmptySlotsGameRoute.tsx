import { useEffect, useRef } from 'react'
import { InGameNavbar } from '../../components/InGameNavbar'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'
import { loadJadeSession, spinJade } from './jadeDragonApi'
import './EmptySlotsGameRoute.css'

export function EmptySlotsGameRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/slots/empty-slots-game')
  const frame = useRef<HTMLIFrameElement>(null)
  useEffect(() => {
    if (!account) return
    let mounted = true
    let spinning = false
    const responses = new Map<string, Promise<unknown>>()
    const receive = (event: MessageEvent) => {
      const source = frame.current?.contentWindow
      if (!source || event.source !== source || event.origin !== window.location.origin ||
        event.data?.channel !== 'fortune-jade-v1' || typeof event.data.id !== 'string' || event.data.id.length > 80) return
      const { id, method, payload } = event.data as { id: string; method: string; payload: unknown }
      let pending = responses.get(id)
      if (!pending) {
        pending = (async () => {
          if (method === 'init') {
            if (spinning) throw new Error('Wait for the current round to settle.')
            return loadJadeSession()
          }
          if (method !== 'spin') throw new Error('Unknown game request.')
          if (spinning) throw new Error('A spin is already in progress.')
          spinning = true
          try { return await spinJade(payload) }
          finally { spinning = false }
        })()
        responses.set(id, pending)
        // Keep financial request IDs for this frame's lifetime; a replay never submits another bet.
      }
      void pending.then(
        result => { if (mounted) source.postMessage({ channel: 'fortune-jade-v1', id, result }, window.location.origin) },
        issue => { if (mounted) source.postMessage({ channel: 'fortune-jade-v1', id, error: issue instanceof Error ? issue.message : 'Game request failed.' }, window.location.origin) },
      )
    }
    window.addEventListener('message', receive)
    return () => { mounted = false; window.removeEventListener('message', receive) }
  }, [account])
  if (isLoading || !account) return <AuthenticatedRouteState error={error} loadingLabel="Opening Jade Dragon…" errorTitle="Jade Dragon could not be opened." onRetry={reload} />
  return <div className="empty-slots-game">
    <InGameNavbar authenticated title="Jade Dragon" />
    <main className="empty-slots-game__surface" aria-label="Jade Dragon: The Golden Pearl">
      <iframe ref={frame} src="/games/jade-dragon/index.html" title="Jade Dragon slot game" allow="fullscreen" />
    </main>
  </div>
}
