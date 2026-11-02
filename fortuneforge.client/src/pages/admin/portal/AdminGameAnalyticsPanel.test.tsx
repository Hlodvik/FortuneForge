// @vitest-environment jsdom
import { act, type ReactNode } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { GameAnalyticsApi, GameAnalyticsReport, RoundAuditDetail, RiskIncident } from '../../../features/admin/portal/gameAnalyticsApi'
import { AdminGameAnalyticsPanel } from './AdminGameAnalyticsPanel'
import { AdminRoundReviewsPanel, AdminRiskIncidentsPanel } from './AdminRoundReviewsPanel'

const metrics = { humanRounds: 10, uniqueRounds: 6, paidRounds: 8, practiceRounds: 1, freeRounds: 1, knownMoneyRounds: 8, unknownMoneyRounds: 0, wagerCents: 1000, payoutCents: 800, netWinCents: 200, netLossCents: 400, observedActiveMilliseconds: 3600000 }
const report = (id = 'hearts'): GameAnalyticsReport => ({ period: { kind: 'day', key: 'day:20261026', startUtc: '2026-10-26T00:00:00Z', endUtc: '2026-10-27T00:00:00Z' }, asOfUtc: '2026-10-26T12:00:00Z', presenceAsOfUtc: '2026-10-26T12:00:00Z', trackingStartedAtUtc: null, currentPlayers: 2, totals: metrics, games: [{ gameId: id, currentPlayers: 2, metrics }], limitations: ['Historical results have not been estimated.'] })
const detail = (retained = true): RoundAuditDetail => ({ summary: { id: 'audit-1', gameId: 'texas-holdem', roundId: 'round-1', tableId: 'table-1', roundNumber: 1, completedAtUtc: '2026-10-26T12:00:00Z', auditStatus: 'flagged', findings: [{ code: 'duplicate-card', severity: 'high', message: 'Inspect the recorded deal.' }], participants: [{ actorId: 'alice', displayName: 'Alice', isAccountBacked: true, wagerCents: 500, payoutCents: 800, netCents: 300 }, { actorId: 'house-1', displayName: 'House bot', isAccountBacked: false, wagerCents: 500, payoutCents: 0, netCents: -500 }], evidencePresent: retained, evidenceDeleteAfterUtc: null, auditorVersion: 'audit-v1', evidenceKind: 'private-deal', evidenceSha256: 'abc123' }, payloadJson: retained ? '{"privateCards":["AS","KH"],"note":"<script>unsafe</script>"}' : null, retentionState: retained ? 'held' : 'deleted', retentionNote: null })
const incident = (resolved = false): RiskIncident => ({ id: 'incident-1', userId: 'alice', gameId: 'texas-holdem', roundId: 'round-1', severity: 'high', status: resolved ? 'resolved' : 'open', attributionBasis: 'Reviewed actions linked to this human participant.', evidenceIds: ['audit-1'], ruleId: 'action-rule', ruleVersion: 'admin-v1', createdAtUtc: '2026-10-26T12:00:00Z', createdBy: 'admin', resolvedAtUtc: resolved ? '2026-10-26T13:00:00Z' : null, resolvedBy: resolved ? 'admin' : null, resolutionOutcome: resolved ? 'cleared' : null, resolutionReason: resolved ? 'The evidence shows a server defect.' : null })
const restricted = { accountRestricted: true, withdrawalsBlocked: true, blockedGameIds: ['texas-holdem'] }
const gateway = (): GameAnalyticsApi => ({ metrics: vi.fn().mockResolvedValue(report()), rounds: vi.fn().mockResolvedValue({ items: [detail().summary], nextCursor: 'next-page' }), evidence: vi.fn().mockResolvedValue(detail()), incidents: vi.fn().mockResolvedValue({ incidents: [incident()], state: restricted }), flag: vi.fn().mockResolvedValue({ incident: incident() }), resolve: vi.fn().mockResolvedValue({ incident: incident(true) }) })
let host: HTMLDivElement, root: Root
beforeEach(() => { vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true); host = document.createElement('div'); document.body.append(host); root = createRoot(host) })
afterEach(async () => { await act(async () => root.unmount()); host.remove(); vi.unstubAllGlobals() })
async function render(node: ReactNode) { await act(async () => root.render(node)) }
async function click(text: string) { const item = [...host.querySelectorAll<HTMLButtonElement>('button')].find(button => button.textContent === text); expect(item).toBeTruthy(); await act(async () => item!.click()) }
async function change(input: HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement, value: string) { await act(async () => { Object.getOwnPropertyDescriptor(Object.getPrototypeOf(input), 'value')!.set!.call(input, value); input.dispatchEvent(new Event(input.tagName === 'SELECT' ? 'change' : 'input', { bubbles: true })) }) }
async function submit(selector = 'form') { await act(async () => host.querySelector(selector)!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))) }
const labelInput = (text: string) => [...host.querySelectorAll('label')].find(label => label.textContent?.startsWith(text))!.querySelector<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>('input, select, textarea')!
function deferred<T>() { let resolve!: (value: T) => void; let reject!: (reason: Error) => void; const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no }); return { promise, resolve, reject } }

describe('admin game analytics', () => {
  it('loads only committed period/date/account filters and omits a date for all time', async () => {
    const api = gateway(); await render(<AdminGameAnalyticsPanel api={api} />)
    expect(api.metrics).toHaveBeenLastCalledWith('day', undefined, undefined)
    await change(labelInput('Period'), 'month'); await change(labelInput('Date'), '2026-10'); await change(labelInput('Account ID'), '  alice  ')
    expect(api.metrics).toHaveBeenCalledTimes(1); await submit()
    expect(api.metrics).toHaveBeenLastCalledWith('month', '2026-10', 'alice')
    await change(labelInput('Period'), 'alltime'); await submit()
    expect(api.metrics).toHaveBeenLastCalledWith('alltime', undefined, 'alice')
    expect(host.textContent).toContain('Historical results have not been estimated.')
  })
  it('pins a player profile query and ignores late responses from an earlier account', async () => {
    const api = gateway(), first = deferred<GameAnalyticsReport>()
    vi.mocked(api.metrics).mockImplementation((_period, _date, id) => id === 'alice' ? first.promise : Promise.resolve(report('mines')))
    await render(<AdminGameAnalyticsPanel api={api} userId="alice" />)
    expect(host.querySelector('input[placeholder="All human players"]')).toBeNull()
    await render(<AdminGameAnalyticsPanel api={api} userId="bob" />)
    expect(api.metrics).toHaveBeenLastCalledWith('day', undefined, 'bob')
    await act(async () => first.resolve(report('hearts')))
    expect(host.querySelector('tbody')?.textContent).toContain('Mines'); expect(host.querySelector('tbody')?.textContent).not.toContain('Hearts')
  })
})

describe('admin private round reviews', () => {
  it('fetches private evidence only on explicit review, escapes payload and excludes bots from attribution', async () => {
    const api = gateway(); await render(<AdminRoundReviewsPanel api={api} />)
    expect(api.evidence).not.toHaveBeenCalled(); expect(host.textContent).not.toContain('privateCards')
    await click('Review round'); expect(api.evidence).toHaveBeenCalledWith('audit-1')
    expect(host.querySelector('details')?.open).toBe(false); expect(host.querySelector('pre')?.textContent).toContain('privateCards')
    expect(host.querySelector('script')).toBeNull()
    const players = labelInput('Player') as HTMLSelectElement
    expect([...players.options].map(option => option.value)).toEqual(['', 'alice'])
    expect(api.flag).not.toHaveBeenCalled()
  })
  it('uses filters and opaque cursors and cannot attribute deleted evidence', async () => {
    const api = gateway(); await render(<AdminRoundReviewsPanel api={api} />)
    await click('Next page →'); expect(api.rounds).toHaveBeenLastCalledWith(undefined, undefined, 'next-page')
    await change(labelInput('Game ID'), '  texas-holdem '); await change(labelInput('Audit status'), 'flagged'); await submit()
    expect(api.rounds).toHaveBeenLastCalledWith('texas-holdem', 'flagged', null)
    vi.mocked(api.evidence).mockResolvedValue(detail(false)); await click('Review round')
    expect(host.textContent).toContain('Detailed evidence is no longer retained.')
    expect(host.querySelector('.admin-incident-form')).toBeNull()
  })
  it('requires attribution and acknowledgement and retries one immutable incident after an uncertain write', async () => {
    const api = gateway(), pending = deferred<{ incident: RiskIncident }>()
    vi.mocked(api.flag).mockImplementationOnce(() => pending.promise).mockResolvedValueOnce({ incident: incident() })
    await render(<AdminRoundReviewsPanel api={api} />); await click('Review round')
    await submit('.admin-incident-form'); expect(api.flag).not.toHaveBeenCalled()
    await change(labelInput('Player'), 'alice'); await change(labelInput('Severity'), 'high'); await change(labelInput('Rule or issue'), 'action-rule')
    await change(labelInput('Evidence and reason'), '  Reviewed actions linked to this human participant.  ')
    await act(async () => host.querySelector<HTMLInputElement>('input[type=checkbox]')!.click())
    await submit('.admin-incident-form'); await submit('.admin-incident-form')
    expect(api.flag).toHaveBeenCalledTimes(1); expect(host.textContent).not.toContain('Incident recorded and evidence held.')
    await act(async () => pending.reject(new Error('Connection interrupted')))
    expect(host.textContent).toContain('Connection interrupted'); expect((labelInput('Rule or issue') as HTMLInputElement).readOnly).toBe(true)
    await submit('.admin-incident-form')
    const draft = vi.mocked(api.flag).mock.calls[0][0]
    expect(draft).toEqual({ userId: 'alice', gameId: 'texas-holdem', roundId: 'round-1', severity: 'high', sourceIncidentId: 'audit-1', ruleId: 'action-rule', ruleVersion: 'admin-v1', attributionBasis: 'Reviewed actions linked to this human participant.', evidenceIds: ['audit-1'] })
    expect(vi.mocked(api.flag).mock.calls[1][0]).toEqual(draft)
    expect(host.textContent).toContain('Incident recorded and evidence held.')
  })
})

describe('admin restriction decisions', () => {
  it('does not claim a cleared restriction until save and refreshed authoritative state both finish', async () => {
    const api = gateway(), save = deferred<{ incident: RiskIncident }>(), refreshed = deferred<{ incidents: RiskIncident[]; state: typeof restricted }>()
    vi.mocked(api.resolve).mockReturnValueOnce(save.promise)
    vi.mocked(api.incidents).mockResolvedValueOnce({ incidents: [incident()], state: restricted }).mockReturnValueOnce(refreshed.promise)
    await render(<AdminRiskIncidentsPanel api={api} userId="alice" />); await click('Resolve incident')
    await change(labelInput('Decision reason'), 'The evidence shows a server defect.'); await submit('.admin-incident-form'); await submit('.admin-incident-form')
    expect(api.resolve).toHaveBeenCalledTimes(1); expect(host.textContent).toContain('Account restricted'); expect(host.textContent).not.toContain('Account access available')
    await act(async () => save.resolve({ incident: incident(true) }))
    expect(host.textContent).toContain('Loading incidents…'); expect(host.textContent).not.toContain('Account access available')
    await act(async () => refreshed.resolve({ incidents: [incident(true)], state: { accountRestricted: false, withdrawalsBlocked: false, blockedGameIds: [] } }))
    expect(host.textContent).toContain('Account access available'); expect(api.resolve).toHaveBeenCalledWith('incident-1', 'cleared', 'The evidence shows a server defect.', 'high')
  })
  it('retains the original restriction and immutable review reason on failed save, then retries the same decision', async () => {
    const api = gateway(); vi.mocked(api.resolve).mockRejectedValueOnce(new Error('Save unavailable'))
    await render(<AdminRiskIncidentsPanel api={api} userId="alice" />); await click('Resolve incident')
    await change(labelInput('Decision'), 'upheld'); await change(labelInput('Decision reason'), 'The evidence confirms the recorded action.'); await submit('.admin-incident-form')
    expect(api.resolve).toHaveBeenCalledWith('incident-1', 'upheld', 'The evidence confirms the recorded action.', 'high')
    expect(host.textContent).toContain('Save unavailable'); expect(host.textContent).toContain('Account restricted')
    expect((labelInput('Decision') as HTMLSelectElement).disabled).toBe(true); expect((labelInput('Decision reason') as HTMLTextAreaElement).readOnly).toBe(true)
    expect((labelInput('Reviewed severity') as HTMLSelectElement).disabled).toBe(true)
    await submit('.admin-incident-form')
    expect(vi.mocked(api.resolve).mock.calls[1]).toEqual(vi.mocked(api.resolve).mock.calls[0])
  })
  it('updates the existing incident to critical only after an explicit selection and retries that same severity', async () => {
    const api = gateway(); vi.mocked(api.resolve).mockRejectedValueOnce(new Error('Uncertain save'))
    await render(<AdminRiskIncidentsPanel api={api} userId="alice" />); await click('Resolve incident')
    expect(labelInput('Reviewed severity').value).toBe('high')
    await change(labelInput('Decision'), 'upheld')
    expect(labelInput('Reviewed severity').value).toBe('high')
    await change(labelInput('Reviewed severity'), 'critical')
    await change(labelInput('Decision reason'), 'The reviewed evidence supports a critical restriction.')
    await submit('.admin-incident-form')
    expect(api.resolve).toHaveBeenLastCalledWith('incident-1', 'upheld', 'The reviewed evidence supports a critical restriction.', 'critical')
    expect((labelInput('Reviewed severity') as HTMLSelectElement).disabled).toBe(true)
    await submit('.admin-incident-form')
    expect(vi.mocked(api.resolve).mock.calls[1]).toEqual(vi.mocked(api.resolve).mock.calls[0])
    expect(api.flag).not.toHaveBeenCalled()
  })
})
