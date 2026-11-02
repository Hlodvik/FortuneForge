// @vitest-environment jsdom
import { act } from 'react'
import { createRoot } from 'react-dom/client'
import { afterEach, expect, it, vi } from 'vitest'

const state = vi.hoisted(() => ({ changePassword: vi.fn().mockResolvedValue(undefined) }))

vi.mock('../../components/PlayerAccountMenus', () => ({ PlayerAccountMenus: () => <details><summary>Account menu</summary></details> }))
vi.mock('../../features/account/useAuthenticatedAccount', () => ({ useAuthenticatedAccount: () => ({
  account: { userId: 'password-test', playerName: 'Avery', email: 'avery@example.com', role: 'Player', balances: { slotsCredits: 42.5 } },
  isLoading: false, error: null, reload: vi.fn(),
}) }))
vi.mock('../../features/account/services/accountsApi', async (importOriginal) => ({
  ...await importOriginal<typeof import('../../features/account/services/accountsApi')>(),
  changeAccountPassword: state.changePassword,
}))

import { AccountPasswordPage } from './AccountPasswordPage'

afterEach(() => { state.changePassword.mockClear(); vi.unstubAllGlobals() })

it('keeps password changes on their own account page', async () => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  const host = document.createElement('div')
  document.body.append(host)
  const root = createRoot(host)
  try {
    await act(async () => root.render(<AccountPasswordPage />))
    expect(host.querySelector('h1')?.textContent).toBe('Change password')
    expect(host.querySelectorAll('[data-site-navbar]')).toHaveLength(1)
      expect(host.querySelector('.home-lobby__sidebar')).toBeNull()
    const form = host.querySelector('form.account-settings__panel')!
    ;(form.querySelector('[name="currentPassword"]') as HTMLInputElement).value = 'old-password'
    ;(form.querySelector('[name="newPassword"]') as HTMLInputElement).value = 'New-password1!'
    ;(form.querySelector('[name="confirmedPassword"]') as HTMLInputElement).value = 'New-password1!'
    await act(async () => { form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); await Promise.resolve() })
    expect(state.changePassword).toHaveBeenCalledWith('old-password', 'New-password1!')
    expect(host.textContent).toContain('Password updated')
    expect(host.querySelector('a[href="/home/settings"]')).not.toBeNull()
  } finally {
    await act(async () => root.unmount())
    host.remove()
  }
})
