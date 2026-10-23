import { createRoot } from 'react-dom/client'
import '@fontsource-variable/sora'
import '../index.css'
import { AdminPortalPage } from '../pages/admin/portal/AdminPortalPage'
import type { AdminPortalApi, AdminManagement, AdminPlayer, AdminPlayerDetail, ManagementAudit } from '../features/admin/portal/adminPortalApi'
import type { OperationsDashboard } from '../features/admin/operations/operationsApi'
import { SLOT_ROUTE_DEFINITIONS } from '../games/slots/routeRegistry'

// Development-only isolated UI fixture. No account login or production API calls.
if (!import.meta.env.DEV) throw new Error('This sample-data preview is development-only.')
const now = new Date().toISOString()
const samplePlayers: AdminPlayer[] = ['Alex Morgan', 'Jamie Taylor', 'Riley Chen'].map((playerName, index) => ({ userId: `sample-player-${index + 1}`, playerName, email: `sample${index + 1}@example.test`, role: 'player', status: 'active', deactivated: false, isBot: false, createdAtUtc: '2026-10-03T14:22:00Z', lastActiveAtUtc: now }))
const entries = <T,>(items: T[]) => ({ items, nextCursor: null, complete: true, limitations: [] })
let management: AdminManagement = { version: 1, botsEnabled: true, updatedAtUtc: now, games: [
  { id: 'blackjack', displayName: 'Fortune Blackjack', category: 'cards', route: '/cards/blackjack', visible: true, botsSupported: true, botsEnabled: true, effectiveBotsEnabled: true },
  { id: 'hearts', displayName: 'Hearts', category: 'cards', route: '/cards/hearts', visible: true, botsSupported: true, botsEnabled: true, effectiveBotsEnabled: true },
  { id: 'baccarat', displayName: 'Baccarat', category: 'cards', route: '/games/baccarat', visible: true, botsSupported: false, botsEnabled: false, effectiveBotsEnabled: false },
  { id: 'roulette', displayName: 'Roulette', category: 'casino', route: '/games/roulette', visible: true, botsSupported: true, botsEnabled: true, effectiveBotsEnabled: true },
  { id: 'horse-flight', displayName: 'Horse Flight', category: 'arcade', route: '/games/horse-flight', visible: false, botsSupported: false, botsEnabled: false, effectiveBotsEnabled: false },
  ...SLOT_ROUTE_DEFINITIONS.map(game => ({ id: game.id, displayName: game.title, category: 'slots', route: game.playPath!, visible: true, botsSupported: false, botsEnabled: false, effectiveBotsEnabled: false })),
] }
let audit: ManagementAudit[] = []
const api: AdminPortalApi = {
  players: async (query = '') => entries(samplePlayers.filter(player => `${player.playerName} ${player.email} ${player.userId}`.toLowerCase().includes(query.toLowerCase()))),
  player: async id => ({ player: samplePlayers.find(player => player.userId === id)!, balances: { slotsCredits: 219.75, freeGames: 5, specialPoints: 20, energy: 100 }, summary: { purchaseCredits: 500, mockPurchaseCredits: 50, gamingWageredCredits: 340, gamingPaidCredits: 54.75, gamingRefundedCredits: 5, gamingNetCredits: -280.25, recordsRead: 42, scope: 'bounded-records', complete: true, limitations: [] }, coverage: { complete: false, sources: ['sample fixtures'], limitations: ['Sample data only. Real views label incomplete legacy match history and missing money fields.'] } } satisfies AdminPlayerDetail),
  purchases: async userId => entries([{ checkoutId: 'sample-checkout-1', userId, invoiceId: null, providerId: 'sample-provider', isMock: false, status: 'completed', currency: 'ZAR', amountMinor: 50000, credits: 500, createdAtUtc: now, statusUpdatedAtUtc: now }]),
  gaming: async userId => entries([{ eventId: 'sample-event-1', userId, game: 'blackjack', matchId: 'sample-match-1', type: 'blackjack-wager', status: 'posted', occurredAtUtc: now, wageredCredits: 10, paidCredits: 0, netCredits: -10, amountCredits: -10, balanceAfterCredits: 219.75, source: 'sample wallet ledger' }]),
  matches: async () => entries([{ matchId: 'sample-match-1', game: 'blackjack', status: 'completed', startedAtUtc: now, completedAtUtc: now, wageredCredits: 10, paidCredits: 20, netCredits: 10 }]),
  management: async () => management,
  audit: async () => entries(audit),
  change: async change => {
    const before = change.gameId ? management.games.find(game => game.id === change.gameId)![change.kind === 'visibility' ? 'visible' : 'botsEnabled'] : management.botsEnabled
    management = { ...management, version: management.version + 1, updatedAtUtc: new Date().toISOString(), botsEnabled: change.gameId ? management.botsEnabled : change.enabled, games: management.games.map(game => game.id === change.gameId ? { ...game, [change.kind === 'visibility' ? 'visible' : 'botsEnabled']: change.enabled } : { ...game }) }
    management.games = management.games.map(game => ({ ...game, effectiveBotsEnabled: game.botsSupported && game.botsEnabled && management.botsEnabled }))
    audit = [{ id: `sample-audit-${management.version}`, actorId: 'sample-admin', operation: change.kind, gameId: change.gameId ?? null, occurredAtUtc: management.updatedAtUtc!, reason: change.reason, before, after: change.enabled }, ...audit]
    return management
  },
}
const financials = (wageredCredits: number, paidCredits: number) => ({ wageredCredits, paidCredits, houseNetCredits: wageredCredits - paidCredits, completedEvents: 18 })
const pool = { grossPoolCredits: 80, winnerPayoutCredits: 72, platformFeeCredits: 8, settledRealHumanPoolMatches: 10 }
const dashboard: OperationsDashboard = { overview: { fromUtc: now, toUtc: now, slots: financials(1480, 1220), blackjack: financials(820, 745), threeCardPoker: financials(460, 310), solitaire: pool, texasHoldem: pool, houseGamingNetCredits: 501, funding: { completedPurchaseCredits: 2500, completedPurchases: 8, completedWithdrawalCredits: 250, completedWithdrawals: 1 }, complete: false, limitations: ['Sample data preview. Financial reporting only includes its explicitly supported sources.'] }, activity: entries([]), queues: entries([]), matches: entries([{ matchId: 'sample-match-1', game: 'blackjack', status: 'completed', playerCount: 3, startedAtUtc: now, completedAtUtc: now, wageredCredits: 10, paidCredits: 20, houseNetCredits: -10 }]), integrity: { fromUtc: now, toUtc: now, checks: [], complete: true, limitations: [] }, bots: { fromUtc: now, toUtc: now, financialTreatment: 'Sample managed-player telemetry', games: [] } }
const overview = async () => dashboard
createRoot(document.getElementById('root')!).render(<><div style={{ padding: '8px 16px', background: '#554117', color: '#fff1c7', font: '11px system-ui' }}>DEVELOPMENT PREVIEW · Sample records only · No production accounts or controls</div><AdminPortalPage playerName="Sample Administrator" api={api} loadOverview={overview} /></>)
