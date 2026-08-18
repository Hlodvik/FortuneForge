export type GameFinancials = {
  wageredCredits: number
  paidCredits: number
  houseNetCredits: number
  completedEvents: number
}

export type OperationsOverview = {
  fromUtc: string
  toUtc: string
  slots: GameFinancials
  blackjack: GameFinancials
  solitaire: {
    grossPoolCredits: number
    winnerPayoutCredits: number
    platformFeeCredits: number
    settledRealHumanPoolMatches: number
  }
  texasHoldem: {
    grossPoolCredits: number
    winnerPayoutCredits: number
    platformFeeCredits: number
    settledRealHumanPoolMatches: number
  }
  houseGamingNetCredits: number
  funding: {
    completedPurchaseCredits: number
    completedPurchases: number
    completedWithdrawalCredits: number
    completedWithdrawals: number
  }
  complete: boolean
  limitations: string[]
}

export type OperationsActivity = {
  eventId: string
  category: string
  game: string
  status: string
  occurredAtUtc: string
  wageredCredits: number | null
  paidCredits: number | null
  houseNetCredits: number | null
}

export type OperationsQueue = {
  queueId: string
  game: string
  status: string
  requiredPlayers: number
  queuedPlayers: number
  entryCredits: number
  updatedAtUtc: string
}

export type OperationsMatch = {
  matchId: string
  game: string
  status: string
  playerCount: number
  startedAtUtc: string
  completedAtUtc: string | null
  wageredCredits: number
  paidCredits: number
  houseNetCredits: number
}

export type OperationsPage<T> = { items: T[]; nextCursor: string | null }

export type OperationsIntegrity = {
  fromUtc: string
  toUtc: string
  checks: Array<{ id: string; status: string; summary: string; recordsChecked: number; findings: number }>
  complete: boolean
  limitations: string[]
}

export type OperationsBots = {
  fromUtc: string
  toUtc: string
  games: Array<{
    game: string
    enabled: boolean
    recentLeaseAttempts: number
    completedTurns: number
    activeLeases: number
  }>
  financialTreatment: string
}

export type OperationsDashboard = {
  overview: OperationsOverview
  activity: OperationsPage<OperationsActivity>
  queues: OperationsPage<OperationsQueue>
  matches: OperationsPage<OperationsMatch>
  integrity: OperationsIntegrity
  bots: OperationsBots
}

export class OperationsRequestError extends Error {
  readonly status: number

  constructor(message: string, status: number) {
    super(message)
    this.status = status
  }
}

export async function getOperationsDashboard(hours = 24): Promise<OperationsDashboard> {
  const safeHours = Math.min(24 * 31, Math.max(1, Math.trunc(hours)))
  const to = new Date()
  const from = new Date(to.getTime() - safeHours * 60 * 60 * 1000)
  const range = new URLSearchParams({ from: from.toISOString(), to: to.toISOString() })
  const [overview, activity, queues, matches, integrity, bots] = await Promise.all([
    request<OperationsOverview>(`/api/admin/operations/overview?${range}`),
    request<OperationsPage<OperationsActivity>>(`/api/admin/operations/activity?${range}&limit=50`),
    request<OperationsPage<OperationsQueue>>(`/api/admin/operations/queues?${range}&limit=50`),
    request<OperationsPage<OperationsMatch>>(`/api/admin/operations/matches?${range}&limit=50`),
    request<OperationsIntegrity>(`/api/admin/operations/integrity?${range}`),
    request<OperationsBots>(`/api/admin/operations/bots?${range}`),
  ])
  return { overview, activity, queues, matches, integrity, bots }
}

async function request<T>(path: string): Promise<T> {
  const response = await fetch(path, {
    method: 'GET',
    credentials: 'include',
    headers: { Accept: 'application/json' },
  })
  if (!response.ok) {
    let message = 'Operations data could not be loaded.'
    try {
      const body = await response.json() as { error?: string; detail?: string }
      message = body.error ?? body.detail ?? message
    } catch {
      // Keep the intentionally generic message for non-JSON failures.
    }
    throw new OperationsRequestError(message, response.status)
  }
  return await response.json() as T
}
