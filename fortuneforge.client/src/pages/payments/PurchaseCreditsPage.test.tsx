// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const accountState = vi.hoisted(() => ({
  account: {
    userId: 'balance-test', playerName: 'Avery', email: 'avery@example.com',
    role: 'player', balances: { slotsCredits: 125 },
  },
  reload: vi.fn(),
}))

vi.mock('../../features/account/useAuthenticatedAccount', () => ({
  useAuthenticatedAccount: () => ({
    account: accountState.account,
    isLoading: false,
    error: null,
    reload: accountState.reload,
  }),
}))
vi.mock('../../features/payments/services/paymentsApi', () => ({
  getPaymentCatalog: vi.fn(async () => ({
    providerId: 'test', isMock: false, mockSimulationEnabled: false,
    markets: [{
      code: 'ZA', displayName: 'South Africa', currency: 'ZAR', locale: 'en-ZA',
      audienceLabel: 'South Africa', paymentNotice: 'Bank transfer',
      minimumAmount: 1, maximumAmount: 1000, creditsPerCurrencyUnit: 1,
      suggestedAmounts: [5, 10, 20],
      paymentMethods: [{ id: 'bank', type: 'bank_transfer', displayName: 'Bank transfer', description: '', settlementLabel: 'Bank transfer' }],
    }],
  })),
  createPaymentCheckout: vi.fn(),
}))
vi.mock('../../components/PaymentAlertsMenu', () => ({ PaymentAlertsMenu: () => null }))
vi.mock('../../components/PlayerHeader', () => ({ PlayerAccountMenus: () => null }))
vi.mock('./BalanceTransactionHistory', () => ({ BalanceTransactionHistory: () => <div data-testid="transaction-history">Transactions</div> }))
vi.mock('../../features/payments/WithdrawalRequestCard', () => ({ WithdrawalRequestCard: () => <div data-testid="withdrawal-form">Withdrawal form</div> }))

import { PurchaseCreditsPage } from './PurchaseCreditsPage'

let host: HTMLDivElement
let root: Root

beforeEach(() => {
  window.history.replaceState(null, '', '/balance')
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  host = document.createElement('div')
  document.body.append(host)
  root = createRoot(host)
})

afterEach(async () => {
  await act(async () => root.unmount())
  host.remove()
  vi.unstubAllGlobals()
})

async function mount() {
  await act(async () => root.render(<PurchaseCreditsPage />))
}

function selectAction(name: string) {
  const button = Array.from(host.querySelectorAll<HTMLButtonElement>('.balance-page__actions button'))
    .find((candidate) => candidate.textContent?.includes(name))
  expect(button).toBeDefined()
  return button!
}

describe('Balance page actions', () => {
  it('starts with an overview and reveals only the selected form or history', async () => {
    await mount()
    expect(host.querySelector('.balance-page__available')?.textContent).toContain('R125')
    expect(host.querySelector('.payment-form')).toBeNull()
    expect(host.querySelector('[data-testid="withdrawal-form"]')).toBeNull()
    expect(host.querySelector('[data-testid="transaction-history"]')).toBeNull()

    await act(async () => selectAction('Deposit').click())
    expect(host.querySelector('.payment-form')).not.toBeNull()
    expect(host.querySelector('[data-testid="withdrawal-form"]')).toBeNull()

    await act(async () => selectAction('Withdrawal').click())
    expect(host.querySelector('.payment-form')).toBeNull()
    expect(host.querySelector('[data-testid="withdrawal-form"]')).not.toBeNull()

    await act(async () => selectAction('Transaction history').click())
    expect(host.querySelector('.payment-form')).toBeNull()
    expect(host.querySelector('[data-testid="withdrawal-form"]')).toBeNull()
    expect(host.querySelector('[data-testid="transaction-history"]')).not.toBeNull()
  })

  it('opens Withdrawal from an existing deep link', async () => {
    window.history.replaceState(null, '', '/balance#withdrawal-request')
    await mount()
    expect(selectAction('Withdrawal').getAttribute('aria-pressed')).toBe('true')
    expect(host.querySelector('[data-testid="withdrawal-form"]')).not.toBeNull()
    expect(host.querySelector('.payment-form')).toBeNull()
  })
})
