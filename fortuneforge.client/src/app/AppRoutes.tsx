import { Component, lazy, Suspense, type ReactNode } from 'react'
import type { SlotRouteDefinition } from '../games/slots'

const LandingPage = lazy(() => import('../pages/landing/LandingPage').then((module) => ({ default: module.LandingPage })))
const DemoSlotsLibraryPage = lazy(() => import('../pages/slots/SlotsLibraryPage').then((module) => ({ default: module.DemoSlotsLibraryPage })))
const DemoCardLibraryPage = lazy(() => import('../pages/cards/CardGameLibraryPage').then((module) => ({ default: module.CardGameLibraryPage })))
const DemoBlackjackPage = lazy(() => import('../pages/cards/blackjack/BlackjackPage').then((module) => ({ default: module.BlackjackPage })))
const DemoBlackjackBotPracticePage = lazy(() => import('../pages/cards/blackjack/BlackjackBotPracticePage').then((module) => ({ default: module.BlackjackBotPracticePage })))
const DemoTexasHoldemPage = lazy(() => import('../pages/cards/texasHoldem/TexasHoldemPage').then((module) => ({ default: module.TexasHoldemPage })))
const DemoTexasHoldemBotPracticePage = lazy(() => import('../pages/cards/texasHoldem/TexasHoldemBotPracticePage').then((module) => ({ default: module.TexasHoldemBotPracticePage })))
const DemoSolitaireBotPracticePage = lazy(() => import('../pages/cards/solitaire/SolitaireBotPracticePage').then((module) => ({ default: module.SolitaireBotPracticePage })))
const AuthenticatedSlotsLibraryRoute = lazy(() => import('./routes/SlotsLibraryRoute').then((module) => ({ default: module.AuthenticatedSlotsLibraryRoute })))
const AuthenticatedCardLibraryRoute = lazy(() => import('./routes/CardLibraryRoute').then((module) => ({ default: module.AuthenticatedCardLibraryRoute })))
const AuthenticatedGameHubRoute = lazy(() => import('./routes/GameHubRoute').then((module) => ({ default: module.AuthenticatedGameHubRoute })))
const AuthenticatedBlackjackRoute = lazy(() => import('./routes/BlackjackRoute').then((module) => ({ default: module.AuthenticatedBlackjackRoute })))
const AuthenticatedTexasHoldemRoute = lazy(() => import('./routes/TexasHoldemRoute').then((module) => ({ default: module.AuthenticatedTexasHoldemRoute })))
const AuthenticatedSolitaireRoute = lazy(() => import('./routes/SolitaireRoute').then((module) => ({ default: module.AuthenticatedSolitaireRoute })))
const SlotGameRoute = lazy(() => import('./routes/SlotGameRoute').then((module) => ({ default: module.SlotGameRoute })))
const RainbowRealmMachinePreview = lazy(() => import('../pages/slots/RainbowRealmMachinePreview').then((module) => ({ default: module.RainbowRealmMachinePreview })))
const CreateAccountPage = lazy(() => import('../pages/auth/CreateAccountPage').then((module) => ({ default: module.CreateAccountPage })))
const LoginPage = lazy(() => import('../pages/auth/LoginPage').then((module) => ({ default: module.LoginPage })))
const VerifyEmailPage = lazy(() => import('../pages/auth/VerifyEmailPage').then((module) => ({ default: module.VerifyEmailPage })))
const HomePage = lazy(() => import('../pages/account/AccountHomePage').then((module) => ({ default: module.HomePage })))
const AccountSettingsPage = lazy(() => import('../pages/account/AccountSettingsPage').then((module) => ({ default: module.AccountSettingsPage })))
const AccountHistoryPage = lazy(() => import('../pages/account/AccountHistoryPage').then((module) => ({ default: module.AccountHistoryPage })))
const PurchaseCreditsPage = lazy(() => import('../pages/payments/PurchaseCreditsPage').then((module) => ({ default: module.PurchaseCreditsPage })))
const PaymentInvoicesPage = lazy(() => import('../pages/payments/PaymentInvoicesPage').then((module) => ({ default: module.PaymentInvoicesPage })))
const PaymentInvoicePage = lazy(() => import('../pages/payments/PaymentInvoicePage').then((module) => ({ default: module.PaymentInvoicePage })))
const AdminPaymentInvoicesPage = lazy(() => import('../pages/payments/AdminPaymentInvoicesPage').then((module) => ({ default: module.AdminPaymentInvoicesPage })))
const AdminOperationsRoute = lazy(() => import('./routes/AdminOperationsRoute').then((module) => ({ default: module.AdminOperationsRoute })))
const NotFoundPage = lazy(() => import('../pages/not-found/NotFoundPage').then((module) => ({ default: module.NotFoundPage })))

export function AppRoutes({
  pathname,
  slotRoute,
  onSpinStateChange,
}: {
  pathname: string
  slotRoute: SlotRouteDefinition | null
  onSpinStateChange: (isSpinning: boolean) => void
}) {
  const invoiceMatch = pathname.match(/^\/home\/invoices\/([A-Za-z0-9]+)$/)
  const adminInvoiceMatch = pathname.match(/^\/admin\/invoices\/([A-Za-z0-9]+)$/)
  let route: ReactNode

  if (pathname === '/') route = <LandingPage />
  else if (pathname === '/demo') route = <DemoSlotsLibraryPage />
  else if (pathname === '/demo/cards') route = <DemoCardLibraryPage demoMode />
  else if (pathname === '/demo/cards/blackjack') route = <DemoBlackjackPage demoMode />
  else if (pathname === '/demo/cards/blackjack/bot-practice') route = <DemoBlackjackBotPracticePage />
  else if (pathname === '/demo/cards/texas-holdem') route = <DemoTexasHoldemPage demoMode returnHref="/demo/cards" />
  else if (pathname === '/demo/cards/texas-holdem/bot-practice') route = <DemoTexasHoldemBotPracticePage />
  else if (pathname === '/demo/cards/solitaire/bot-practice') route = <DemoSolitaireBotPracticePage />
  else if (pathname === '/slots') route = <AuthenticatedSlotsLibraryRoute />
  else if (pathname === '/cards') route = <AuthenticatedCardLibraryRoute />
  else if (pathname === '/games') route = <AuthenticatedGameHubRoute />
  else if (pathname === '/cards/blackjack') route = <AuthenticatedBlackjackRoute />
  else if (pathname === '/cards/texas-holdem') route = <AuthenticatedTexasHoldemRoute />
  else if (pathname === '/cards/solitaire') route = <AuthenticatedSolitaireRoute />
  else if (slotRoute !== null) {
    route = <SlotGameRoute definition={slotRoute} pathname={pathname} onSpinStateChange={onSpinStateChange} />
  }
  else if (pathname === '/slots/rainbow-realm-preview') route = <RainbowRealmMachinePreview />
  else if (pathname === '/create-account') route = <CreateAccountPage />
  else if (pathname === '/login') route = <LoginPage />
  else if (pathname === '/verify-email') route = <VerifyEmailPage />
  else if (pathname === '/home') route = <HomePage />
  else if (pathname === '/home/settings') route = <AccountSettingsPage />
  else if (pathname === '/home/history') route = <AccountHistoryPage />
  else if (pathname === '/home/rand' || pathname === '/home/credits') route = <PurchaseCreditsPage />
  else if (pathname === '/home/invoices') route = <PaymentInvoicesPage />
  else if (invoiceMatch !== null) route = <PaymentInvoicePage invoiceId={invoiceMatch[1]} />
  else if (pathname === '/admin/invoices') route = <AdminPaymentInvoicesPage />
  else if (pathname === '/admin/operations') route = <AdminOperationsRoute />
  else if (adminInvoiceMatch !== null) route = <PaymentInvoicePage invoiceId={adminInvoiceMatch[1]} adminView />
  else route = <NotFoundPage />

  return (
    <RouteErrorBoundary key={pathname}>
      <Suspense fallback={<RouteLoadingState />}>{route}</Suspense>
    </RouteErrorBoundary>
  )
}

function RouteLoadingState() {
  return <main className="route-state" role="status">Opening Fortune Forge…</main>
}

class RouteErrorBoundary extends Component<{ children: ReactNode }, { failed: boolean }> {
  state = { failed: false }

  static getDerivedStateFromError() {
    return { failed: true }
  }

  render() {
    if (!this.state.failed) return this.props.children
    return (
      <main className="route-state route-state--error" role="alert">
        <strong>This page could not be loaded.</strong>
        <span>Check your connection, then try loading the page again.</span>
        <button type="button" onClick={() => window.location.reload()}>Try again</button>
        <a href="/">Return to Fortune Forge</a>
      </main>
    )
  }
}
