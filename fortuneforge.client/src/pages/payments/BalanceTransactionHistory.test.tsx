// @vitest-environment jsdom
import { act } from 'react'
import { createRoot } from 'react-dom/client'
import { afterEach, expect, it, vi } from 'vitest'

const { listPaymentInvoices, listPaymentWithdrawals } = vi.hoisted(() => ({
  listPaymentInvoices: vi.fn(),
  listPaymentWithdrawals: vi.fn(),
}))

vi.mock('../../features/payments/services/paymentsApi', () => ({
  listPaymentInvoices,
  listPaymentWithdrawals,
}))

import { BalanceTransactionHistory } from './BalanceTransactionHistory'

afterEach(() => {
  vi.clearAllMocks()
  vi.unstubAllGlobals()
})

it('combines real deposit and withdrawal records by date without claiming pending funds were added', async () => {
  listPaymentInvoices.mockResolvedValue([
    { checkoutId: 'deposit-old', invoiceId: 'INV-OLD', createdAtUtc: '2026-10-01T12:00:00Z', credits: 40, status: 'completed' },
    { checkoutId: 'deposit-new', invoiceId: 'INV-NEW', createdAtUtc: '2026-10-03T12:00:00Z', credits: 20, status: 'processing' },
  ])
  listPaymentWithdrawals.mockResolvedValue([
    { withdrawalId: 'withdraw-mid', createdAtUtc: '2026-10-02T12:00:00Z', creditsDebited: 10, status: 'pending' },
  ])
  const host = document.createElement('div')
  document.body.append(host)
  const root = createRoot(host)
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)

  try {
    await act(async () => root.render(<BalanceTransactionHistory />))
    const rows = [...host.querySelectorAll('.balance-transactions__row')]
    expect(rows).toHaveLength(3)
    expect(rows[0].textContent).toContain('Deposit request')
    expect(rows[0].textContent).toContain('Processing')
    expect(rows[1].textContent).toContain('Withdrawal request')
    expect(rows[1].textContent).toContain('Pending')
    expect(rows[2].textContent).toContain('Deposit')
    expect(rows[2].textContent).toContain('Completed')
    expect(host.querySelector('a[href="/home/invoices/INV-OLD"]')).not.toBeNull()
  } finally {
    await act(async () => root.unmount())
    host.remove()
  }
})

it('shows an error instead of a partial history when either source fails', async () => {
  listPaymentInvoices.mockResolvedValue([])
  listPaymentWithdrawals.mockRejectedValue(new Error('Withdrawal records unavailable'))
  const host = document.createElement('div')
  document.body.append(host)
  const root = createRoot(host)
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)

  try {
    await act(async () => root.render(<BalanceTransactionHistory />))
    expect(host.querySelector('[role="alert"]')?.textContent).toContain('Withdrawal records unavailable')
    expect(host.querySelectorAll('.balance-transactions__row')).toHaveLength(0)
  } finally {
    await act(async () => root.unmount())
    host.remove()
  }
})
