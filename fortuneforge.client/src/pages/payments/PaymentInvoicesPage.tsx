import { useCallback, useEffect, useState } from 'react'
import { PaymentAlertsMenu } from '../../components/PaymentAlertsMenu'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { listPaymentInvoices, type PaymentCheckout } from '../../features/payments/services/paymentsApi'
import { AccountLobbyShell } from '../account/AccountLobbyShell'
import '../index.css'
import './invoiceTheme.css'

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
    <AccountLobbyShell account={account} activeNav={null}>
      <div className="invoice-main">
        <nav className="invoice-page__navigation" aria-label="Invoice history navigation"><a href="/balance">← Balance</a><PaymentAlertsMenu /></nav>
        {isAccountLoading && <div className="player-state" role="status">Opening your invoices…</div>}
        {!isAccountLoading && accountError !== null && <div className="player-state player-state--error" role="alert"><strong>Invoices could not be opened.</strong><span>{accountError}</span><button className="landing-button landing-button--secondary" type="button" onClick={reload}>Try again</button></div>}
        {!isAccountLoading && account !== null && invoices === null && invoiceError === null && <div className="player-state" role="status">Loading invoices…</div>}
        {!isAccountLoading && account !== null && invoiceError !== null && <div className="player-state player-state--error" role="alert"><strong>Invoices are unavailable.</strong><span>{invoiceError}</span><button className="landing-button landing-button--secondary" type="button" onClick={() => void loadInvoices()}>Try again</button></div>}
        {!isAccountLoading && account !== null && invoices !== null && (
          <section className="invoice-list" aria-labelledby="invoice-list-title">
            <header><div><p className="account-eyebrow">Payment history</p><h1 id="invoice-list-title">Your invoices</h1><p>Open any order to view its live status and receipt.</p></div><a className="landing-button landing-button--gold" href="/balance">Add Rand</a></header>
            {invoices.length === 0
              ? <div className="invoice-list__empty"><strong>No invoices yet.</strong><span>Your payment orders will appear here.</span></div>
              : <div className="invoice-list__items">{invoices.map((invoice) => <InvoiceRow key={invoice.checkoutId} invoice={invoice} />)}</div>}
          </section>
        )}
      </div>
    </AccountLobbyShell>
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
