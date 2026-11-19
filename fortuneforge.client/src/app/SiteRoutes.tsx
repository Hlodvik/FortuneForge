// Edited: 2026-11-18T22:49:02-06:00 | Computer: Vinnabok
import { lazy, useEffect, type ReactNode } from 'react'
import { SiteRouteLayout } from './SiteRouteLayout'

const LandingPage = lazy(() => import('../pages/landing/LandingPage').then((module) => ({ default: module.LandingPage })))
const CreateAccountPage = lazy(() => import('../pages/auth/CreateAccountPage').then((module) => ({ default: module.CreateAccountPage })))
const LoginPage = lazy(() => import('../pages/auth/LoginPage').then((module) => ({ default: module.LoginPage })))
const VerifyEmailPage = lazy(() => import('../pages/auth/VerifyEmailPage').then((module) => ({ default: module.VerifyEmailPage })))
const HomePage = lazy(() => import('../pages/account/AccountHomePage').then((module) => ({ default: module.HomePage })))
const AccountPasswordPage = lazy(() => import('../pages/account/AccountPasswordPage').then((module) => ({ default: module.AccountPasswordPage })))
const AccountReviewPage = lazy(() => import('../pages/account/AccountReviewPage').then((module) => ({ default: module.AccountReviewPage })))
const PurchaseCreditsPage = lazy(() => import('../pages/payments/PurchaseCreditsPage').then((module) => ({ default: module.PurchaseCreditsPage })))
const PaymentInvoicesPage = lazy(() => import('../pages/payments/PaymentInvoicesPage').then((module) => ({ default: module.PaymentInvoicesPage })))
const PaymentInvoicePage = lazy(() => import('../pages/payments/PaymentInvoicePage').then((module) => ({ default: module.PaymentInvoicePage })))
const AdminPaymentInvoicesPage = lazy(() => import('../pages/payments/AdminPaymentInvoicesPage').then((module) => ({ default: module.AdminPaymentInvoicesPage })))
const AdminOperationsRoute = lazy(() => import('./routes/AdminOperationsRoute').then((module) => ({ default: module.AdminOperationsRoute })))
const AdminPortalRoute = lazy(() => import('./routes/AdminPortalRoute').then((module) => ({ default: module.AdminPortalRoute })))
const NotFoundPage = lazy(() => import('../pages/not-found/NotFoundPage').then((module) => ({ default: module.NotFoundPage })))

/** Site routes have no dependency on playable game clients. Null hands control to the game router. */
// eslint-disable-next-line react/only-export-components -- Shared resolver serves both entry points.
export function resolveSiteRoute(pathname: string): ReactNode | null {
  const invoiceMatch = pathname.match(/^\/home\/invoices\/([A-Za-z0-9]+)$/)
  const adminInvoiceMatch = pathname.match(/^\/admin\/invoices\/([A-Za-z0-9]+)$/)
  if (pathname === '/') return <LandingPage />
  if (pathname === '/slots') return <CatalogueHomeRedirect href="/home?category=Slots" />
  if (pathname === '/cards') return <CatalogueHomeRedirect href="/home?category=Cards" />
  if (pathname === '/games') return <CatalogueHomeRedirect href="/home" />
  if (pathname === '/create-account') return <CreateAccountPage />
  if (pathname === '/login') return <LoginPage />
  if (pathname === '/verify-email') return <VerifyEmailPage />
  if (pathname === '/account/review') return <AccountReviewPage />
  if (pathname === '/home' || pathname === '/home/settings' || pathname === '/home/history') return <HomePage />
  if (pathname === '/home/change-password') return <AccountPasswordPage />
  if (pathname === '/balance') return <PurchaseCreditsPage />
  if (pathname === '/home/rand' || pathname === '/home/credits') return <LegacyBalanceRedirect />
  if (pathname === '/home/invoices') return <PaymentInvoicesPage />
  if (invoiceMatch !== null) return <PaymentInvoicePage invoiceId={invoiceMatch[1]} />
  if (pathname === '/admin/invoices') return <AdminPaymentInvoicesPage />
  if (pathname === '/admin/operations') return <AdminOperationsRoute />
  if (pathname === '/admin') return <AdminPortalRoute />
  if (adminInvoiceMatch !== null) return <PaymentInvoicePage invoiceId={adminInvoiceMatch[1]} adminView />
  return null
}

export function SiteNotFoundPage() { return <NotFoundPage /> }

function CatalogueHomeRedirect({ href }: { href: string }) {
  useEffect(() => { window.location.replace(href) }, [href])
  return <SiteRouteLayout><main className="route-state" role="status">Opening your home…</main></SiteRouteLayout>
}

function LegacyBalanceRedirect() {
  useEffect(() => {
    window.location.replace(`/balance${window.location.search}${window.location.hash}`)
  }, [])
  return <SiteRouteLayout><main className="route-state" role="status">Opening your balance…</main></SiteRouteLayout>
}

