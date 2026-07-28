import { useState, type FormEvent } from 'react'
import { ForgeCreditAmount } from '../../components/ForgeCreditAmount'
import { PaymentAlertsMenu } from '../../components/PaymentAlertsMenu'
import { useAuthenticatedAccount } from '../landing/useAuthenticatedAccount'
import '../landing/landing.css'
import { listAdminPaymentInvoices, type PaymentCheckout } from './services/paymentsApi'

export function AdminPaymentInvoicesPage() {
  const { account, error: accountError, isLoading, reload } =
    useAuthenticatedAccount('/admin/invoices')
  const [userId, setUserId] = useState('')
  const [searchedUserId, setSearchedUserId] = useState('')
  const [invoices, setInvoices] = useState<PaymentCheckout[] | null>(null)
  const [searchError, setSearchError] = useState<string | null>(null)
  const [isSearching, setIsSearching] = useState(false)

  async function handleSearch(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (userId === '') {
      setSearchError('Enter the user ID linked to the customer account.')
      return
    }

    setIsSearching(true)
    setSearchError(null)
    try {
      setInvoices(await listAdminPaymentInvoices(userId))
      setSearchedUserId(userId)
    } catch (error) {
      setInvoices(null)
      setSearchError(error instanceof Error ? error.message : 'Invoices are unavailable.')
    } finally {
      setIsSearching(false)
    }
  }

  return (
    <div className="player-page player-page--credits">
      <header className="landing-bar">
        <a className="landing-brand" href="/" aria-label="Fortune Forge home"><span className="landing-brand__spark" aria-hidden="true">✦</span><span>Fortune Forge</span></a>
        <div className="landing-bar__account">
          {account !== null && <ForgeCreditAmount amount={account.balances.slotsCredits} />}
          <PaymentAlertsMenu />
          <nav className="landing-nav" aria-label="Admin invoice navigation"><a className="landing-nav__link" href="/home">Home</a></nav>
        </div>
      </header>
      <main className="invoice-main">
        {isLoading && <div className="player-state" role="status">Opening finance invoices…</div>}
        {!isLoading && accountError !== null && <div className="player-state player-state--error" role="alert"><strong>Admin invoices could not be opened.</strong><span>{accountError}</span><button className="landing-button landing-button--secondary" type="button" onClick={reload}>Try again</button></div>}
        {!isLoading && account !== null && account.role.toLowerCase() !== 'admin' && <div className="player-state player-state--error" role="alert"><strong>Admin access required.</strong><span>Your account cannot view other customers’ invoices.</span><a href="/home">Return home</a></div>}
        {!isLoading && account?.role.toLowerCase() === 'admin' && (
          <section className="invoice-list admin-invoices" aria-labelledby="admin-invoices-title">
            <header><div><p className="account-eyebrow">Finance administration</p><h1 id="admin-invoices-title">Customer invoices</h1><p>Search by the immutable user ID linked to the customer’s user document.</p></div></header>
            <form className="admin-invoices__search" onSubmit={(event) => void handleSearch(event)}>
              <label htmlFor="admin-invoice-user"><span>Customer user ID</span><input id="admin-invoice-user" type="text" autoComplete="off" value={userId} onChange={(event) => setUserId(event.target.value.replace(/[^A-Za-z0-9_-]/g, '').slice(0, 128))} /></label>
              <button className="landing-button landing-button--gold" type="submit" disabled={isSearching || userId === ''}>{isSearching ? 'Searching…' : 'Find invoices'}</button>
            </form>
            {searchError !== null && <p className="payment-checkout__error" role="alert">{searchError}</p>}
            {invoices !== null && invoices.length === 0 && <div className="invoice-list__empty"><strong>No invoices found.</strong><span>User {searchedUserId} has no payment orders.</span></div>}
            {invoices !== null && invoices.length > 0 && (
              <div className="invoice-list__items">
                {invoices.map((invoice) => (
                  <a className="invoice-list__row" key={invoice.checkoutId} href={`/admin/invoices/${encodeURIComponent(invoice.invoiceId)}`}>
                    <span className={`invoice-list__status invoice-list__status--${invoice.status}`} aria-hidden="true" />
                    <span><small>Invoice</small><strong>{invoice.invoiceId}</strong></span>
                    <span><small>Date</small><strong>{new Date(invoice.createdAtUtc).toLocaleDateString()}</strong></span>
                    <span><small>Total</small><strong>{formatMoney(invoice)}</strong></span>
                    <span><small>Credits</small><strong>{invoice.credits.toLocaleString()}</strong></span>
                    <em>{invoice.status}</em><b aria-hidden="true">›</b>
                  </a>
                ))}
              </div>
            )}
          </section>
        )}
      </main>
    </div>
  )
}

function formatMoney(invoice: PaymentCheckout) {
  return new Intl.NumberFormat(invoice.locale, { style: 'currency', currency: invoice.currency }).format(invoice.amountMinor / 100)
}
