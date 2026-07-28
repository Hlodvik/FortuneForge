import { AdminPaymentInvoicesPage } from '../features/payments/AdminPaymentInvoicesPage'
import { PaymentInvoicePage } from '../features/payments/PaymentInvoicePage'
import { PaymentInvoicesPage } from '../features/payments/PaymentInvoicesPage'
import { PurchaseCreditsPage } from '../features/payments/PurchaseCreditsPage'
import { AccountHistoryPage } from '../features/landing/AccountHistoryPage'
import { HomePage } from '../features/landing/AccountHomePage'
import { AccountSettingsPage } from '../features/landing/AccountSettingsPage'
import { CreateAccountPage } from '../features/landing/CreateAccountPage'
import { LandingPage } from '../features/landing/LandingPage'
import { LoginPage } from '../features/landing/LoginPage'
import { VerifyEmailPage } from '../features/landing/VerifyEmailPage'
import { RainbowRealmMachinePreview } from '../features/slots/RainbowRealmMachinePreview'
import type { SlotExperienceSet } from '../features/slots/config/slotExperienceSets'
import {
  AuthenticatedGameLibraryRoute,
  AuthenticatedSlotsRoute,
} from './AuthenticatedGameRoute'

export function AppRoutes({
  pathname,
  slotExperienceSet,
  onSpinStateChange,
}: {
  pathname: string
  slotExperienceSet: SlotExperienceSet | null
  onSpinStateChange: (isSpinning: boolean) => void
}) {
  const invoiceMatch = pathname.match(/^\/home\/invoices\/([A-Za-z0-9]+)$/)
  const adminInvoiceMatch = pathname.match(
    /^\/admin\/invoices\/([A-Za-z0-9]+)$/,
  )

  if (pathname === '/') return <LandingPage />
  if (pathname === '/slots') return <AuthenticatedGameLibraryRoute />
  if (slotExperienceSet !== null) {
    return (
      <AuthenticatedSlotsRoute
        experienceSet={slotExperienceSet}
        onSpinStateChange={onSpinStateChange}
        returnPath={pathname}
      />
    )
  }
  if (pathname === '/slots/rainbow-realm-preview') {
    return <RainbowRealmMachinePreview />
  }
  if (pathname === '/create-account') return <CreateAccountPage />
  if (pathname === '/login') return <LoginPage />
  if (pathname === '/verify-email') return <VerifyEmailPage />
  if (pathname === '/home') return <HomePage />
  if (pathname === '/home/settings') return <AccountSettingsPage />
  if (pathname === '/home/history') return <AccountHistoryPage />
  if (pathname === '/home/credits') return <PurchaseCreditsPage />
  if (pathname === '/home/invoices') return <PaymentInvoicesPage />
  if (invoiceMatch !== null) {
    return <PaymentInvoicePage invoiceId={invoiceMatch[1]} />
  }
  if (pathname === '/admin/invoices') return <AdminPaymentInvoicesPage />
  if (adminInvoiceMatch !== null) {
    return <PaymentInvoicePage invoiceId={adminInvoiceMatch[1]} adminView />
  }
  return <NotFoundPage />
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
