import { renderToStaticMarkup } from 'react-dom/server'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { AccountSummary } from '../../features/account/services/accountsApi'

const state = vi.hoisted(() => ({ account: null as AccountSummary | null, isLoading: false, error: null as string | null, portal: vi.fn() }))
vi.mock('../../features/account/useAuthenticatedAccount', () => ({ useAuthenticatedAccount: () => ({ account: state.account, isLoading: state.isLoading, error: state.error, reload: vi.fn() }) }))
vi.mock('../../pages/admin/portal/AdminPortalPage', () => ({ AdminPortalPage: ({ playerName }: { playerName: string }) => { state.portal(playerName); return <main>Admin portal for {playerName}</main> } }))
import { AdminPortalRoute } from './AdminPortalRoute'

const account = (role: string): AccountSummary => ({
  userId: 'route-player', playerName: 'Avery', email: 'avery@example.test', createdAtUtc: '2026-10-21T00:00:00Z',
  balances: { slotsCredits: 100, freeGames: 0 }, role,
  slots: { spinsPlayed: 0, wins: 0, losses: 0, creditsWagered: 0, creditsWon: 0, netCredits: 0 },
})
beforeEach(() => { state.account = account('player'); state.error = null; state.isLoading = false; state.portal.mockClear() })

describe('admin portal route account guard', () => {
  it('never mounts the admin portal for an authenticated non-admin', () => {
    const markup = renderToStaticMarkup(<AdminPortalRoute />)
    expect(markup).toContain('Administrator access required.')
    expect(markup).toContain('href="/home"')
    expect(state.portal).not.toHaveBeenCalled()
  })
  it('does not mount the admin portal while an account refresh is pending', () => {
    state.account = account('admin'); state.isLoading = true
    expect(renderToStaticMarkup(<AdminPortalRoute />)).toContain('Opening the admin portal')
    expect(state.portal).not.toHaveBeenCalled()
  })
  it('allows an authenticated server-designated admin', () => {
    state.account = account('Admin')
    expect(renderToStaticMarkup(<AdminPortalRoute />)).toContain('Admin portal for Avery')
    expect(state.portal).toHaveBeenCalledExactlyOnceWith('Avery')
  })
})
