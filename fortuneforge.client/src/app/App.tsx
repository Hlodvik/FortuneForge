import { useEffect } from 'react'
import { findSlotRoute } from '../games/slots'
import { AppRoutes } from './AppRoutes'
import { usePageTitle } from './usePageTitle'
import { useGamePresence } from '../features/catalog/useGamePresence'
import { AccountAccessBoundary } from '../features/account/AccountAccessBoundary'
import { GameLeaveGuardProvider } from '../features/navigation/GameLeaveGuard'
import './styles/index.css'

export default function App() {
  const pathname = window.location.pathname.replace(/\/+$/, '') || '/'
  const slotRoute = findSlotRoute(pathname)
  const isPlayableGameRoute = slotRoute !== null || (
    pathname.startsWith('/games/') ||
    pathname.startsWith('/cards/') ||
    pathname.startsWith('/demo/cards/')
  )
  const usesThemeSpecificSlotBackdrop =
    slotRoute?.shellBackdrop === 'theme'
  const appShellClassName = [
    'app-shell',
    pathname === '/' ? 'app-shell--landing' : '',
    pathname === '/home' || pathname.startsWith('/home/') || pathname === '/balance' ? 'app-shell--home' : '',
    isPlayableGameRoute ? 'app-shell--game' : '',
    pathname.startsWith('/slots/') ? 'app-shell--slot-game' : '',
    pathname === '/cards' || pathname.startsWith('/cards/') || pathname === '/demo/cards' || pathname.startsWith('/demo/cards/')
      ? 'app-shell--card-room'
      : '',
    usesThemeSpecificSlotBackdrop ? 'app-shell--themed-slot-backdrop' : '',
  ]
    .filter(Boolean)
    .join(' ')

  usePageTitle(pathname)
  useGamePresence(pathname)

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
