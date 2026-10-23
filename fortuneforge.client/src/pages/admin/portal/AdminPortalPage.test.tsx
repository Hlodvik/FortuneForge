// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AdminPortalError, type AdminPortalApi, type AdminManagement, type AdminPlayer, type AdminPlayerDetail } from '../../../features/admin/portal/adminPortalApi'
import { AdminPortalPage } from './AdminPortalPage'
import { adminMoney } from '../../../features/admin/portal/adminFormatting'

vi.mock('../../../features/admin/operations/operationsApi', () => ({ getOperationsDashboard: vi.fn().mockRejectedValue(new Error('Reporting offline')) }))
const player = (userId: string): AdminPlayer => ({ userId, playerName: userId, email: `${userId}@example.test`, role: 'player', status: 'active', deactivated: false, isBot: false, createdAtUtc: '2026-10-21T00:00:00Z', lastActiveAtUtc: null })
const detail = (userId: string): AdminPlayerDetail => ({ player: player(userId), balances: { slotsCredits: 19.75, freeGames: null, specialPoints: null, energy: null }, summary: { purchaseCredits: 100, gamingWageredCredits: 30, gamingPaidCredits: 20, gamingRefundedCredits: 0, gamingNetCredits: -10, recordsRead: 5, scope: 'bounded-records', complete: false, limitations: ['Not a lifetime total.'] }, coverage: { complete: false, sources: [], limitations: ['Recorded sources only.'] } })
const state = (): AdminManagement => ({ version: 2, botsEnabled: true, updatedAtUtc: null, games: [{ id: 'hearts', displayName: 'Hearts', category: 'Card game', route: '/cards/hearts', visible: true, botsSupported: true, botsEnabled: true, effectiveBotsEnabled: true }] })
const page = <T,>(items: T[] = []) => ({ items, nextCursor: null, complete: true, limitations: [] })
function gateway(): AdminPortalApi {
  return { players: vi.fn().mockResolvedValue(page([player('first'), player('second')])), player: vi.fn(async id => detail(id)), purchases: vi.fn().mockResolvedValue(page()), gaming: vi.fn().mockResolvedValue(page()), matches: vi.fn().mockResolvedValue(page()), management: vi.fn().mockResolvedValue(state()), audit: vi.fn().mockResolvedValue(page()), change: vi.fn().mockResolvedValue(state()) }
}
let host: HTMLDivElement, root: Root
beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  HTMLDialogElement.prototype.showModal = function () { this.setAttribute('open', '') }
  HTMLDialogElement.prototype.close = function () { this.removeAttribute('open') }
  host = document.createElement('div'); document.body.append(host); root = createRoot(host)
})
afterEach(() => { act(() => root.unmount()); host.remove(); vi.unstubAllGlobals() })
const flush = () => act(async () => { await Promise.resolve(); await Promise.resolve() })
async function render(api: AdminPortalApi) { await act(async () => { root.render(<AdminPortalPage playerName="Test admin" api={api} />) }); await flush() }
async function click(text: string) { const button = [...host.querySelectorAll('button')].find(item => item.textContent === text); expect(button, `button ${text}`).toBeTruthy(); await act(async () => button!.click()); await flush() }
async function reason(value: string) { const input = host.querySelector('textarea')!; await act(async () => { Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value')!.set!.call(input, value); input.dispatchEvent(new Event('input', { bubbles: true })) }) }
async function submit() { await act(async () => { host.querySelector('dialog form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })) }); await flush() }

describe('admin portal', () => {
  it('loads sensitive player records only after choosing the Players section', async () => {
    const api = gateway(); await render(api)
    expect(api.players).not.toHaveBeenCalled()
    await click('♙Players')
    expect(api.players).toHaveBeenCalledWith('', null)
    expect(host.textContent).toContain('first@example.test')
    expect(host.textContent).toContain('Last active')
  })
  it('keeps absent amounts unknown rather than fabricating zero balances', () => {
    expect(adminMoney(null)).toBe('Not recorded')
    expect(adminMoney(undefined)).toBe('Not recorded')
    expect(adminMoney(0)).toMatch(/^R0/)
  })
  it('shows player history and warns that recorded cashflow is not lifetime loss', async () => {
    const api = gateway(); await render(api); await click('♙Players'); await click('View history →')
    expect(api.player).toHaveBeenCalledWith('first')
    expect(api.matches).toHaveBeenCalledWith('first', null)
    expect(host.textContent).toContain('not a lifetime loss calculation')
    expect(host.textContent).toContain('Not a lifetime total.')
    await click('Purchases'); expect(api.purchases).toHaveBeenCalledWith('first', null)
    await click('Wallet & gaming events'); expect(api.gaming).toHaveBeenCalledWith('first', null)
  })
  it('does not show a late first-player response in a second-player profile', async () => {
    const api = gateway(); let resolveFirst!: (value: AdminPlayerDetail) => void
    vi.mocked(api.player).mockImplementation(id => id === 'first' ? new Promise(resolve => { resolveFirst = resolve }) : Promise.resolve(detail(id)))
    await render(api); await click('♙Players'); await click('View history →'); await click('← All players')
    await act(async () => { [...host.querySelectorAll('button')].filter(item => item.textContent === 'View history →')[1].click() }); await flush()
    expect(host.querySelector('.admin-player-summary')?.textContent).toContain('second@example.test')
    await act(async () => resolveFirst(detail('first')))
    expect(host.querySelector('.admin-player-summary')?.textContent).not.toContain('first@example.test')
  })
  it('requires a confirmation and reason, then uses the current settings revision', async () => {
    const api = gateway(); await render(api); await click('▦Game catalogue'); await click('Hide game')
    expect(api.change).not.toHaveBeenCalled()
    expect(host.querySelector<HTMLButtonElement>('dialog button[type=submit]')!.disabled).toBe(true)
    await reason('Temporarily hide for testing'); await submit()
    expect(api.change).toHaveBeenCalledWith(expect.objectContaining({ kind: 'visibility', gameId: 'hearts', enabled: false, expectedVersion: 2, reason: 'Temporarily hide for testing' }))
    expect(host.textContent).toContain('Change saved and audited.')
  })
  it('retries an uncertain write using the same idempotency key and unchanged reason', async () => {
    const api = gateway(); vi.mocked(api.change).mockRejectedValueOnce(new Error('Connection lost')).mockResolvedValueOnce(state())
    await render(api); await click('♟Bot controls'); await click('Pause new bot arrivals'); await reason('Pause arrivals during review'); await submit()
    expect(host.querySelector('textarea')!.readOnly).toBe(true)
    const first = vi.mocked(api.change).mock.calls[0][0]
    await submit()
    expect(vi.mocked(api.change).mock.calls[1][0]).toEqual(first)
    expect(host.textContent).toContain('Change saved and audited.')
  })
  it('reloads after a conflicting version without pretending the change succeeded', async () => {
    const api = gateway(); vi.mocked(api.change).mockRejectedValue(new AdminPortalError('Version changed', 409))
    await render(api); await click('▦Game catalogue'); await click('Hide game'); await reason('Hide while reviewing artwork'); await submit()
    expect(api.management).toHaveBeenCalledTimes(2)
    expect(host.textContent).toContain('Settings changed elsewhere')
    expect(host.textContent).not.toContain('Change saved and audited.')
  })
  it('leaves settings unchanged when a confirmation is cancelled', async () => {
    const api = gateway(); await render(api); await click('▦Game catalogue'); await click('Hide game'); await click('Cancel')
    expect(api.change).not.toHaveBeenCalled(); expect(host.querySelector('dialog')).toBeNull()
  })
})
