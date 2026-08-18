import { useLayoutEffect } from 'react'
import { findSlotRoute } from '../games/slots'

const pageTitles: Record<string, string> = {
  '/': 'Fortune Forge — Play Above the Clouds',
  '/slots': 'Choose a Slot Machine — Fortune Forge',
  '/demo': 'Choose a Demo — Fortune Forge',
  '/cards': 'Choose a Card Game — Fortune Forge',
  '/games': 'Other Games — Fortune Forge',
  '/demo/cards': 'Choose a Card Game Demo — Fortune Forge',
  '/cards/blackjack': 'Credit Blackjack Table — Fortune Forge',
  '/demo/cards/blackjack': 'Blackjack Demo — Fortune Forge',
  '/demo/cards/blackjack/bot-practice': 'Blackjack Practice Lab — Fortune Forge',
  '/cards/texas-holdem': 'Credit Texas Hold’em — Fortune Forge',
  '/demo/cards/texas-holdem': 'Texas Hold’em Demo — Fortune Forge',
  '/demo/cards/texas-holdem/bot-practice': 'Texas Hold’em Practice Lab — Fortune Forge',
  '/cards/solitaire': 'Competitive Solitaire — Fortune Forge',
  '/demo/cards/solitaire/bot-practice': 'Solitaire Practice Lab — Fortune Forge',
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
  '/admin/operations': 'Operations — Fortune Forge',
}

export function usePageTitle(pathname: string) {
  useLayoutEffect(() => {
    document.title = pageTitleForPath(pathname)
  }, [pathname])
}

export function pageTitleForPath(pathname: string): string {
  const slotRoute = findSlotRoute(pathname)
  return slotRoute !== null
    ? `${slotRoute.shortTitle}${slotRoute.demoPath === pathname ? ' Demo' : ''} — Fortune Forge`
    : pathname.startsWith('/home/invoices/') ||
      pathname.startsWith('/admin/invoices/')
      ? 'Payment Invoice — Fortune Forge'
      : (pageTitles[pathname] ?? 'Page Not Found — Fortune Forge')
}
