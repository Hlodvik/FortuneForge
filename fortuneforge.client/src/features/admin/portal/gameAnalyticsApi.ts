import { adminRequest } from './adminPortalApi'

export type GameMetrics = { humanRounds: number; uniqueRounds: number; paidRounds: number; practiceRounds: number; freeRounds: number; knownMoneyRounds: number; unknownMoneyRounds: number; wagerCents: number; payoutCents: number; netWinCents: number; netLossCents: number; observedActiveMilliseconds: number }
export type GameAnalyticsReport = { period: { kind: string; key: string; startUtc: string | null; endUtc: string | null }; asOfUtc: string; trackingStartedAtUtc: string | null; totals: GameMetrics; games: { gameId: string; metrics: GameMetrics; currentPlayers: number }[]; currentPlayers: number; limitations: string[]; presenceAsOfUtc: string | null }
export type RoundAudit = { id: string; gameId: string; roundId: string; tableId: string; roundNumber: number; completedAtUtc: string; auditStatus: string; findings: { code: string; severity: string; message: string }[]; participants: { actorId: string; displayName: string; isAccountBacked: boolean; wagerCents: number; payoutCents: number; netCents: number }[]; evidencePresent: boolean; evidenceDeleteAfterUtc: string | null; auditorVersion: string; evidenceKind: string; evidenceSha256: string }
export type RoundAuditPage = { items: RoundAudit[]; nextCursor: string | null }
export type RoundAuditDetail = { summary: RoundAudit; payloadJson: string | null; retentionState: string; retentionNote: string | null }
export type RiskIncident = { id: string; userId: string; gameId: string; roundId: string; severity: string; status: string; attributionBasis: string; evidenceIds: string[]; ruleId: string; ruleVersion: string; createdAtUtc: string; createdBy: string; resolvedAtUtc: string | null; resolvedBy: string | null; resolutionOutcome: string | null; resolutionReason: string | null }
export type PlayerRiskState = { accountRestricted: boolean; withdrawalsBlocked: boolean; blockedGameIds: string[] }
export type IncidentDraft = { userId: string; gameId: string; roundId: string; severity: string; sourceIncidentId: string; ruleId: string; ruleVersion: string; attributionBasis: string; evidenceIds: string[] }
const write = (body: unknown): RequestInit => ({ method: 'POST', headers: { 'X-FortuneForge-Admin': '1', 'Content-Type': 'application/json' }, body: JSON.stringify(body) })
const query = (values: Record<string, string | null | undefined>) => new URLSearchParams(Object.entries(values).filter((entry): entry is [string, string] => Boolean(entry[1])))
export const gameAnalyticsApi = {
  metrics: (period: string, date?: string, userId?: string) => adminRequest<GameAnalyticsReport>(`/api/admin/game-analytics?${query({ period, date, userId })}`),
  rounds: (gameId?: string, status?: string, cursor?: string | null) => adminRequest<RoundAuditPage>(`/api/admin/game-analytics/rounds?${query({ gameId, status, cursor })}`),
  evidence: (id: string) => adminRequest<RoundAuditDetail>(`/api/admin/game-analytics/rounds/${encodeURIComponent(id)}`),
  incidents: (userId?: string) => adminRequest<{ incidents: RiskIncident[]; state: PlayerRiskState | null }>(`/api/admin/risk-incidents?${query({ userId })}`),
  flag: (draft: IncidentDraft) => adminRequest<{ incident: RiskIncident }>(`/api/admin/risk-incidents`, write(draft)),
  resolve: (id: string, outcome: string, reason: string, severity?: string) => adminRequest<{ incident: RiskIncident }>(`/api/admin/risk-incidents/${encodeURIComponent(id)}/resolution`, write({ outcome, reason, severity })),
}
export type GameAnalyticsApi = typeof gameAnalyticsApi
