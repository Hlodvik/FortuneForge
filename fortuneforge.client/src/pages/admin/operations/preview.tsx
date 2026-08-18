import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import type { OperationsDashboard } from '../../../features/admin/operations/operationsApi'
import { AdminOperationsDashboardView } from './AdminOperationsPage'
import './adminOperations.css'

const now = new Date().toISOString()
const dashboard: OperationsDashboard = {
  overview: {
    fromUtc: now, toUtc: now,
    slots: { wageredCredits: 18240, paidCredits: 16412.5, houseNetCredits: 1827.5, completedEvents: 421 },
    blackjack: { wageredCredits: 4250, paidCredits: 3980, houseNetCredits: 270, completedEvents: 88 },
    solitaire: { grossPoolCredits: 800, winnerPayoutCredits: 720, platformFeeCredits: 80, settledRealHumanPoolMatches: 16 },
    texasHoldem: { grossPoolCredits: 1200, winnerPayoutCredits: 1080, platformFeeCredits: 120, settledRealHumanPoolMatches: 24 },
    houseGamingNetCredits: 2297.5,
    funding: { completedPurchaseCredits: 12900, completedPurchases: 74, completedWithdrawalCredits: 3400, completedWithdrawals: 12 },
    complete: true,
    limitations: [],
  },
  bots: {
    fromUtc: now, toUtc: now,
    financialTreatment: 'Synthetic bot play is nonfinancial and excluded from balances, ledgers, revenue, expense, liability, and house P&L.',
    games: [
      { game: 'blackjack', enabled: true, recentLeaseAttempts: 42, completedTurns: 39, activeLeases: 2 },
      { game: 'solitaire', enabled: true, recentLeaseAttempts: 51, completedTurns: 51, activeLeases: 0 },
      { game: 'texas-holdem', enabled: true, recentLeaseAttempts: 36, completedTurns: 33, activeLeases: 1 },
    ],
  },
  integrity: { fromUtc: now, toUtc: now, complete: true, limitations: [], checks: [
    { id: 'money', status: 'pass', summary: 'Gaming monetary values must be non-negative.', recordsChecked: 525, findings: 0 },
    { id: 'bots', status: 'pass', summary: 'Bot practice is account-neutral and excluded from every financial source and formula.', recordsChecked: 129, findings: 0 },
  ] },
  matches: { nextCursor: null, items: [
    { matchId: 'f37a9a', game: 'blackjack', status: 'completed', playerCount: 1, startedAtUtc: now, completedAtUtc: now, wageredCredits: 25, paidCredits: 50, houseNetCredits: -25 },
    { matchId: 'c81e4b', game: 'solitaire', status: 'settled', playerCount: 4, startedAtUtc: now, completedAtUtc: now, wageredCredits: 50, paidCredits: 45, houseNetCredits: 5 },
  ] },
  queues: { nextCursor: null, items: [
    { queueId: '4d9f2e', game: 'solitaire', status: 'waiting', requiredPlayers: 4, queuedPlayers: 3, entryCredits: 5, updatedAtUtc: now },
  ] },
  activity: { nextCursor: null, items: [
    { eventId: '4e292a51d5c1', category: 'gaming', game: 'slots', status: 'completed', occurredAtUtc: now, wageredCredits: 10, paidCredits: 7.5, houseNetCredits: 2.5 },
    { eventId: '14f620bb1972', category: 'funding', game: 'purchase', status: 'completed', occurredAtUtc: now, wageredCredits: null, paidCredits: 100, houseNetCredits: null },
  ] },
}

createRoot(document.getElementById('root')!).render(<StrictMode><main className="operations-page"><AdminOperationsDashboardView dashboard={dashboard} /></main></StrictMode>)
