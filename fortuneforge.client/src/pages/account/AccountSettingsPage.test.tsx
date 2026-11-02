// @vitest-environment jsdom
import { act } from 'react'
import { createRoot } from 'react-dom/client'
import { afterEach, describe, expect, it, vi } from 'vitest'

const state = vi.hoisted(() => ({
  loading: false,
  error: null as string | null,
  reload: vi.fn(),
}))

vi.mock('../../components/PlayerAccountMenus', () => ({ PlayerAccountMenus: () => <details><summary>Account menu</summary></details> }))
vi.mock('../../features/account/useAuthenticatedAccount', () => ({ useAuthenticatedAccount: () => ({
  account: state.loading || state.error ? null : {
    userId: 'settings-test', playerName: 'Avery', email: 'avery@example.com',
    role: 'player', balances: { slotsCredits: 42.5 },
  },
  isLoading: state.loading, error: state.error, reload: state.reload,
}) }))

import { AccountSettingsPage } from './AccountSettingsPage'

afterEach(() => {
  state.loading = false
  state.error = null
  state.reload.mockReset()
  vi.unstubAllGlobals()
})

describe('account settings', () => {
  it('keeps account controls usable inside the shared player shell', async () => {
    const host = document.createElement('div')
    document.body.append(host)
    const root = createRoot(host)
    vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
    try {
      await act(async () => root.render(<AccountSettingsPage />))
      expect(host.querySelectorAll('[data-site-navbar]')).toHaveLength(1)
      expect(host.querySelector('.home-lobby__sidebar')).toBeNull()
      expect(host.querySelector('.account-settings__profile')?.getAttribute('aria-label')).toBe('Your profile')
      expect(host.textContent).toContain('avery@example.com')

      const username = Array.from(host.querySelectorAll<HTMLButtonElement>('.account-settings__actions button')).find(button => button.textContent?.includes('Change username'))!
      const password = host.querySelector<HTMLAnchorElement>('.account-settings__actions a[href="/home/change-password"]')!
      const deactivate = Array.from(host.querySelectorAll<HTMLButtonElement>('.account-settings__actions button')).find(button => button.textContent?.includes('Deactivate account'))!
      await act(async () => username.click())
      expect(username.getAttribute('aria-expanded')).toBe('true')
      expect(host.querySelector<HTMLInputElement>('input[name="playerName"]')?.value).toBe('Avery')
      expect(password.textContent).toContain('Change password')
      expect(host.querySelector('input[name="newPassword"]')).toBeNull()
      await act(async () => deactivate.click())
      expect(username.getAttribute('aria-expanded')).toBe('false')
      expect(host.querySelector('input[name="playerName"]')).toBeNull()
      expect(host.querySelector('input[name="deletePassword"]')).not.toBeNull()
      expect(host.querySelector('a[href="/home/history"]')).not.toBeNull()
    } finally {
      await act(async () => root.unmount())
      host.remove()
    }
  })
})
