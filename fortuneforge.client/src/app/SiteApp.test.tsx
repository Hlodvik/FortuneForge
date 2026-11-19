// Edited: 2026-11-18T22:51:39-06:00 | Computer: Vinnabok
// @vitest-environment jsdom
import { act, type ReactNode } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { SiteApp } from './SiteApp'
import { resolveSiteRoute } from './SiteRoutes'

vi.mock('../features/account/AccountAccessBoundary', () => ({ AccountAccessBoundary: ({ children }: { children: ReactNode }) => children }))
vi.mock('../components/SiteNavbar', () => ({ SiteNavbar: ({ mode }: { mode: string }) => <nav data-mode={mode} /> }))
vi.mock('../pages/landing/LandingPage', () => ({ LandingPage: () => <main>Public landing<a href="/slots/arcane-archives">Play Arcane Archives</a></main> }))
vi.mock('../pages/account/AccountHomePage', () => ({ HomePage: () => <main>Verified account home</main> }))
vi.mock('../pages/auth/LoginPage', () => ({ LoginPage: () => <main>Log in</main> }))
vi.mock('../pages/not-found/NotFoundPage', () => ({ NotFoundPage: () => <main>Page unavailable</main> }))

let root: Root, host: HTMLDivElement
beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  window.history.replaceState({}, '', '/')
  host = document.createElement('div')
  document.body.append(host)
  root = createRoot(host)
})
afterEach(() => { act(() => root.unmount()); host.remove(); vi.unstubAllGlobals() })

const renderPath = async (pathname: string) => {
  window.history.replaceState({}, '', pathname)
  await act(async () => { root.render(<SiteApp />); window.dispatchEvent(new PopStateEvent('popstate')) })
}

describe('isolated site entry', () => {
  it('keeps public, account and login URLs on their shared site routes', async () => {
    await renderPath('/')
    expect(host.textContent).toContain('Public landing')
    await renderPath('/home')
    expect(host.textContent).toBe('Verified account home')
    await renderPath('/login')
    expect(host.textContent).toBe('Log in')
    await renderPath('/missing')
    expect(host.textContent).toBe('Page unavailable')
  })

  it('leaves game links as document navigation without changing browser history', async () => {
    await renderPath('/')
    const link = host.querySelector('a')!
    let intercepted: boolean | undefined
    // Observe after all application listeners and stop jsdom's native navigation.
    const observer = (event: MouseEvent) => { intercepted = event.defaultPrevented; event.preventDefault() }
    window.addEventListener('click', observer, { once: true })
    await act(async () => { link.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, button: 0 })) })
    expect(intercepted).toBe(false)
    expect(window.location.pathname).toBe('/')
    expect(link.getAttribute('href')).toBe('/slots/arcane-archives')
  })

  it.each(['/slots/arcane-archives', '/games/mines', '/cards/blackjack', '/demo/cards/blackjack'])('hands %s to the preserved game entry', pathname => {
    expect(resolveSiteRoute(pathname)).toBeNull()
  })
})
