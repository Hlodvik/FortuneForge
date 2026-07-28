import { useLayoutEffect } from 'react'

const pageTitles: Record<string, string> = {
  '/': 'Fortune Forge — Play Above the Clouds',
  '/slots': 'Choose a Slot Machine — Fortune Forge',
  '/slots/wukong': "Wukong's Journey to the West — Fortune Forge",
  '/slots/rainbow-realm': 'Rainbow Realm — Fortune Forge',
  '/slots/rainbow-realm-preview': 'Rainbow Realm Cabinet Preview — Fortune Forge',
  '/create-account': 'Create Account — Fortune Forge',
  '/login': 'Log In — Fortune Forge',
  '/verify-email': 'Verify Email — Fortune Forge',
  '/home': 'Home — Fortune Forge',
  '/home/settings': 'Account Settings — Fortune Forge',
  '/home/history': 'User History — Fortune Forge',
  '/home/credits': 'Credits and Withdrawals — Fortune Forge',
  '/home/invoices': 'Payment Invoices — Fortune Forge',
  '/admin/invoices': 'Customer Invoices — Fortune Forge',
}

export function usePageTitle(pathname: string) {
  useLayoutEffect(() => {
    document.title =
      pathname.startsWith('/home/invoices/') ||
      pathname.startsWith('/admin/invoices/')
        ? 'Payment Invoice — Fortune Forge'
        : (pageTitles[pathname] ?? 'Page Not Found — Fortune Forge')
  }, [pathname])
}
