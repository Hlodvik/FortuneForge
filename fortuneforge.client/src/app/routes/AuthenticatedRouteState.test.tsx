// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { GameLoadingRouteProvider } from '../../features/game-loading/GameLoadingRouteContext'
import type { GameLoadingEntry } from '../../features/game-loading/types'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'

const entry: GameLoadingEntry = {
  id: 'blackjack', title: 'Fortune Blackjack', routes: ['/cards/blackjack'],
  image: '/blackjack-loading.webp', background: '#071a15', accent: '#dfbf78',
}
const retry = vi.fn()
let host: HTMLDivElement
let root: Root

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  retry.mockReset()
  host = document.createElement('div'); document.body.append(host)
  root = createRoot(host)
})
afterEach(() => { act(() => root.unmount()); host.remove(); vi.useRealTimers(); vi.unstubAllGlobals() })

function PendingState({ error = null }: { error?: string | null }) {
  return <AuthenticatedRouteState error={error} loadingLabel="Opening the live Blackjack table…" errorTitle="The Blackjack table could not be opened." onRetry={retry} />
}

describe('authenticated route pending presentation', () => {
  it('uses route-supplied game artwork only while account work is pending', () => {
    act(() => root.render(<GameLoadingRouteProvider entry={entry}><PendingState /></GameLoadingRouteProvider>))
    expect(host.querySelector('[data-game-loading]')?.getAttribute('data-game-loading')).toBe('blackjack')
    expect(host.querySelector('[role="status"]')?.textContent).toBe('Loading Fortune Blackjack…')
    expect(host.querySelector('img')?.getAttribute('src')).toBe(entry.image)
    expect(host.querySelector('[role="alert"], button, a, progress, [role="progressbar"]')).toBeNull()
    expect(retry).not.toHaveBeenCalled()
  })

  it('preserves the original text-only loading state without a game entry', () => {
    act(() => root.render(<GameLoadingRouteProvider><PendingState /></GameLoadingRouteProvider>))
    expect(host.querySelector('[role="status"]')?.textContent).toBe('Opening the live Blackjack table…')
    expect(host.querySelector('[data-game-loading], img, [role="alert"]')).toBeNull()
    act(() => root.render(<PendingState />))
    expect(host.querySelector('[role="status"]')?.textContent).toBe('Opening the live Blackjack table…')
  })

  it('preserves the original error title, detail, retry callback and Return home link', () => {
    act(() => root.render(<GameLoadingRouteProvider entry={entry}><PendingState error="The account service could not be reached." /></GameLoadingRouteProvider>))
    const alert = host.querySelector('[role="alert"]')!
    expect(alert.querySelector('strong')?.textContent).toBe('The Blackjack table could not be opened.')
    expect(alert.querySelector('span')?.textContent).toBe('The account service could not be reached.')
    expect(host.querySelector('[data-game-loading], [role="status"], img')).toBeNull()
    const button = alert.querySelector<HTMLButtonElement>('button')!
    expect(button.textContent?.trim()).toBe('Try again')
    act(() => button.click())
    expect(retry).toHaveBeenCalledOnce()
    expect(alert.querySelector('a')?.getAttribute('href')).toBe('/home')
    expect(alert.querySelector('a')?.textContent).toBe('Return home')
  })

  it('leaves loading immediately when the caller becomes ready without adding timers', () => {
    vi.useFakeTimers()
    function Gate({ pending }: { pending: boolean }) {
      return <GameLoadingRouteProvider entry={entry}>{pending ? <PendingState /> : <main data-testid="game-ready">The table is ready</main>}</GameLoadingRouteProvider>
    }
    act(() => root.render(<Gate pending />))
    expect(host.querySelector('[data-game-loading]')).not.toBeNull()
    expect(vi.getTimerCount()).toBe(0)
    act(() => root.render(<Gate pending={false} />))
    expect(host.querySelector('[data-testid="game-ready"]')?.textContent).toBe('The table is ready')
    expect(host.querySelector('[data-game-loading], [role="status"]')).toBeNull()
    expect(vi.getTimerCount()).toBe(0)
  })

  it('updates the artwork for the next caller-supplied route without retaining the previous entry', () => {
    act(() => root.render(<GameLoadingRouteProvider entry={entry}><PendingState /></GameLoadingRouteProvider>))
    const hearts = { ...entry, id: 'hearts', title: 'Hearts', routes: ['/cards/hearts'], image: '/hearts-loading.webp' }
    act(() => root.render(<GameLoadingRouteProvider entry={hearts}><PendingState /></GameLoadingRouteProvider>))
    expect(host.querySelector('[data-game-loading]')?.getAttribute('data-game-loading')).toBe('hearts')
    expect(host.querySelector('[role="status"]')?.textContent).toBe('Loading Hearts…')
    expect(host.querySelector('img')?.getAttribute('src')).toBe(hearts.image)
    expect(host.textContent).not.toContain('Fortune Blackjack')
  })
})
