import { fetchWithAccountSession } from '../account/services/accountsApi'
import { adminRequest } from '../admin/portal/adminPortalApi'

export type ReportCategory = 'bug' | 'suggestion' | 'other'
export type ReportStatus = 'open' | 'in-review' | 'resolved' | 'closed'
export type ReportInput = { category: ReportCategory; message: string; path: string }
export type ReportReceipt = { id: string; status: ReportStatus; createdAtUtc: string }
export type GameReport = ReportReceipt & { userId: string; gameId: string; gameName: string; path: string; category: ReportCategory; message: string; version: number; updatedAtUtc: string; adminNote: string; updatedBy: string | null }
export type ReportsPage = { items: GameReport[]; nextCursor: string | null }
export class GameReportError extends Error {
  readonly status: number
  constructor(message: string, status: number) { super(message); this.name = 'GameReportError'; this.status = status }
}
export async function sendGameReport(input: ReportInput, key: string, signal?: AbortSignal): Promise<ReportReceipt> {
  const response = await fetchWithAccountSession('/api/game-reports', {
    method: 'POST', cache: 'no-store', signal,
    headers: { 'X-FortuneForge-Report': '1', 'Idempotency-Key': key }, body: JSON.stringify(input),
  })
  if (!response.ok) {
    const body = await response.json().catch(() => null) as { error?: string } | null
    throw new GameReportError(body?.error ?? (response.status === 401 ? 'Please sign in again before sending your report.' : response.status === 429 ? 'Please wait a few minutes before sending another report.' : 'Your report could not be confirmed. Retry to send the same report safely.'), response.status)
  }
  const value: unknown = await response.json().catch(() => undefined)
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new GameReportError('Your report could not be confirmed. Retry to confirm the same report safely.', 0)
  const receipt = value as Record<string, unknown>
  if (Object.keys(receipt).length !== 3 || typeof receipt.id !== 'string' || !/^[a-f0-9]{64}$/.test(receipt.id) ||
      !['open', 'in-review', 'resolved', 'closed'].includes(String(receipt.status)) ||
      typeof receipt.createdAtUtc !== 'string' || receipt.createdAtUtc.length > 64 || !Number.isFinite(Date.parse(receipt.createdAtUtc)))
    throw new GameReportError('Your report could not be confirmed. Retry to confirm the same report safely.', 0)
  return receipt as ReportReceipt
}
export const adminGameReportsApi = {
  list: (cursor: string | null = null) => adminRequest<ReportsPage>(`/api/admin/game-reports?limit=25${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ''}`),
  detail: (id: string) => adminRequest<GameReport>(`/api/admin/game-reports/${encodeURIComponent(id)}`),
  update: (report: GameReport, status: ReportStatus, note: string) => adminRequest<GameReport>(`/api/admin/game-reports/${encodeURIComponent(report.id)}/status`, {
    method: 'POST', headers: { 'X-FortuneForge-Admin': '1' }, body: JSON.stringify({ status, expectedVersion: report.version, note }),
  }),
}
export type AdminGameReportsApi = typeof adminGameReportsApi
