import { useCallback, useEffect, useState } from 'react'
import { ForgeCreditAmount } from '../../components/ForgeCreditAmount'
import { PaymentAlertsMenu } from '../../components/PaymentAlertsMenu'
import { useAuthenticatedAccount } from '../landing/useAuthenticatedAccount'
import '../landing/landing.css'
import { listPaymentInvoices, type PaymentCheckout } from './services/paymentsApi'

export function PaymentInvoicesPage() {
  const { account, error: accountError, isLoading: isAccountLoading, reload } =
    useAuthenticatedAccount('/home/invoices')
  const [invoices, setInvoices] = useState<PaymentCheckout[] | null>(null)
  const [invoiceError, setInvoiceError] = useState<string | null>(null)

  const loadInvoices = useCallback(async () => {
    try {
      setInvoices(await listPaymentInvoices(50))
      setInvoiceError(null)
    } catch (error) {
      setInvoiceError(error instanceof Error ? error.message : 'Invoices are unavailable.')
    }
  }, [])

  useEffect(() => {
    if (account !== null) {
      void loadInvoices()
    }
  }, [account, loadInvoices])

  return (
    <div className="player-page player-page--credits">
      <header className="landing-bar">
        <a className="landing-brand" href="/" aria-label="Fortune Forge home"><span className="landing-brand__spark" aria-hidden="true">✦</span><span>Fortune Forge</span></a>
        <div className="landing-bar__account">
          {account !== null && <ForgeCreditAmount amount={account.balances.slotsCredits} />}
          <PaymentAlertsMenu />
          <nav className="landing-nav" aria-label="Invoice history navigation"><a className="landing-nav__link" href="/home/rand">Add Rand</a><a className="landing-nav__link" href="/home">Home</a></nav>
        </div>
      </header>
      <main className="invoice-main">
        {isAccountLoading && <div className="player-state" role="status">Opening your invoices…</div>}
        {!isAccountLoading && accountError !== null && <div className="player-state player-state--error" role="alert"><strong>Invoices could not be opened.</strong><span>{accountError}</span><button className="landing-button landing-button--secondary" type="button" onClick={reload}>Try again</button></div>}
        {!isAccountLoading && account !== null && invoices === null && invoiceError === null && <div className="player-state" role="status">Loading invoices…</div>}
        {!isAccountLoading && account !== null && invoiceError !== null && <div className="player-state player-state--error" role="alert"><strong>Invoices are unavailable.</strong><span>{invoiceError}</span><button className="landing-button landing-button--secondary" type="button" onClick={() => void loadInvoices()}>Try again</button></div>}
        {!isAccountLoading && account !== null && invoices !== null && (
          <section className="invoice-list" aria-labelledby="invoice-list-title">
            <header><div><p className="account-eyebrow">Payment history</p><h1 id="invoice-list-title">Your invoices</h1><p>Open any order to view its live status and receipt.</p></div><a className="landing-button landing-button--gold" href="/home/rand">Add Rand</a></header>
            {invoices.length === 0
              ? <div className="invoice-list__empty"><strong>No invoices yet.</strong><span>Your payment orders will appear here.</span></div>
              : <div className="invoice-list__items">{invoices.map((invoice) => <InvoiceRow key={invoice.checkoutId} invoice={invoice} />)}</div>}
          </section>
        )}
      </main>
    </div>
  )
}

function InvoiceRow({ invoice }: { invoice: PaymentCheckout }) {
  return (
    <a className="invoice-list__row" href={`/home/invoices/${encodeURIComponent(invoice.invoiceId)}`}>
      <span className={`invoice-list__status invoice-list__status--${invoice.status}`} aria-hidden="true" />
      <span><small>Invoice</small><strong>{invoice.invoiceId}</strong></span>
      <span><small>Date</small><strong>{new Date(invoice.createdAtUtc).toLocaleDateString()}</strong></span>
      <span><small>Total</small><strong>{new Intl.NumberFormat(invoice.locale, { style: 'currency', currency: invoice.currency }).format(invoice.amountMinor / 100)}</strong></span>
      <span><small>Rand added</small><strong>R{invoice.credits.toLocaleString()}</strong></span>
      <em>{invoice.status}</em><b aria-hidden="true">›</b>
    </a>
  )
}
