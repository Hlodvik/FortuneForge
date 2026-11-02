// @vitest-environment jsdom
import { act } from 'react'
import { createRoot } from 'react-dom/client'
import { afterEach, expect, it, vi } from 'vitest'

const accountApi = vi.hoisted(() => ({ createAccount: vi.fn(), resendVerification: vi.fn() }))
vi.mock('../../features/account/services/accountsApi', () => accountApi)

import { CreateAccountPage } from './CreateAccountPage'

afterEach(() => { accountApi.createAccount.mockReset(); accountApi.resendVerification.mockReset(); vi.unstubAllGlobals() })

function typeInto(input: HTMLInputElement, value: string) {
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set
  if (!setter) throw new Error('The test environment cannot set input values.')
  setter.call(input, value)
  input.dispatchEvent(new Event('input', { bubbles: true }))
}

it('shows live password checks and requires confirmation before account creation', async () => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  const host = document.createElement('div')
  document.body.append(host)
  const root = createRoot(host)
  try {
    await act(async () => root.render(<CreateAccountPage />))
    expect(host.querySelector('a[aria-label="Fortune Forge home"]')?.getAttribute('href')).toBe('/')
    expect(host.textContent).not.toContain('Back home')
    expect(host.querySelector<HTMLInputElement>('[name="playerName"]')?.labels?.[0]?.textContent).toContain('Username')

    const password = host.querySelector<HTMLInputElement>('[name="password"]')!
    const confirmation = host.querySelector<HTMLInputElement>('[name="confirmedPassword"]')!
    const submit = host.querySelector<HTMLButtonElement>('button[type="submit"]')!
    expect(submit.disabled).toBe(true)

    await act(async () => typeInto(password, 'Passw0rd!'))
    expect(host.querySelectorAll('.password-requirements li.is-met')).toHaveLength(4)
    expect(submit.disabled).toBe(true)

    await act(async () => typeInto(confirmation, 'Passw0rd?'))
    expect(host.textContent).toContain('Passwords do not match yet')
    expect(submit.disabled).toBe(true)

    await act(async () => typeInto(confirmation, 'Passw0rd!'))
    expect(host.textContent).toContain('Passwords match')
    expect(submit.disabled).toBe(false)
  } finally {
    await act(async () => root.unmount())
    host.remove()
  }
})
