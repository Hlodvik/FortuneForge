// @vitest-environment jsdom
import { act, StrictMode } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi, type Mock } from 'vitest'
import type { SnakeGateway } from './contracts'
import { LocalSnakeGateway } from './localSnakeGateway'
import { SnakeGame } from './SnakeGame'

let root: Root | null, host: HTMLDivElement, local: LocalSnakeGateway
let gateway: { [K in keyof SnakeGateway]: Mock<SnakeGateway[K]> }
function deferred() { let resolve!: () => void; const promise = new Promise<void>(accept => { resolve = accept }); return { promise, resolve } }
async function flush() { await act(async () => { for (let index = 0; index < 12; index++) await Promise.resolve() }) }
async function mount() { root = createRoot(host); await act(async () => root!.render(<StrictMode><SnakeGame gateway={gateway} /></StrictMode>)); await flush() }
const phase = () => host.querySelector('main')?.getAttribute('data-phase')
const moves = () => Number(host.querySelector('main')?.getAttribute('data-moves'))
const board = () => host.querySelector<HTMLElement>('.ff-snake-board-wrap')!
function button(name: string) {
  const found = [...host.querySelectorAll<HTMLButtonElement>('button')].find(item => (item.getAttribute('aria-label') ?? item.textContent) === name)
  if (!found) throw new Error(`Missing ${name}: ${host.textContent}`)
  return found
}
async function click(name: string) { await act(async () => { button(name).focus(); button(name).click() }); await flush() }
async function time(milliseconds: number) { await act(async () => vi.advanceTimersByTimeAsync(milliseconds)); await flush() }
async function key(value: string, target: HTMLElement = board(), repeat = false) {
  await act(async () => target.dispatchEvent(new KeyboardEvent('keydown', { key: value, repeat, bubbles: true, cancelable: true }))); await flush()
}

beforeEach(() => {
  vi.useFakeTimers(); vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  vi.spyOn(document, 'hasFocus').mockReturnValue(true)
  Object.defineProperty(document, 'hidden', { configurable: true, value: false })
  vi.stubGlobal('requestAnimationFrame', (callback: FrameRequestCallback) => window.setTimeout(() => callback(performance.now()), 16))
  vi.stubGlobal('cancelAnimationFrame', (id: number) => window.clearTimeout(id))
  localStorage.clear(); local = new LocalSnakeGateway()
  gateway = {
    getStatus: vi.fn<SnakeGateway['getStatus']>(signal => local.getStatus(signal)),
    startGame: vi.fn<SnakeGateway['startGame']>((_seed, signal, options) => local.startGame(17, signal, options)),
    reset: vi.fn<SnakeGateway['reset']>((id, _seed, signal, options) => local.reset(id, 17, signal, options)),
    turn: vi.fn<SnakeGateway['turn']>((id, direction, signal) => local.turn(id, direction, signal)),
    tick: vi.fn<SnakeGateway['tick']>((id, signal) => local.tick(id, signal)),
  }
  host = document.createElement('div'); document.body.append(host); root = null
})
afterEach(async () => {
  if (root) await act(async () => root!.unmount())
  host.remove(); vi.restoreAllMocks(); vi.clearAllTimers(); vi.useRealTimers(); vi.unstubAllGlobals()
})

describe('current Snake cabinet with serialized run intent', () => {
  it('prepares once under StrictMode and waits for Play at the unchanged cadence', async () => {
    await mount(); await time(2000)
    expect(phase()).toBe('ready'); expect(moves()).toBe(0); expect(gateway.startGame).toHaveBeenCalledTimes(1)
    await click('Play'); expect(document.activeElement).toBe(board())
    await time(114); expect(moves()).toBe(0)
    await time(1); expect(moves()).toBe(1)
  })

  it('pauses on blur and ignores steering until an explicit resume', async () => {
    await mount(); await click('Play'); await time(115)
    await act(async () => window.dispatchEvent(new Event('blur'))); await flush()
    expect(phase()).toBe('paused')
    await key('ArrowDown'); await time(1000); expect(moves()).toBe(1); expect(phase()).toBe('paused')
    await key('p'); expect(phase()).toBe('playing')
    await time(115); expect(moves()).toBe(2)
  })

  it('keeps a hidden run paused after visibility returns', async () => {
    await mount(); await click('Play')
    Object.defineProperty(document, 'hidden', { configurable: true, value: true })
    await act(async () => document.dispatchEvent(new Event('visibilitychange'))); await flush()
    await time(1000); expect(phase()).toBe('paused'); expect(moves()).toBe(0)
    Object.defineProperty(document, 'hidden', { configurable: true, value: false })
    await act(async () => document.dispatchEvent(new Event('visibilitychange'))); await flush()
    expect(phase()).toBe('paused')
  })

  it('does not capture keys from other controls or repeat pause hotkeys', async () => {
    await mount()
    const outside = document.createElement('textarea'); document.body.append(outside)
    try { outside.focus(); await key('ArrowDown', outside); expect(phase()).toBe('ready') }
    finally { outside.remove() }
    const speed = host.querySelector<HTMLSelectElement>('select')!
    speed.focus(); await key('ArrowDown', speed); expect(phase()).toBe('ready')
    board().focus(); await key('ArrowDown'); expect(phase()).toBe('playing')
    await key('p', board(), true); expect(phase()).toBe('playing')
    await key('p'); expect(phase()).toBe('paused')
  })

  it('waits for an in-flight tick before resetting and does not auto-play the new run', async () => {
    const gate = deferred()
    gateway.tick.mockImplementationOnce(async (id, signal) => { const result = await local.tick(id, signal); await gate.promise; return result })
    await mount(); await click('Play'); await time(115)
    expect(gateway.tick).toHaveBeenCalledTimes(1)
    await click('New run'); expect(phase()).toBe('loading'); expect(gateway.reset).not.toHaveBeenCalled()
    await act(async () => gate.resolve()); await flush()
    expect(gateway.reset).toHaveBeenCalledTimes(1); expect(phase()).toBe('ready'); expect(moves()).toBe(0)
    await time(1000); expect(moves()).toBe(0)
  })

  it('keeps board-size selection ready and retains the selected legal dimensions', async () => {
    await mount(); await click('Play')
    const size = host.querySelectorAll<HTMLSelectElement>('select')[1]!
    await act(async () => { size.value = '24'; size.dispatchEvent(new Event('change', { bubbles: true })) }); await flush()
    expect(phase()).toBe('ready'); expect(gateway.reset).toHaveBeenCalledWith(expect.any(String), undefined, expect.any(AbortSignal), { width: 24, height: 24 })
    await time(1000); expect(moves()).toBe(0)
  })

  it('offers a working retry after preparation fails before a game exists', async () => {
    gateway.getStatus.mockRejectedValueOnce(new Error('offline'))
    await mount(); expect(host.querySelector('[role="alert"]')).toBeTruthy()
    await click('Try again'); expect(phase()).toBe('ready'); expect(gateway.startGame).toHaveBeenCalledTimes(1)
  })

  it('treats a held gamepad pause as one press even while its direction changes', async () => {
    const buttons = Array.from({ length: 16 }, () => ({ pressed: false }))
    const pad = { buttons, axes: [0, 0] }
    Object.defineProperty(navigator, 'getGamepads', { configurable: true, value: () => [pad] })
    await mount(); await click('Play')
    buttons[9]!.pressed = true; await time(16); expect(phase()).toBe('paused')
    pad.axes[0] = 1; await time(64); expect(phase()).toBe('paused'); expect(moves()).toBe(0)
    buttons[9]!.pressed = false; await time(16); expect(phase()).toBe('paused')
    buttons[9]!.pressed = true; await time(16); expect(phase()).toBe('playing')
  })
})
