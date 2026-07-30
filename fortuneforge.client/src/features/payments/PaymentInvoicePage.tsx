import { useCallback, useEffect, useState } from 'react'
import { ForgeCreditAmount } from '../../components/ForgeCreditAmount'
import { PaymentAlertsMenu } from '../../components/PaymentAlertsMenu'
import { useAuthenticatedAccount } from '../landing/useAuthenticatedAccount'
import '../landing/landing.css'
import {
  getAdminPaymentInvoice,
  getPaymentCatalog,
  getPaymentInvoice,
  PaymentRequestError,
  simulateMockPayment,
  type PaymentCheckout,
} from './services/paymentsApi'

const creditsFormatter = new Intl.NumberFormat('en-US')

export function PaymentInvoicePage({ invoiceId, adminView = false }: { invoiceId: string; adminView?: boolean }) {
  const redirectPath = `${adminView ? '/admin' : '/home'}/invoices/${encodeURIComponent(invoiceId)}`
  const { account, error: accountError, isLoading: isAccountLoading, reload } =
    useAuthenticatedAccount(redirectPath)
  const [invoice, setInvoice] = useState<PaymentCheckout | null>(null)
  const [invoiceError, setInvoiceError] = useState<string | null>(null)
  const [canSimulate, setCanSimulate] = useState(false)
  const [isUpdating, setIsUpdating] = useState(false)

  const loadInvoice = useCallback(async () => {
    try {
      setInvoice(await (adminView
        ? getAdminPaymentInvoice(invoiceId)
        : getPaymentInvoice(invoiceId)))
      setInvoiceError(null)
    } catch (requestError) {
      setInvoiceError(errorMessage(requestError))
    }
  }, [adminView, invoiceId])

  useEffect(() => {
    if (account === null) {
      return
    }

    let active = true
    const invoiceRequest = adminView
      ? getAdminPaymentInvoice(invoiceId)
      : getPaymentInvoice(invoiceId)
    void Promise.all([invoiceRequest, getPaymentCatalog()])
      .then(([nextInvoice, catalog]) => {
        if (active) {
          setInvoice(nextInvoice)
          setCanSimulate(!adminView && catalog.mockSimulationEnabled)
          setInvoiceError(null)
        }
      })
      .catch((requestError: unknown) => {
        if (active) {
          setInvoiceError(errorMessage(requestError))
        }
      })

    const interval = window.setInterval(() => {
      if (active) {
        void loadInvoice()
      }
    }, 15_000)
    return () => {
      active = false
      window.clearInterval(interval)
    }
  }, [account, adminView, invoiceId, loadInvoice])

  async function updateMockStatus(status: 'processing' | 'completed' | 'failed') {
    if (invoice === null) {
      return
    }

    setIsUpdating(true)
    setInvoiceError(null)
    try {
      setInvoice(await simulateMockPayment(invoice.checkoutId, status))
      window.dispatchEvent(new Event('payment-invoices-updated'))
    } catch (requestError) {
      setInvoiceError(errorMessage(requestError))
    } finally {
      setIsUpdating(false)
    }
  }

  const displayedBalance = invoice?.status === 'completed' && invoice.creditedBalance !== null
    ? invoice.creditedBalance
    : account?.balances.slotsCredits
  const submittedCustomerName = invoice === null
    ? ''
    : `${invoice.customer.firstName} ${invoice.customer.lastName}`.trim()
  const paymentReference = invoice?.customer.customerReference
    || invoice?.customer.beneficiaryReference
    || invoice?.bankTransfer?.reference
    || ''
  const payerBank = invoice?.payerBank ?? null
  const hasPayerBank = payerBank !== null
    && [
      payerBank.accountHolder,
      payerBank.bankName,
      payerBank.accountNumber,
      payerBank.branchCode,
      payerBank.accountType,
    ].some((value) => value.trim() !== '')

  return (
    <div className="player-page player-page--credits">
      <header className="landing-bar">
        <a className="landing-brand" href="/" aria-label="Fortune Forge home"><span className="landing-brand__spark" aria-hidden="true">✦</span><span>Fortune Forge</span></a>
        <div className="landing-bar__account">
          {displayedBalance !== undefined && <ForgeCreditAmount amount={displayedBalance} />}
          <PaymentAlertsMenu />
          <nav className="landing-nav" aria-label="Invoice navigation">{adminView ? <a className="landing-nav__link" href="/admin/invoices">Admin invoices</a> : <a className="landing-nav__link" href="/home/rand">Add Rand</a>}<a className="landing-nav__link" href="/home">Home</a></nav>
        </div>
      </header>

      <main className="invoice-main">
        {isAccountLoading && <PageState label="Opening your invoice…" />}
        {!isAccountLoading && accountError !== null && <PageError message={accountError} onRetry={reload} />}
        {!isAccountLoading && account !== null && invoice === null && invoiceError === null && <PageState label="Loading invoice…" />}
        {!isAccountLoading && account !== null && invoice === null && invoiceError !== null && <PageError message={invoiceError} onRetry={() => void loadInvoice()} />}
        {!isAccountLoading && account !== null && invoice !== null && (
          <article className="invoice-sheet">
            <header className="invoice-sheet__header">
              <div><p className="account-eyebrow">Fortune Forge payment invoice</p><h1>Invoice</h1><p className="invoice-sheet__id">{invoice.invoiceId}</p></div>
              <div className={`invoice-status invoice-status--${invoice.status}`}><small>Order status</small><strong>{formatStatus(invoice.status)}</strong><span>Updated {new Date(invoice.statusUpdatedAtUtc).toLocaleString()}</span></div>
            </header>

            {invoice.isMock && <p className="invoice-sheet__mock"><strong>Mock invoice:</strong> do not send real money to these bank details.</p>}

            <section className="invoice-progress" aria-label="Invoice progress">
              <StatusStep label="Received" date={invoice.createdAtUtc} reached />
              <span className={invoice.processingAtUtc !== null || invoice.status === 'completed' ? 'is-reached' : undefined} aria-hidden="true" />
              <StatusStep label="Processing" date={invoice.processingAtUtc} reached={invoice.processingAtUtc !== null || invoice.status === 'completed'} />
              <span className={invoice.status === 'completed' ? 'is-reached' : undefined} aria-hidden="true" />
              <StatusStep label="Completed" date={invoice.completedAtUtc} reached={invoice.status === 'completed'} />
            </section>

            {(invoice.status === 'failed' || invoice.status === 'expired') && (
              <p className="invoice-sheet__failure" role="status">This order is {invoice.status}. No Rand was added.</p>
            )}
            {invoice.status === 'completed' && (
              <p className="invoice-sheet__success" role="status">Payment completed. R{creditsFormatter.format(invoice.credits)} was added exactly once.</p>
            )}

            <div className="invoice-sheet__parties">
              <section><small>Customer</small><strong>{submittedCustomerName || (adminView ? 'Linked customer account' : account.playerName)}</strong><span>{invoice.customer.email || account.email}</span><span>Reference #: {paymentReference || '—'}</span><span className="invoice-sheet__uid">UID: {invoice.userId}</span></section>
              <section><small>Invoice date</small><strong>{new Date(invoice.createdAtUtc).toLocaleDateString()}</strong><span>{new Date(invoice.createdAtUtc).toLocaleTimeString()}</span><span>{invoice.marketName} · {invoice.currency}</span></section>
              <section><small>Paid using</small><strong>{invoice.paymentMethodName}</strong><span>{invoice.paymentMethodType.replace('_', ' ')}</span><span>Confirmed after bank matching</span></section>
              {payerBank !== null && hasPayerBank && (
                <section>
                  <small>Payment account</small>
                  <strong>{payerBank.accountHolder || 'Account holder supplied'}</strong>
                  <span>{payerBank.bankName || 'Bank name not supplied'} · {payerBank.accountType || 'Account type not supplied'}</span>
                  <span>Account #: {payerBank.accountNumber || '—'}</span>
                  <span>Branch code: {payerBank.branchCode || '—'}</span>
                </section>
              )}
            </div>

            <section className="invoice-receipt" aria-labelledby="invoice-receipt-title">
              <div className="invoice-receipt__heading"><h2 id="invoice-receipt-title">Receipt</h2><span>{invoice.market} / {invoice.currency}</span></div>
              <div className="invoice-receipt__row invoice-receipt__row--head"><span>Description</span><span>Payment</span><span>Rand added</span></div>
              <div className="invoice-receipt__row"><span>Rand balance load</span><strong>{formatMoney(invoice)}</strong><strong>R{creditsFormatter.format(invoice.credits)}</strong></div>
              <div className="invoice-receipt__total"><span>Total</span><strong>{formatMoney(invoice)}</strong></div>
            </section>

            {invoice.bankTransfer !== null && (
              <section className="invoice-bank" aria-labelledby="invoice-bank-title">
                <div><p className="account-eyebrow">Payment instructions</p><h2 id="invoice-bank-title">Bank transfer</h2><p>{invoice.bankTransfer.instructions}</p></div>
                <dl>
                  <div><dt>Bank</dt><dd>{invoice.bankTransfer.bankName}</dd></div>
                  <div><dt>Account name</dt><dd>{invoice.bankTransfer.accountName}</dd></div>
                  <div><dt>Account number</dt><dd>{invoice.bankTransfer.accountNumber}</dd></div>
                  <div><dt>Branch code</dt><dd>{invoice.bankTransfer.branchCode}</dd></div>
                  <div><dt>Reference #</dt><dd>{paymentReference || invoice.bankTransfer.reference}</dd></div>
                  {invoice.customer.beneficiaryReference && invoice.customer.beneficiaryReference !== paymentReference && (
                    <div><dt>Beneficiary reference</dt><dd>{invoice.customer.beneficiaryReference}</dd></div>
                  )}
                </dl>
              </section>
            )}

            {invoiceError !== null && <p className="payment-checkout__error" role="alert">{invoiceError}</p>}
            <footer className="invoice-sheet__actions">
              <button type="button" className="landing-button landing-button--secondary" onClick={() => window.print()}>Print invoice</button>
              <button type="button" className="landing-button landing-button--secondary" disabled={isUpdating} onClick={() => void loadInvoice()}>Refresh status</button>
              {canSimulate && invoice.status === 'received' && <button type="button" className="landing-button landing-button--gold" disabled={isUpdating} onClick={() => void updateMockStatus('processing')}>Simulate processing</button>}
              {canSimulate && invoice.status === 'processing' && <button type="button" className="landing-button landing-button--gold" disabled={isUpdating} onClick={() => void updateMockStatus('completed')}>Simulate completed</button>}
              {canSimulate && (invoice.status === 'received' || invoice.status === 'processing') && <button type="button" className="invoice-sheet__fail" disabled={isUpdating} onClick={() => void updateMockStatus('failed')}>Simulate failed</button>}
            </footer>
          </article>
        )}
      </main>
    </div>
  )
}

function StatusStep({ label, date, reached }: { label: string; date: string | null; reached: boolean }) {
  return <div className={reached ? 'is-reached' : undefined}><i aria-hidden="true">{reached ? '✓' : ''}</i><strong>{label}</strong><small>{date === null ? 'Waiting' : new Date(date).toLocaleString()}</small></div>
}

function PageState({ label }: { label: string }) {
  return <div className="player-state" role="status"><span aria-hidden="true">✦</span>{label}</div>
}

function PageError({ message, onRetry }: { message: string; onRetry: () => void }) {
  return <div className="player-state player-state--error" role="alert"><strong>The invoice could not be opened.</strong><span>{message}</span><button className="landing-button landing-button--secondary" type="button" onClick={onRetry}>Try again</button><a href="/home/invoices">View your invoices</a></div>
}

function formatMoney(invoice: PaymentCheckout) {
  return new Intl.NumberFormat(invoice.locale, { style: 'currency', currency: invoice.currency }).format(invoice.amountMinor / 100)
}

function formatStatus(status: PaymentCheckout['status']) {
  return status.charAt(0).toUpperCase() + status.slice(1)
}

function errorMessage(error: unknown) {
  if (error instanceof PaymentRequestError || error instanceof Error) {
    return error.message
  }

  return 'The invoice service is unavailable.'
}
