// @vitest-environment jsdom
import { act } from 'react'
import { createRoot } from 'react-dom/client'
import { afterEach, expect, it, vi } from 'vitest'

vi.mock('../../components/PlayerAccountMenus', () => ({ PlayerAccountMenus: () => <details><summary>Account menu</summary><a href="/home/settings">Settings</a></details> }))
vi.mock('../../features/account/useAuthenticatedAccount', () => ({ useAuthenticatedAccount: () => ({
  account: { userId: 'player-1', playerName: 'Avery', role: 'Player', email: 'avery@example.com', balances: { slotsCredits: 100 } },
  error: null, isLoading: false, reload: vi.fn(),
}) }))
vi.mock('../../features/catalog/recentGameActivity', () => ({ useRecentGameActivity: () => ({ games: [], isLoading: false, error: null, refresh: vi.fn() }) }))
vi.mock('../../features/catalog/catalogPopularity', async importOriginal => ({
  ...await importOriginal<typeof import('../../features/catalog/catalogPopularity')>(),
  useCataloguePopularity: () => ({ games: null, isLoading: false, error: null, reload: vi.fn() }),
}))
vi.mock('../../features/catalog/catalogVisibility', () => ({ useCatalogueVisibility: () => ({ loading: false, error: null, isVisible: () => true }) }))
vi.mock('./AccountHistoryPage', () => ({ AccountHistoryContent: () => <section>History panel content</section> }))
vi.mock('./AccountSettingsPage', () => ({ AccountSettingsContent: () => <section>Settings panel content <a href="/home/history">Game history</a></section> }))

import { HomePage } from './AccountHomePage'

afterEach(() => {
  window.history.replaceState(null, '', '/')
  document.body.style.overflow = ''
  delete (HTMLDialogElement.prototype as Partial<HTMLDialogElement>).showModal
  delete (HTMLDialogElement.prototype as Partial<HTMLDialogElement>).close
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

function stubDialog() {
  HTMLDialogElement.prototype.showModal = function () { this.setAttribute('open', '') }
  HTMLDialogElement.prototype.close = function () { this.removeAttribute('open') }
}

it('opens History over Home without a page reload and returns to Home on close', async () => {
  stubDialog()
  window.history.replaceState(null, '', '/home')
  const back = vi.spyOn(window.history, 'back').mockImplementation(() => {
    window.history.replaceState(null, '', '/home')
    window.dispatchEvent(new PopStateEvent('popstate'))
  })
  const host = document.createElement('div')
  document.body.append(host)
  const root = createRoot(host)
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  try {
    await act(async () => root.render(<HomePage />))
    await act(async () => host.querySelector<HTMLAnchorElement>('.home-lobby__navigation a[href="/home/history"]')!.click())
    const dialog = host.querySelector<HTMLDialogElement>('.home-lobby__overlay')!
    expect(window.location.pathname).toBe('/home/history')
    expect(dialog.open).toBe(true)
    expect(dialog.textContent).toContain('History panel content')
    expect(host.querySelector('.home-lobby__dashboard')).not.toBeNull()
    expect(host.querySelector('.home-lobby__navigation [aria-current="page"]')?.textContent).toBe('History')
    await act(async () => host.querySelector<HTMLButtonElement>('.home-lobby__overlay-close')!.click())
    expect(back).toHaveBeenCalledOnce()
    expect(window.location.pathname).toBe('/home')
    expect(dialog.open).toBe(false)
  } finally {
    await act(async () => root.unmount())
    host.remove()
  }
})

it('opens a direct Settings link as a panel and replaces the URL on close', async () => {
  stubDialog()
  window.history.replaceState(null, '', '/home/settings')
  const host = document.createElement('div')
  document.body.append(host)
  const root = createRoot(host)
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  try {
    await act(async () => root.render(<HomePage />))
    const dialog = host.querySelector<HTMLDialogElement>('.home-lobby__overlay')!
    expect(dialog.open).toBe(true)
    expect(dialog.textContent).toContain('Settings panel content')
    expect(host.querySelector('.home-lobby__dashboard')).not.toBeNull()
    await act(async () => host.querySelector<HTMLAnchorElement>('.home-lobby__overlay a[href="/home/history"]')!.click())
    expect(window.location.pathname).toBe('/home/history')
    expect(dialog.textContent).toContain('History panel content')
    await act(async () => host.querySelector<HTMLButtonElement>('.home-lobby__overlay-close')!.click())
    expect(window.location.pathname).toBe('/home')
    expect(dialog.open).toBe(false)
  } finally {
    await act(async () => root.unmount())
    host.remove()
  }
})
