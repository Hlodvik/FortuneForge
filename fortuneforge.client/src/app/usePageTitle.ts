import { useLayoutEffect } from 'react'
import { findSlotRoute } from '../games/slots'

const pageTitles: Record<string, string> = {
  '/': 'Fortune Forge — Play Your Way',
  '/slots': 'Choose a Slot Machine — Fortune Forge',
  '/demo': 'Choose a Demo — Fortune Forge',
  '/cards': 'Choose a Card Game — Fortune Forge',
  '/games': 'Other Games — Fortune Forge',
  '/games/asteroids': 'Asteroids Competition — Fortune Forge',
  '/games/2048': '2048 — Fortune Forge',
  '/games/drop-merge': 'Drop Merge — Fortune Forge',
  '/games/baccarat': 'Baccarat — Fortune Forge',
  '/games/casino-war': 'Casino War — Fortune Forge',
  '/games/keno': 'Keno — Fortune Forge',
  '/games/sic-bo': 'Sic Bo — Fortune Forge',
  '/games/flappy': 'Flappy — Fortune Forge',
  '/games/horse-flight': 'Horse Flight — Fortune Forge',
  '/games/snake': 'Snake — Fortune Forge',
  '/games/video-poker': 'Video Poker — Fortune Forge',
  '/games/roulette': 'Roulette — Fortune Forge',
  '/games/craps': 'Craps — Fortune Forge',
  '/games/liars-dice': 'Liar’s Dice — Fortune Forge',
  '/demo/cards': 'Choose a Card Game Demo — Fortune Forge',
  '/cards/hearts': 'Hearts — Fortune Forge',
  '/cards/blackjack': 'Blackjack Table — Fortune Forge',
  '/demo/cards/blackjack': 'Blackjack Demo — Fortune Forge',
  '/demo/cards/blackjack/bot-practice': 'Blackjack Table — Fortune Forge',
  '/cards/texas-holdem': 'Texas Hold’em Table — Fortune Forge',
  '/demo/cards/texas-holdem': 'Texas Hold’em Demo — Fortune Forge',
  '/demo/cards/texas-holdem/bot-practice': 'Texas Hold’em Table — Fortune Forge',
  '/cards/solitaire': 'Competitive Solitaire — Fortune Forge',
  '/demo/cards/solitaire/bot-practice': 'Solitaire Race — Fortune Forge',
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
