// Edited: 2026-11-18T22:49:02-06:00 | Computer: Vinnabok
import type { ReactNode } from 'react'
import { SiteNavbar } from '../components/SiteNavbar'

export function SiteRouteLayout({ children, pathname = typeof window === 'undefined' ? '/' : window.location.pathname }: { children: ReactNode; pathname?: string }) {
  return <div className="site-route-state"><SiteNavbar mode={pathname.replace(/\/+$/, '') === '/account/review' ? 'restricted' : 'public'} />{children}</div>
}
