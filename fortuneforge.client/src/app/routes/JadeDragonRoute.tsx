import { useLayoutEffect, useRef } from 'react'
import { InGameNavbar } from '../../components/InGameNavbar'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { useGameFrameRelease } from '../../features/game-releases/useGameRelease'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'
import { fetchWithAccountSession, getCurrentAccount } from '../../features/account/services/accountsApi'
import { attachAuthenticatedFrameTransport } from '../../features/game-releases/authenticatedFrameTransport'
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
    return attachAuthenticatedFrameTransport({ accountId: account.userId, source: () => frame.current?.contentWindow,
      origin: frameOrigin, authenticatedFetch: fetchWithAccountSession, loadAccount: getCurrentAccount })
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
