import { useEffect, useState } from 'react'
import { listPaymentInvoices, type PaymentCheckout } from '../features/payments/services/paymentsApi'
import './PaymentAlertsMenu.css'

export function PaymentAlertsMenu() {
  const [invoices, setInvoices] = useState<PaymentCheckout[]>([])
  const [error, setError] = useState<string | null>(null)
  const [isLoading, setIsLoading] = useState(true)

  useEffect(() => {
    let active = true

    async function refresh() {
      try {
        const nextInvoices = await listPaymentInvoices(8)
        if (active) {
          setInvoices(nextInvoices)
          setError(null)
        }
      } catch {
        if (active) {
          setError('Orders are temporarily unavailable.')
        }
      } finally {
        if (active) {
          setIsLoading(false)
        }
      }
    }

    void refresh()
    const handleInvoiceUpdate = () => void refresh()
    window.addEventListener('payment-invoices-updated', handleInvoiceUpdate)
    const interval = window.setInterval(() => void refresh(), 30_000)
    return () => {
      active = false
      window.removeEventListener('payment-invoices-updated', handleInvoiceUpdate)
      window.clearInterval(interval)
    }
  }, [])

  const activeCount = invoices.filter((invoice) =>
    invoice.status === 'received' || invoice.status === 'processing').length

  return (
    <details className="payment-alerts">
      <summary aria-label={`Payment alerts${activeCount > 0 ? `, ${activeCount} active` : ''}`}>
        <svg aria-hidden="true" viewBox="0 0 24 24">
          <path d="M18 8a6 6 0 0 0-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9M10 21h4" />
        </svg>
        {activeCount > 0 && <span>{activeCount > 9 ? '9+' : activeCount}</span>}
      </summary>
      <section className="payment-alerts__panel" aria-label="Recent payment orders">
        <header><div><small>Payment alerts</small><strong>Your invoices</strong></div><a href="/home/invoices">View all</a></header>
        {isLoading && <p className="payment-alerts__message">Loading orders…</p>}
        {!isLoading && error !== null && <p className="payment-alerts__message">{error}</p>}
        {!isLoading && error === null && invoices.length === 0 && (
          <p className="payment-alerts__message">No payment orders yet.</p>
        )}
        {!isLoading && error === null && invoices.length > 0 && (
          <ul>
            {invoices.map((invoice) => (
              <li key={invoice.checkoutId}>
                <a href={`/home/invoices/${encodeURIComponent(invoice.invoiceId)}`}>
                  <span className={`payment-alerts__status payment-alerts__status--${invoice.status}`} aria-hidden="true" />
                  <span><strong>{formatMoney(invoice)}</strong><small>{formatStatus(invoice.status)} · {new Date(invoice.createdAtUtc).toLocaleDateString()}</small></span>
                  <span aria-hidden="true">›</span>
                </a>
              </li>
            ))}
          </ul>
        )}
      </section>
    </details>
  )
}

function formatMoney(invoice: PaymentCheckout) {
  return new Intl.NumberFormat(invoice.locale, {
    style: 'currency',
    currency: invoice.currency,
  }).format(invoice.amountMinor / 100)
}

function formatStatus(status: PaymentCheckout['status']) {
  return status.charAt(0).toUpperCase() + status.slice(1)
}
