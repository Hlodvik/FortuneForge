// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AdminGameReportsPanel } from './AdminGameReportsPanel'
import { AdminPortalError } from '../../../features/admin/portal/adminPortalApi'
import type { AdminGameReportsApi, GameReport } from '../../../features/reports/gameReportsApi'
const report: GameReport = { id: 'a'.repeat(64), userId: 'reporting-player', gameId: 'dragon-tiger', gameName: 'Dragon Tiger', path: '/games/dragon-tiger', category: 'bug', message: '<script>not executable</script> The deal button stopped.', status: 'open', version: 0, createdAtUtc: '2026-10-24T18:00:00Z', updatedAtUtc: '2026-10-24T18:00:00Z', adminNote: '', updatedBy: null }
function api(): AdminGameReportsApi { return { list: vi.fn().mockResolvedValue({ items: [report], nextCursor: null }), detail: vi.fn().mockResolvedValue(report), update: vi.fn().mockResolvedValue({ ...report, status: 'in-review', version: 1 }) } }
let host: HTMLDivElement, root: Root
beforeEach(() => { vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true); host = document.createElement('div'); document.body.append(host); root = createRoot(host) })
afterEach(() => { act(() => root.unmount()); host.remove(); vi.unstubAllGlobals() })
const flush = () => act(async () => { await Promise.resolve(); await Promise.resolve() })
async function render(gateway: AdminGameReportsApi) { await act(async () => root.render(<AdminGameReportsPanel api={gateway}/>)); await flush() }
async function click(text: string) { const button = [...host.querySelectorAll('button')].find(item => item.textContent === text)!; expect(button).toBeTruthy(); await act(async () => button.click()); await flush() }
describe('admin player reports', () => {
  it('lists and opens persisted reports as text without executing submitted markup', async () => {
    const gateway = api(); await render(gateway); expect(gateway.list).toHaveBeenCalledWith(null)
    expect(host.querySelector('script')).toBeNull(); await click('Read report')
    expect(gateway.detail).toHaveBeenCalledWith(report.id)
    expect(host.textContent).toContain('reporting-player'); expect(host.textContent).toContain('<script>not executable</script>')
    expect(host.querySelector('script')).toBeNull()
  })
  it('updates status using the displayed revision and records the saved response', async () => {
    const gateway = api(); await render(gateway); await click('Read report')
    const select = host.querySelector('select')!; await act(async () => { select.value = 'in-review'; select.dispatchEvent(new Event('change', { bubbles: true })) })
    await act(async () => host.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))); await flush()
    expect(gateway.update).toHaveBeenCalledWith(report, 'in-review', '')
    expect(host.textContent).toContain('Report updated')
  })
  it('reloads a version conflict instead of pretending the requested change was saved', async () => {
    const gateway = api(); vi.mocked(gateway.update).mockRejectedValue(new AdminPortalError('Changed elsewhere', 409))
    await render(gateway); await click('Read report')
    const select = host.querySelector('select')!; await act(async () => { select.value = 'resolved'; select.dispatchEvent(new Event('change', { bubbles: true })) })
    await act(async () => host.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))); await flush()
    expect(gateway.detail).toHaveBeenCalledTimes(2); expect(host.textContent).toContain('changed elsewhere')
    expect(host.textContent).not.toContain('Report updated.')
  })
})
