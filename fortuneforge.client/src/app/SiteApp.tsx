// Edited: 2026-11-18T22:49:02-06:00 | Computer: Vinnabok
import { Suspense, useEffect, useState } from 'react'
import { AccountAccessBoundary } from '../features/account/AccountAccessBoundary'
import { resolveSiteRoute, SiteNotFoundPage } from './SiteRoutes'
import { SiteRouteLayout } from './SiteRouteLayout'
import { RouteErrorBoundary, RouteErrorState, RouteLoadingState } from './RouteBoundary'
import { usePageTitle } from './usePageTitle'
import { siteShellClasses } from './siteShellClasses'
import './styles/shell.css'

/** Links to games deliberately load a document so Hosting can serve their preserved shell. */
export function SiteApp() {
  const [pathname, setPathname] = useState(() => window.location.pathname.replace(/\/+$/, '') || '/')
  useEffect(() => {
    const syncPath = () => setPathname(window.location.pathname.replace(/\/+$/, '') || '/')
    window.addEventListener('popstate', syncPath)
    return () => window.removeEventListener('popstate', syncPath)
  }, [])
  usePageTitle(pathname)
  const shellClass = siteShellClasses(pathname).join(' ')
  return <div className={shellClass}><AccountAccessBoundary>
    <RouteErrorBoundary key={pathname} fallback={<SiteRouteLayout><RouteErrorState /></SiteRouteLayout>}>
      <Suspense fallback={<SiteRouteLayout><RouteLoadingState /></SiteRouteLayout>}>
        {resolveSiteRoute(pathname) ?? <SiteNotFoundPage />}
      </Suspense>
    </RouteErrorBoundary>
  </AccountAccessBoundary></div>
}
