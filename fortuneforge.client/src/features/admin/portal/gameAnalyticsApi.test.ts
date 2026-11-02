import { beforeEach, describe, expect, it, vi } from 'vitest'
const request = vi.hoisted(() => vi.fn())
vi.mock('./adminPortalApi', () => ({ adminRequest: request }))
import { gameAnalyticsApi } from './gameAnalyticsApi'

beforeEach(() => request.mockReset().mockResolvedValue({}))
describe('admin game analytics API', () => {
  it('encodes UTC period, account and opaque pagination filters without inventing empty filters', async () => {
    await gameAnalyticsApi.metrics('month', '2026-10', 'account/a + b')
    const query = new URL(request.mock.calls[0][0], 'http://local').searchParams
    expect(query.get('period')).toBe('month'); expect(query.get('date')).toBe('2026-10'); expect(query.get('userId')).toBe('account/a + b')
    await gameAnalyticsApi.rounds('texas-holdem', 'flagged', 'opaque+/=')
    const roundQuery = new URL(request.mock.calls[1][0], 'http://local').searchParams
    expect(roundQuery.get('cursor')).toBe('opaque+/='); expect(roundQuery.get('gameId')).toBe('texas-holdem')
    await gameAnalyticsApi.metrics('alltime')
    expect(request.mock.calls[2][0]).toBe('/api/admin/game-analytics?period=alltime')
  })
  it('keeps evidence reads private and all mutations explicitly marked administrator JSON requests', async () => {
    await gameAnalyticsApi.evidence('round/a b')
    expect(request).toHaveBeenLastCalledWith('/api/admin/game-analytics/rounds/round%2Fa%20b')
    const draft = { userId: 'alice', gameId: 'mines', roundId: 'round1', sourceIncidentId: 'audit-stable', severity: 'high', ruleId: 'rule-1', ruleVersion: 'admin-v1', attributionBasis: 'Reviewed player action evidence.', evidenceIds: ['audit-stable'] }
    await gameAnalyticsApi.flag(draft)
    expect(request).toHaveBeenLastCalledWith('/api/admin/risk-incidents', expect.objectContaining({ method: 'POST', headers: { 'X-FortuneForge-Admin': '1', 'Content-Type': 'application/json' }, body: JSON.stringify(draft) }))
    await gameAnalyticsApi.resolve('incident/a', 'cleared', 'Evidence was a server error.')
    expect(request).toHaveBeenLastCalledWith('/api/admin/risk-incidents/incident%2Fa/resolution', expect.objectContaining({ method: 'POST', body: JSON.stringify({ outcome: 'cleared', reason: 'Evidence was a server error.' }) }))
    await gameAnalyticsApi.resolve('incident/a', 'upheld', 'Reviewed evidence supports critical severity.', 'critical')
    expect(request).toHaveBeenLastCalledWith('/api/admin/risk-incidents/incident%2Fa/resolution', expect.objectContaining({ method: 'POST', body: JSON.stringify({ outcome: 'upheld', reason: 'Reviewed evidence supports critical severity.', severity: 'critical' }) }))
  })
})
