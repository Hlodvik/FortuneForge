import type { ReactNode } from 'react'
import { InGameShell } from '../components/InGameShell'
import { SiteNavbar } from '../components/SiteNavbar'
import type { GameLoadingEntry } from '../features/game-loading/types'

/** Pending and failed routes retain navigation without wrapping ready pages twice. */
export function RouteStateLayout({ children, entry, pathname = typeof window === 'undefined' ? '/' : window.location.pathname }: {
  children: ReactNode
  entry?: GameLoadingEntry
  pathname?: string
}) {
  const routePath = pathname.replace(/\/+$/, '') || '/'
  const isPublic = routePath === '/' || routePath.startsWith('/demo') || routePath.endsWith('/demo')
    || ['/login', '/create-account', '/verify-email', '/slots/rainbow-realm-preview'].includes(routePath)
  const authenticated = !isPublic && /^\/(home|balance|admin|games|cards|slots)(\/|$)/.test(routePath)

  // Navigation follows the route kind even when no loading artwork is registered.
  const gameRoute = /^\/(games|cards|slots)\/.+/.test(routePath) || /^\/demo\/cards\/.+/.test(routePath)
  if (entry || gameRoute) return <InGameShell authenticated={authenticated} title={entry?.title} className="route-state-shell">{children}</InGameShell>
  return <div className="site-route-state"><SiteNavbar mode={routePath === '/account/review' ? 'restricted' : authenticated ? 'account' : 'public'} authenticated={authenticated} />{children}</div>
}
