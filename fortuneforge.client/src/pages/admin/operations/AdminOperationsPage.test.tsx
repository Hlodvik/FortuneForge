import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { AppRoutes } from '../../../app/AppRoutes'
import { pageTitleForPath } from '../../../app/usePageTitle'
import type { OperationsDashboard } from '../../../features/admin/operations/operationsApi'
import { AdminOperationsDashboardView } from './AdminOperationsPage'

describe('admin operations route', () => {
  it('is lazy-loaded on its isolated route with an operations title', () => {
    const markup = renderToStaticMarkup(createElement(AppRoutes, {
      pathname: '/admin/operations',
      slotRoute: null,
      onSpinStateChange: () => undefined,
    }))

    expect(markup).toContain('Opening Fortune Forge…')
    expect(pageTitleForPath('/admin/operations')).toBe('Operations — Fortune Forge')
  })

  it('renders credit Hold’em fees separately from bot telemetry', () => {
    const now = new Date().toISOString()
    const dashboard = {
      overview: {
        fromUtc: now, toUtc: now,
        slots: { wageredCredits: 0, paidCredits: 0, houseNetCredits: 0, completedEvents: 0 },
        blackjack: { wageredCredits: 0, paidCredits: 0, houseNetCredits: 0, completedEvents: 0 },
        solitaire: { grossPoolCredits: 0, winnerPayoutCredits: 0, platformFeeCredits: 0, settledRealHumanPoolMatches: 0 },
        texasHoldem: { grossPoolCredits: 100, winnerPayoutCredits: 90, platformFeeCredits: 10, settledRealHumanPoolMatches: 1 },
        houseGamingNetCredits: 10,
        funding: { completedPurchaseCredits: 0, completedPurchases: 0, completedWithdrawalCredits: 0, completedWithdrawals: 0 },
        complete: true, limitations: [],
      },
      activity: { items: [{ eventId: 'table-loss', category: 'gaming', game: 'blackjack', status: 'settled', occurredAtUtc: now, wageredCredits: 10, paidCredits: 15, houseNetCredits: -5 }], nextCursor: null },
      queues: { items: [], nextCursor: null }, matches: { items: [], nextCursor: null },
      integrity: { fromUtc: now, toUtc: now, checks: [], complete: true, limitations: [] },
      bots: { fromUtc: now, toUtc: now, games: [], financialTreatment: 'Synthetic bots are nonfinancial.' },
    } satisfies OperationsDashboard

    const markup = renderToStaticMarkup(createElement(AdminOperationsDashboardView, { dashboard }))

    expect(markup).toContain("Real-human pool Hold&#x27;em fees")
    expect(markup).toContain('R10,00')
    expect(markup).toContain('net R-5,00')
    expect(markup).toContain('Synthetic bot telemetry')
  })
})
