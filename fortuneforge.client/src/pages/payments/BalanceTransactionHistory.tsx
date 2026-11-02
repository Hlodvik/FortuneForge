import { useCallback, useEffect, useState } from 'react'
import {
  listPaymentInvoices,
  listPaymentWithdrawals,
  type PaymentCheckout,
  type PaymentWithdrawalSummary,
} from '../../features/payments/services/paymentsApi'
import './balanceTransactionHistory.css'

type Transaction =
  | { kind: 'deposit'; id: string; createdAtUtc: string; value: PaymentCheckout }
  | { kind: 'withdrawal'; id: string; createdAtUtc: string; value: PaymentWithdrawalSummary }

const randFormatter = new Intl.NumberFormat('en-ZA', {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
})

export function BalanceTransactionHistory() {
  const [transactions, setTransactions] = useState<Transaction[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [reloadKey, setReloadKey] = useState(0)

  const reload = useCallback(() => {
    setError(null)
    setTransactions(null)
    setReloadKey((current) => current + 1)
  }, [])

  useEffect(() => {
    let active = true
    void Promise.all([listPaymentInvoices(50), listPaymentWithdrawals(50)])
      .then(([invoices, withdrawals]) => {
        if (!active) return
        const combined: Transaction[] = [
          ...invoices.map((value) => ({
            kind: 'deposit' as const,
            id: value.checkoutId,
            createdAtUtc: value.createdAtUtc,
            value,
          })),
          ...withdrawals.map((value) => ({
            kind: 'withdrawal' as const,
            id: value.withdrawalId,
            createdAtUtc: value.createdAtUtc,
            value,
          })),
        ]
        combined.sort((first, second) =>
          Date.parse(second.createdAtUtc) - Date.parse(first.createdAtUtc))
        setTransactions(combined)
        setError(null)
      })
      .catch((requestError: unknown) => {
        if (!active) return
        setError(requestError instanceof Error ? requestError.message : 'Transaction history is unavailable.')
        setTransactions(null)
      })
    return () => { active = false }
  }, [reloadKey])

  return (
    <section className="balance-transactions" aria-labelledby="balance-transactions-title">
      <div className="balance-transactions__heading">
        <div>
          <p className="balance-transactions__eyebrow">Your account</p>
          <h2 id="balance-transactions-title">Transaction history</h2>
        </div>
        <button type="button" onClick={reload} aria-label="Refresh transaction history">Refresh</button>
      </div>
      {transactions === null && error === null && <p className="balance-transactions__state" role="status">Loading transactions…</p>}
      {error !== null && (
        <div className="balance-transactions__state balance-transactions__state--error" role="alert">
          <strong>History could not be loaded.</strong>
          <span>{error}</span>
          <button type="button" onClick={reload}>Try again</button>
        </div>
      )}
      {transactions !== null && transactions.length === 0 && (
        <p className="balance-transactions__state">Your deposits and withdrawals will appear here.</p>
      )}
      {transactions !== null && transactions.length > 0 && (
        <div className="balance-transactions__list">
          {transactions.map((transaction) => <TransactionRow key={`${transaction.kind}-${transaction.id}`} transaction={transaction} />)}
        </div>
      )}
    </section>
  )
}

function TransactionRow({ transaction }: { transaction: Transaction }) {
  const { kind, value } = transaction
  const status = value.status.toLowerCase()
  const completed = status === 'completed'
  const label = kind === 'deposit'
    ? completed ? 'Deposit' : 'Deposit request'
    : completed ? 'Withdrawal' : 'Withdrawal request'
  const credits = kind === 'deposit' ? value.credits : value.creditsDebited
  const formattedAmount = `R${randFormatter.format(credits)}`
  const date = new Date(value.createdAtUtc)
  const dateLabel = Number.isNaN(date.getTime())
    ? 'Date unavailable'
    : date.toLocaleString('en-ZA', { dateStyle: 'medium', timeStyle: 'short' })
  const details = kind === 'deposit'
    ? <a href={`/home/invoices/${encodeURIComponent(value.invoiceId)}`}>View invoice</a>
    : <span>Reference {value.withdrawalId}</span>

  return (
    <article className="balance-transactions__row">
      <span className={`balance-transactions__icon balance-transactions__icon--${kind}`} aria-hidden="true">
        {kind === 'deposit' ? '↓' : '↑'}
      </span>
      <div className="balance-transactions__description">
        <strong>{label}</strong>
        <span>{dateLabel}</span>
        <small>{details}</small>
      </div>
      <div className="balance-transactions__value">
        <strong>{formattedAmount}</strong>
        <span className={`balance-transactions__status balance-transactions__status--${completed ? 'completed' : 'open'}`}>
          {statusLabel(status)}
        </span>
      </div>
    </article>
  )
}

function statusLabel(status: string) {
  switch (status) {
    case 'received': return 'Received'
    case 'pending': return 'Pending'
    case 'processing': return 'Processing'
    case 'completed': return 'Completed'
    case 'failed': return 'Failed'
    case 'expired': return 'Expired'
    case 'rejected': return 'Rejected'
    case 'cancelled':
    case 'canceled': return 'Cancelled'
    default: return status.replaceAll('_', ' ')
  }
}
