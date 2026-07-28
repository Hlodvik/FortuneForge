import { useCallback, useLayoutEffect, useRef } from 'react'
import { CreateAccountPage } from './features/landing/CreateAccountPage'
import { AccountHistoryPage } from './features/landing/AccountHistoryPage'
import { HomePage } from './features/landing/AccountHomePage'
import { AccountSettingsPage } from './features/landing/AccountSettingsPage'
import { LandingPage } from './features/landing/LandingPage'
import { LoginPage } from './features/landing/LoginPage'
import { VerifyEmailPage } from './features/landing/VerifyEmailPage'
import { GameLibraryPage } from './features/games/GameLibraryPage'
import { PurchaseCreditsPage } from './features/payments/PurchaseCreditsPage'
import { PaymentInvoicePage } from './features/payments/PaymentInvoicePage'
import { PaymentInvoicesPage } from './features/payments/PaymentInvoicesPage'
import { AdminPaymentInvoicesPage } from './features/payments/AdminPaymentInvoicesPage'
import { useAuthenticatedAccount } from './features/landing/useAuthenticatedAccount'
import { SlotsPage } from './features/slots/SlotsPage'
import { RainbowRealmMachinePreview } from './features/slots/RainbowRealmMachinePreview'
import {
  SLOT_EXPERIENCE_SETS_BY_ROUTE,
  type SlotExperienceSet,
} from './features/slots/config/slotExperienceSets'
import slotBackgroundVideoMp4 from './assets/slots/backgrounds/background-clouds-animated.mp4'
import slotBackgroundVideoWebm from './assets/slots/backgrounds/background-clouds-animated.webm'
import './App.css'

function App() {
  const pathname = window.location.pathname.replace(/\/+$/, '') || '/'
  const invoiceMatch = pathname.match(/^\/home\/invoices\/([A-Za-z0-9]+)$/)
  const adminInvoiceMatch = pathname.match(/^\/admin\/invoices\/([A-Za-z0-9]+)$/)
  const backgroundVideoRef = useRef<HTMLVideoElement | null>(null)
  const userAgent = window.navigator.userAgent
  const isGoogleChrome = /\bChrome\//.test(userAgent) && !/\b(?:Edg|OPR)\//.test(userAgent)
  const slotExperienceSet = pathname in SLOT_EXPERIENCE_SETS_BY_ROUTE
    ? SLOT_EXPERIENCE_SETS_BY_ROUTE[pathname]
    : null
  const isWukongSlotExperience = slotExperienceSet?.cabinet.id === 'wukong-celestial-arcade-v1'
  const usesThemeSpecificSlotBackdrop = slotExperienceSet !== null && !isWukongSlotExperience
  const shouldRenderWukongShellBackdrop = slotExperienceSet === null || isWukongSlotExperience
  const appShellClassName = [
    'app-shell',
    pathname.startsWith('/slots/') ? 'app-shell--slot-game' : '',
    usesThemeSpecificSlotBackdrop ? 'app-shell--themed-slot-backdrop' : '',
  ].filter(Boolean).join(' ')

  const handleSlotSpinStateChange = useCallback((isSpinning: boolean) => {
    const video = backgroundVideoRef.current
    if (!video || !isGoogleChrome) {
      return
    }

    if (isSpinning) {
      video.pause()
      return
    }

    void video.play().catch(() => undefined)
  }, [isGoogleChrome])

  useLayoutEffect(() => {
    const titles: Record<string, string> = {
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
    document.title = pathname.startsWith('/home/invoices/') || pathname.startsWith('/admin/invoices/')
      ? 'Payment Invoice — Fortune Forge'
      : (titles[pathname] ?? 'Page Not Found — Fortune Forge')
  }, [pathname])

  let page = <NotFoundPage />

  if (pathname === '/') {
    page = <LandingPage />
  } else if (pathname === '/slots') {
    page = <AuthenticatedGameLibraryRoute />
  } else if (slotExperienceSet !== null) {
    page = (
      <AuthenticatedSlotsRoute
        experienceSet={slotExperienceSet}
        onSpinStateChange={handleSlotSpinStateChange}
        returnPath={pathname}
      />
    )
  } else if (pathname === '/slots/rainbow-realm-preview') {
    page = <RainbowRealmMachinePreview />
  } else if (pathname === '/create-account') {
    page = <CreateAccountPage />
  } else if (pathname === '/login') {
    page = <LoginPage />
  } else if (pathname === '/verify-email') {
    page = <VerifyEmailPage />
  } else if (pathname === '/home') {
    page = <HomePage />
  } else if (pathname === '/home/settings') {
    page = <AccountSettingsPage />
  } else if (pathname === '/home/history') {
    page = <AccountHistoryPage />
  } else if (pathname === '/home/credits') {
    page = <PurchaseCreditsPage />
  } else if (pathname === '/home/invoices') {
    page = <PaymentInvoicesPage />
  } else if (invoiceMatch !== null) {
    page = <PaymentInvoicePage invoiceId={invoiceMatch[1]} />
  } else if (pathname === '/admin/invoices') {
    page = <AdminPaymentInvoicesPage />
  } else if (adminInvoiceMatch !== null) {
    page = <PaymentInvoicePage invoiceId={adminInvoiceMatch[1]} adminView />
  }

  return (
    <div className={appShellClassName}>
      {shouldRenderWukongShellBackdrop && (
        <video
          ref={backgroundVideoRef}
          className="app-shell__background-video"
          autoPlay
          loop
          muted
          playsInline
          preload="auto"
          aria-hidden="true"
        >
          <source
            src={slotBackgroundVideoMp4}
            type="video/mp4"
            media="(prefers-reduced-motion: no-preference)"
          />
          <source
            src={slotBackgroundVideoWebm}
            type="video/webm"
            media="(prefers-reduced-motion: no-preference)"
          />
        </video>
      )}
      {page}
    </div>
  )
}

type AuthenticatedSlotsRouteProps = {
  experienceSet: SlotExperienceSet
  onSpinStateChange: (isSpinning: boolean) => void
  returnPath: string
}

function AuthenticatedSlotsRoute({
  experienceSet,
  onSpinStateChange,
  returnPath,
}: AuthenticatedSlotsRouteProps) {
  const { account, error, isLoading, reload } = useAuthenticatedAccount(returnPath)

  if (isLoading || account === null) {
    return (
      <div className="player-page">
        <main className="player-main">
          {error === null
            ? <div className="player-state" role="status">Opening Fortune Slots…</div>
            : (
              <div className="player-state player-state--error" role="alert">
                <strong>Fortune Slots could not be opened.</strong>
                <span>{error}</span>
                <button className="landing-button landing-button--secondary" type="button" onClick={reload}>
                  Try again
                </button>
                <a href="/home">Return home</a>
              </div>
            )}
        </main>
      </div>
    )
  }

  return (
    <SlotsPage
      account={account}
      experienceSet={experienceSet}
      onSpinStateChange={onSpinStateChange}
    />
  )
}

function AuthenticatedGameLibraryRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/slots')

  if (isLoading || account === null) {
    return (
      <div className="player-page">
        <main className="player-main">
          {error === null
            ? <div className="player-state" role="status">Opening the slot collection…</div>
            : (
              <div className="player-state player-state--error" role="alert">
                <strong>The slot collection could not be opened.</strong>
                <span>{error}</span>
                <button className="landing-button landing-button--secondary" type="button" onClick={reload}>
                  Try again
                </button>
                <a href="/home">Return home</a>
              </div>
            )}
        </main>
      </div>
    )
  }

  return <GameLibraryPage account={account} />
}

function NotFoundPage() {
  return (
    <div className="player-page">
      <main className="player-main">
        <div className="player-state player-state--error" role="alert">
          <strong>Page not found.</strong>
          <span>The Fortune Forge page you requested does not exist.</span>
          <a href="/">Return to the landing page</a>
        </div>
      </main>
    </div>
  )
}

export default App
