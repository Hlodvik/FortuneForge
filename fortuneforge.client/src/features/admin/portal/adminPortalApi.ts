import { fetchWithAccountSession } from '../../account/services/accountsApi'

export type AdminPage<T> = { items: T[]; nextCursor: string | null; complete: boolean; limitations: string[] }
export type AdminPlayer = {
  userId: string; playerName: string; email: string | null; role: string; status: string
  deactivated: boolean; isBot: boolean; createdAtUtc: string | null; lastActiveAtUtc: string | null
}
export type AdminPlayerDetail = {
  player: AdminPlayer
  balances: { slotsCredits: number | null; freeGames: number | null; specialPoints: number | null; energy: number | null }
  summary: { purchaseCredits: number; mockPurchaseCredits?: number; gamingWageredCredits: number; gamingPaidCredits: number; gamingRefundedCredits: number; gamingNetCredits: number; recordsRead: number; scope: string; complete: boolean; limitations: string[] }
  coverage: { complete: boolean; sources: string[]; limitations: string[] }
}
export type AdminPurchase = {
  checkoutId: string; userId: string; invoiceId: string | null; providerId: string | null; isMock: boolean
  status: string; currency: string; amountMinor: number | null; credits: number | null
  mockClassificationKnown?: boolean
  createdAtUtc: string | null; statusUpdatedAtUtc: string | null
}
export type AdminGamingEvent = {
  eventId: string; userId: string; game: string; matchId: string | null; type: string; status: string
  occurredAtUtc: string | null; wageredCredits: number | null; paidCredits: number | null; netCredits: number | null
  amountCredits: number | null; balanceAfterCredits: number | null; source: string
}
export type AdminPlayerMatch = {
  matchId: string; game: string; status: string; startedAtUtc: string | null; completedAtUtc: string | null
  wageredCredits: number | null; paidCredits: number | null; netCredits: number | null
  players?: Array<{ userId: string | null; displayName: string | null; isBot: boolean | null }> | null
}
export type ManagedGame = { id: string; displayName: string; category: string; route: string; visible: boolean; botsSupported: boolean; botsEnabled: boolean; effectiveBotsEnabled: boolean }
export type AdminManagement = { version: number; botsEnabled: boolean; games: ManagedGame[]; updatedAtUtc: string | null }
export type ManagementAudit = { id: string; actorId: string; operation: string; gameId: string | null; occurredAtUtc: string; reason: string; before: unknown; after: unknown }
export type ManagementChange = { kind: 'visibility' | 'bots'; gameId?: string; enabled: boolean; expectedVersion: number; reason: string; idempotencyKey: string }

export class AdminPortalError extends Error {
  readonly status: number
  constructor(message: string, status: number) { super(message); this.name = 'AdminPortalError'; this.status = status }
}

export async function adminRequest<T>(path: string, init: RequestInit = {}): Promise<T> {
  const response = await fetchWithAccountSession(path, { ...init, cache: 'no-store', headers: { Accept: 'application/json', ...init.headers } })
  if (!response.ok) {
    let message = response.status === 403 ? 'Administrator access is required.' : response.status === 401 ? 'Your session has expired. Please sign in again.' : 'The admin request could not be completed.'
    try {
      const body = await response.json() as { error?: string; detail?: string }
      message = body.error ?? body.detail ?? message
    } catch { /* Do not expose raw server error pages. */ }
    throw new AdminPortalError(message, response.status)
  }
  return response.json() as Promise<T>
}

function pageQuery(cursor?: string | null, query?: string) {
  const params = new URLSearchParams({ limit: '25' })
  if (cursor) params.set('cursor', cursor)
  if (query?.trim()) params.set('query', query.trim())
  return params
}
export const adminPortalApi = {
  players: (query = '', cursor?: string | null) => adminRequest<AdminPage<AdminPlayer>>(`/api/admin/players?${pageQuery(cursor, query)}`),
  player: (id: string) => adminRequest<AdminPlayerDetail>(`/api/admin/players/${encodeURIComponent(id)}`),
  purchases: (id: string, cursor?: string | null) => adminRequest<AdminPage<AdminPurchase>>(`/api/admin/players/${encodeURIComponent(id)}/purchases?${pageQuery(cursor)}`),
  gaming: (id: string, cursor?: string | null) => adminRequest<AdminPage<AdminGamingEvent>>(`/api/admin/players/${encodeURIComponent(id)}/gaming?${pageQuery(cursor)}`),
  matches: (id: string, cursor?: string | null) => adminRequest<AdminPage<AdminPlayerMatch>>(`/api/admin/players/${encodeURIComponent(id)}/matches?${pageQuery(cursor)}`),
  management: () => adminRequest<AdminManagement>('/api/admin/management'),
  audit: (cursor?: string | null) => adminRequest<AdminPage<ManagementAudit>>(`/api/admin/management/audit?${pageQuery(cursor)}`).then(page => ({ ...page, complete: true, limitations: [] })),
  change: (change: ManagementChange) => {
    const path = change.gameId ? `/api/admin/management/games/${encodeURIComponent(change.gameId)}/${change.kind}` : '/api/admin/management/bots'
    const body = change.kind === 'visibility' ? { visible: change.enabled, expectedVersion: change.expectedVersion, reason: change.reason } : { enabled: change.enabled, expectedVersion: change.expectedVersion, reason: change.reason }
    return adminRequest<AdminManagement>(path, { method: 'POST', headers: { 'X-FortuneForge-Admin': '1', 'Idempotency-Key': change.idempotencyKey }, body: JSON.stringify(body) })
  },
}
export type AdminPortalApi = typeof adminPortalApi
