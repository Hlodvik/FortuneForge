// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { LoginPage } from './LoginPage'

vi.mock('../../features/account/useOptionalAccountSession', () => ({
  useOptionalAccountSession: () => ({ account: null, isLoading: false }),
}))

let host: HTMLDivElement
let root: Root

beforeEach(async () => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  window.history.replaceState({}, '', '/login')
  host = document.createElement('div')
  document.body.append(host)
  root = createRoot(host)
  await act(async () => root.render(<LoginPage />))
})

afterEach(async () => {
  await act(async () => root.unmount())
  host.remove()
  vi.unstubAllGlobals()
})

describe('login credential autofill', () => {
  it('exposes a recognizable username and current-password pair', () => {
    const form = host.querySelector<HTMLFormElement>('form')!
    const username = host.querySelector<HTMLInputElement>('#login-email')!
    const password = host.querySelector<HTMLInputElement>('#login-password')!

    expect(form.getAttribute('autocomplete')).toBe('on')
    expect(username.name).toBe('email')
    expect(username.getAttribute('autocomplete')).toBe('username')
    expect(password.name).toBe('password')
    expect(password.getAttribute('autocomplete')).toBe('current-password')
    expect(username.labels?.[0]?.textContent).toContain('Email')
    expect(password.labels?.[0]?.textContent).toContain('Password')
  })
})
