// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { GameCatalogueRail } from './GameCatalogueRail'

let host: HTMLDivElement, root: Root
let resize: () => void
const opened = vi.fn()

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  vi.stubGlobal('ResizeObserver', class {
    constructor(callback: () => void) { resize = callback }
    observe() {}
    disconnect() {}
  })
  vi.stubGlobal('matchMedia', vi.fn(() => ({ matches: false })))
  host = document.createElement('div'); document.body.append(host); root = createRoot(host)
})
afterEach(() => { act(() => root.unmount()); host.remove(); vi.unstubAllGlobals(); vi.clearAllMocks() })

function cards(prefix = 'game', count = 8) {
  return Array.from({ length: count }, (_, index) => <a key={`${prefix}-${index}`} href={`/${prefix}-${index}`}
    onClick={event => { event.preventDefault(); opened(index) }}>Game {index + 1}</a>)
}
function render(prefix = 'game', count = 8) {
  act(() => root.render(<GameCatalogueRail label="All games">{cards(prefix, count)}</GameCatalogueRail>))
}
function measure(width = 500, scrollWidth = 1800) {
  const viewport = host.querySelector<HTMLDivElement>('.game-catalogue-rail__viewport')!
  Object.defineProperties(viewport, { clientWidth: { value: width, configurable: true }, scrollWidth: { value: scrollWidth, configurable: true } })
  viewport.scrollTo = vi.fn((options?: ScrollToOptions | number) => {
    viewport.scrollLeft = typeof options === 'number' ? options : options?.left ?? 0
    viewport.dispatchEvent(new Event('scroll'))
  })
  viewport.setPointerCapture = vi.fn()
  viewport.hasPointerCapture = vi.fn(() => false)
  act(() => resize())
  return viewport
}
function arrows() { return [...host.querySelectorAll<HTMLButtonElement>('.game-catalogue-rail__arrow')] }
function pointer(element: Element, type: string, x: number, pointerType = 'mouse', y = 20) {
  const event = new MouseEvent(type, { bubbles: true, cancelable: true, button: 0, clientX: x, clientY: y })
  Object.defineProperties(event, { pointerId: { value: 1 }, pointerType: { value: pointerType }, isPrimary: { value: true } })
  act(() => element.dispatchEvent(event))
  return event
}
function click(element: Element, detail = 1) {
  const event = new MouseEvent('click', { bubbles: true, cancelable: true, detail })
  act(() => element.dispatchEvent(event))
}

describe('GameCatalogueRail', () => {
  it('keeps every game once in reading order and clamps arrow navigation at both ends', () => {
    render(); const viewport = measure()
    expect(host.querySelectorAll('[role="listitem"]')).toHaveLength(8)
    expect([...host.querySelectorAll('a')].map(link => link.textContent)).toEqual(Array.from({ length: 8 }, (_, i) => `Game ${i + 1}`))
    const [previous, next] = arrows()
    expect(previous.disabled).toBe(true); expect(next.disabled).toBe(false)
    click(next)
    expect(viewport.scrollLeft).toBe(425); expect(previous.disabled).toBe(false)
    for (let index = 0; index < 6; index++) click(next)
    expect(viewport.scrollLeft).toBe(1300); expect(next.disabled).toBe(true)
    for (let index = 0; index < 6; index++) click(previous)
    expect(viewport.scrollLeft).toBe(0); expect(previous.disabled).toBe(true)
    expect(host.querySelector('a')?.getAttribute('href')).toBe('/game-0')
  })

  it('supports keyboard browsing and reduced motion without wrapping', () => {
    render(); const viewport = measure()
    vi.mocked(window.matchMedia).mockReturnValue({ matches: true } as MediaQueryList)
    const key = (value: string) => act(() => viewport.dispatchEvent(new KeyboardEvent('keydown', { key: value, bubbles: true, cancelable: true })))
    key('ArrowLeft'); expect(viewport.scrollLeft).toBe(0)
    key('ArrowRight'); expect(viewport.scrollLeft).toBe(425)
    expect(viewport.scrollTo).toHaveBeenLastCalledWith({ left: 425, behavior: 'instant' })
    key('End'); expect(viewport.scrollLeft).toBe(1300)
    key('ArrowRight'); expect(viewport.scrollLeft).toBe(1300)
    key('Home'); expect(viewport.scrollLeft).toBe(0)
    expect(viewport.tabIndex).toBe(0)
    expect(viewport.getAttribute('aria-label')).toBe('All games')
    expect(arrows().every(button => button.getAttribute('aria-controls') === viewport.id)).toBe(true)
  })

  it('drags with a mouse, blocks only the resulting accidental click, and retains normal card clicks', () => {
    render(); const viewport = measure(); const first = host.querySelector('a')!
    pointer(first, 'pointerdown', 200); pointer(first, 'pointerup', 200); click(first)
    expect(opened).toHaveBeenCalledTimes(1)
    pointer(first, 'pointerdown', 200); pointer(viewport, 'pointermove', 40)
    expect(viewport.scrollLeft).toBe(160)
    expect(viewport.setPointerCapture).toHaveBeenCalledWith(1)
    pointer(viewport, 'pointerup', 40); click(first)
    expect(opened).toHaveBeenCalledTimes(1)
    pointer(first, 'pointerdown', 100); pointer(first, 'pointerup', 100); click(first)
    expect(opened).toHaveBeenCalledTimes(2)
    pointer(first, 'pointerdown', 100); pointer(viewport, 'pointermove', -2000); pointer(viewport, 'pointerup', -2000)
    expect(viewport.scrollLeft).toBe(1300)
    click(first, 0) // Keyboard activation remains usable after a drag.
    expect(opened).toHaveBeenCalledTimes(3)
  })

  it('leaves touch gestures native and never treats a small pointer wobble as a drag', () => {
    render(); const viewport = measure(); const first = host.querySelector('a')!
    pointer(first, 'pointerdown', 200, 'touch')
    expect(pointer(viewport, 'pointermove', 20, 'touch').defaultPrevented).toBe(false)
    pointer(viewport, 'pointerup', 20, 'touch')
    expect(viewport.scrollLeft).toBe(0)
    expect(viewport.setPointerCapture).not.toHaveBeenCalled()
    pointer(first, 'pointerdown', 200); pointer(viewport, 'pointermove', 197); pointer(first, 'pointerup', 197); click(first)
    expect(opened).toHaveBeenCalledOnce()
  })

  it('resets to the first game after filtering and updates arrows when the row fits after resizing', () => {
    render(); let viewport = measure()
    click(arrows()[1]); expect(viewport.scrollLeft).toBeGreaterThan(0)
    render('filtered', 3)
    expect(viewport.scrollLeft).toBe(0)
    expect(host.querySelectorAll('[role="listitem"]')).toHaveLength(3)
    viewport = measure(1000, 700)
    expect(arrows().every(button => button.disabled)).toBe(true)
    expect(viewport.scrollLeft).toBe(0)
  })
})
