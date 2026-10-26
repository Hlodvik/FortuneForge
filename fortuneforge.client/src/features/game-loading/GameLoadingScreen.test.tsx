// @vitest-environment jsdom
import { readFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { act, lazy, Suspense, type ReactNode } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { GameLoadingScreen } from './GameLoadingScreen'
import type { GameLoadingEntry } from './types'

const entry: GameLoadingEntry = {
  id: 'blackjack', title: 'Fortune Blackjack', routes: ['/cards/blackjack'],
  image: '/selected-blackjack.webp', background: '#071a15', accent: '#dfbf78',
}
let host: HTMLDivElement
let root: Root

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  host = document.createElement('div')
  document.body.append(host)
  root = createRoot(host)
})

afterEach(() => {
  act(() => root.unmount())
  host.remove()
  vi.useRealTimers()
  vi.unstubAllGlobals()
})

describe('GameLoadingScreen', () => {
  it('names the game and announces an indeterminate pending status without requiring interaction', () => {
    act(() => root.render(<GameLoadingScreen entry={entry} />))
    const section = host.querySelector('section')!
    const heading = host.querySelector('h1')!
    const status = host.querySelector('[role="status"]')!
    expect(heading.textContent).toBe(entry.title)
    expect(section.getAttribute('aria-labelledby')).toBe(heading.id)
    expect(status.textContent).toBe(`Loading ${entry.title}…`)
    expect(status.getAttribute('aria-live')).toBe('polite')
    expect(status.getAttribute('aria-atomic')).toBe('true')
    expect(status.querySelector('[aria-hidden="true"]')).not.toBeNull()
    expect(host.querySelector('button, a, input, audio, video, progress, [role="progressbar"], [aria-valuenow]')).toBeNull()
    expect(host.textContent).not.toMatch(/\d+\s*%|click|tap|continue/i)
    expect(section.style.getPropertyValue('--game-loading-background')).toBe(entry.background)
    expect(section.style.getPropertyValue('--game-loading-accent')).toBe(entry.accent)
    const image = host.querySelector('img')!
    expect(image.getAttribute('src')).toBe(entry.image)
    expect(image.getAttribute('alt')).toBe('')
    expect([image.width, image.height]).toEqual([768, 384])
  })

  it('renders an accessible generic dark fallback without reading the current route', () => {
    history.replaceState(null, '', '/games/unknown?private=value')
    act(() => root.render(<GameLoadingScreen />))
    expect(host.querySelector('section')?.dataset.gameLoading).toBe('generic')
    expect(host.querySelector('h1')?.textContent).toBe('Your game')
    expect(host.querySelector('[role="status"]')?.textContent).toBe('Loading game…')
    expect(host.querySelector('img')).toBeNull()
    expect(host.textContent).not.toMatch(/unknown|private|value/)
    expect(host.querySelector<HTMLElement>('section')!.style.getPropertyValue('--game-loading-background')).toMatch(/^#[\da-f]{6}$/i)
  })

  it('keeps the visible game title and status when artwork is missing or fails', () => {
    act(() => root.render(<GameLoadingScreen entry={{ ...entry, image: '' }} />))
    expect(host.querySelector('img')).toBeNull()
    expect(host.querySelector('h1')?.className).toContain('fallback-title')
    act(() => root.render(<GameLoadingScreen entry={entry} />))
    act(() => host.querySelector('img')!.dispatchEvent(new Event('error')))
    expect(host.querySelector('img')).toBeNull()
    expect(host.querySelector('h1')?.textContent).toBe(entry.title)
    expect(host.querySelector('h1')?.className).toContain('fallback-title')
    expect(host.querySelector('[role="status"]')?.textContent).toBe(`Loading ${entry.title}…`)
  })

  it('shows the next game artwork when a previous game image failed', () => {
    act(() => root.render(<GameLoadingScreen entry={entry} />))
    act(() => host.querySelector('img')!.dispatchEvent(new Event('error')))
    const next = { ...entry, id: 'roulette', title: 'Roulette', image: '/selected-roulette.webp' }
    act(() => root.render(<GameLoadingScreen entry={next} />))
    expect(host.querySelector('img')?.getAttribute('src')).toBe(next.image)
    expect(host.querySelector('h1')?.textContent).toBe(next.title)
    expect(host.querySelector('[role="status"]')?.textContent).toBe('Loading Roulette…')
  })

  it('adds no timer or minimum display duration to a caller-owned pending state', () => {
    vi.useFakeTimers()
    act(() => root.render(<GameLoadingScreen entry={entry} />))
    expect(vi.getTimerCount()).toBe(0)
    act(() => root.render(<main>Game ready</main>))
    expect(host.textContent).toBe('Game ready')
    expect(host.querySelector('[role="status"], [data-game-loading]')).toBeNull()
    expect(vi.getTimerCount()).toBe(0)
  })

  it('leaves the screen as soon as a genuinely pending lazy chunk resolves', async () => {
    let resolveGame!: (module: { default: () => ReactNode }) => void
    const Game = lazy(() => new Promise<{ default: () => ReactNode }>((resolve) => { resolveGame = resolve }))
    await act(async () => root.render(
      <Suspense fallback={<GameLoadingScreen entry={entry} />}><Game /></Suspense>,
    ))
    expect(host.querySelector('[role="status"]')).not.toBeNull()
    await act(async () => resolveGame({ default: () => <main>Loaded game</main> }))
    expect(host.textContent).toBe('Loaded game')
    expect(host.querySelector('[data-game-loading]')).toBeNull()
  })

  it('retains complete title artwork and disables indicator motion for reduced-motion users', () => {
    const css = readFileSync(join(dirname(fileURLToPath(import.meta.url)), 'gameLoadingScreen.css'), 'utf8')
    expect(css).toMatch(/\.ff-game-loading__image\s*\{[^}]*object-fit:\s*contain/s)
    expect(css).toMatch(/@media\s*\(prefers-reduced-motion:\s*reduce\)\s*\{\s*\.ff-game-loading__indicator\s*\{[^}]*animation:\s*none/s)
    expect(css).toMatch(/\.ff-game-loading__status\s*>\s*:last-child\s*\{[^}]*overflow-wrap:\s*anywhere/s)
  })
})
