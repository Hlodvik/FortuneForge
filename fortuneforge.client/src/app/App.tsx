import { useEffect, useState } from 'react'
import { findSlotRoute } from '../games/slots'
import { AppRoutes } from './AppRoutes'
import { usePageTitle } from './usePageTitle'
import { useGamePresence } from '../features/catalog/useGamePresence'
import { AccountAccessBoundary } from '../features/account/AccountAccessBoundary'
import { GameLeaveGuardProvider } from '../features/navigation/GameLeaveGuard'
import './styles/index.css'

export default function App() {
  const [pathname, setPathname] = useState(() => window.location.pathname.replace(/\/+$/, '') || '/')
  const matchedSlotRoute = findSlotRoute(pathname)
  const slotRoute = matchedSlotRoute?.playPath === pathname ? matchedSlotRoute : null
  const isPlayableGameRoute = slotRoute !== null || (
    pathname.startsWith('/games/') ||
    pathname.startsWith('/cards/')
  )
  const usesThemeSpecificSlotBackdrop =
    slotRoute?.shellBackdrop === 'theme'
  const appShellClassName = [
    'app-shell',
    pathname === '/' ? 'app-shell--landing' : '',
    pathname === '/home' || pathname.startsWith('/home/') || pathname === '/balance' ? 'app-shell--home' : '',
    isPlayableGameRoute ? 'app-shell--game' : '',
    pathname.startsWith('/slots/') ? 'app-shell--slot-game' : '',
    pathname === '/cards' || pathname.startsWith('/cards/')
      ? 'app-shell--card-room'
      : '',
    usesThemeSpecificSlotBackdrop ? 'app-shell--themed-slot-backdrop' : '',
  ]
    .filter(Boolean)
    .join(' ')

  usePageTitle(pathname)
  useGamePresence(pathname)

  useEffect(() => {
    const syncPathname = () => setPathname(window.location.pathname.replace(/\/+$/, '') || '/')
    const openSlotFromCatalogue = (event: MouseEvent) => {
      if (isPlayableGameRoute || event.defaultPrevented || event.button !== 0 ||
        event.metaKey || event.ctrlKey || event.shiftKey || event.altKey ||
        !(event.target instanceof Element)) return
      const link = event.target.closest<HTMLAnchorElement>('a[href]')
      if (!link || link.hasAttribute('download') || (link.target && link.target !== '_self')) return
      const destination = new URL(link.href, window.location.href)
      const destinationPath = destination.pathname.replace(/\/+$/, '') || '/'
      if (destination.origin !== window.location.origin || findSlotRoute(destinationPath)?.playPath !== destinationPath) return

      // Keep the catalogue click's browser audio activation in this document.
      event.preventDefault()
      window.history.pushState(window.history.state, '', destination.href)
      syncPathname()
      window.scrollTo(0, 0)
    }

    window.addEventListener('popstate', syncPathname)
    document.addEventListener('click', openSlotFromCatalogue)
    return () => {
      window.removeEventListener('popstate', syncPathname)
      document.removeEventListener('click', openSlotFromCatalogue)
    }
  }, [isPlayableGameRoute])

  useEffect(() => {
    if (!isPlayableGameRoute) return undefined

    document.documentElement.classList.add('ff-game-active')
    document.body.classList.add('ff-game-active')
    const preventNativeImageDrag = (event: DragEvent) => {
      if (event.target instanceof HTMLImageElement) event.preventDefault()
    }
    document.addEventListener('dragstart', preventNativeImageDrag, true)

    return () => {
      document.documentElement.classList.remove('ff-game-active')
      document.body.classList.remove('ff-game-active')
      document.removeEventListener('dragstart', preventNativeImageDrag, true)
    }
  }, [isPlayableGameRoute])

  return (
    <div className={appShellClassName}>
      <GameLeaveGuardProvider><AccountAccessBoundary><AppRoutes
        pathname={pathname}
        slotRoute={slotRoute}
      /></AccountAccessBoundary></GameLeaveGuardProvider>
    </div>
  )
}
