import { useLayoutEffect, useRef } from 'react'
import { InGameNavbar } from '../../components/InGameNavbar'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { useGameFrameRelease } from '../../features/game-releases/useGameRelease'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'
import { loadJadeSession, spinJade } from './jadeDragonApi'
import './JadeDragonRoute.css'

export function JadeDragonRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/slots/jade-dragon')
  const { state: release, retry } = useGameFrameRelease('jade-dragon')
  const frame = useRef<HTMLIFrameElement>(null)
  const frameLocation = new URL(release.status === 'ready' ? release.frameUrl : '/games/jade-dragon/index.html', window.location.origin)
  const frameOrigin = frameLocation.origin
  const isExternal = frameOrigin !== window.location.origin
  if (isExternal) frameLocation.searchParams.set('ffHostOrigin', window.location.origin)
  const frameSource = frameLocation.href
  const frameReady = release.status === 'local' || release.status === 'ready'

  useLayoutEffect(() => {
    if (!account || !frameReady) return
    let mounted = true
    let spinning = false
    const responses = new Map<string, Promise<unknown>>()
    const receive = (event: MessageEvent) => {
      const source = frame.current?.contentWindow
      if (!source || event.source !== source || event.origin !== frameOrigin ||
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
        result => { if (mounted) source.postMessage({ channel: 'fortune-jade-v1', id, result }, frameOrigin) },
        issue => { if (mounted) source.postMessage({ channel: 'fortune-jade-v1', id, error: issue instanceof Error ? issue.message : 'Game request failed.' }, frameOrigin) },
      )
    }
    window.addEventListener('message', receive)
    return () => { mounted = false; window.removeEventListener('message', receive) }
  }, [account, frameOrigin, frameReady, frameSource])
  if (isLoading || !account) return <AuthenticatedRouteState error={error} loadingLabel="Opening Jade Dragon…" errorTitle="Jade Dragon could not be opened." onRetry={reload} />
  if (!frameReady) return <AuthenticatedRouteState error={release.status === 'error' ? release.error : null}
    loadingLabel="Opening Jade Dragon…" errorTitle="Jade Dragon could not be opened." onRetry={retry} />
  return <div className="jade-dragon">
    <InGameNavbar authenticated title="Jade Dragon" />
    <main className="jade-dragon__surface" aria-label="Jade Dragon: The Golden Pearl">
      <iframe key={frameSource} ref={frame} src={isExternal ? frameSource : frameLocation.pathname}
        title="Jade Dragon slot game" allow="fullscreen" referrerPolicy="origin"
        sandbox={isExternal ? 'allow-scripts allow-same-origin' : undefined} />
    </main>
  </div>
}
