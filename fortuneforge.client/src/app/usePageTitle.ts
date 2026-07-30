import { useLayoutEffect } from 'react'
import { findSlotGameManifestByRoute } from '../features/slots/games'

const pageTitles: Record<string, string> = {
  '/': 'Fortune Forge — Play Above the Clouds',
  '/slots': 'Choose a Slot Machine — Fortune Forge',
  '/demo': 'Choose a Demo — Fortune Forge',
  '/slots/rainbow-realm-preview': 'Rainbow Realm Cabinet Preview — Fortune Forge',
  '/create-account': 'Create Account — Fortune Forge',
  '/login': 'Log In — Fortune Forge',
  '/verify-email': 'Verify Email — Fortune Forge',
  '/home': 'Home — Fortune Forge',
  '/home/settings': 'Account Settings — Fortune Forge',
  '/home/history': 'User History — Fortune Forge',
  '/home/rand': 'Rand balance and withdrawals — Fortune Forge',
  '/home/credits': 'Rand balance and withdrawals — Fortune Forge',
  '/home/invoices': 'Payment Invoices — Fortune Forge',
  '/admin/invoices': 'Customer Invoices — Fortune Forge',
}

export function usePageTitle(pathname: string) {
  useLayoutEffect(() => {
    const slotGame = findSlotGameManifestByRoute(pathname)
    document.title =
      slotGame !== null
        ? `${slotGame.catalog.shortTitle}${slotGame.routes.demo === pathname ? ' Demo' : ''} — Fortune Forge`
        : pathname.startsWith('/home/invoices/') ||
      pathname.startsWith('/admin/invoices/')
        ? 'Payment Invoice — Fortune Forge'
        : (pageTitles[pathname] ?? 'Page Not Found — Fortune Forge')
  }, [pathname])
}
