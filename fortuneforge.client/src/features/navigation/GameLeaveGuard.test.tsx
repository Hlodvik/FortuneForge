// @vitest-environment jsdom
import { act, StrictMode } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi, type Mock } from 'vitest'
import { GameLeaveGuardProvider, useGameLeaveGuard } from './GameLeaveGuard'
import { requestGameNavigation } from './gameLeaveNavigation'

let root: Root, host: HTMLDivElement
let navigate: Mock<(href: string) => void>, action: Mock<() => void>, fetch: Mock<() => void>
function Probe({ active, description = 'Your accepted round continues while you are away.' }: { active: boolean; description?: string }) {
  useGameLeaveGuard(active ? { title: 'Leave the table?', description } : null)
  return <><a href="/home">Home</a><a href="/balance" target="_blank">New tab</a>
    <button onClick={() => requestGameNavigation(action)}>Programmatic navigation</button></>
}
async function mount(active: boolean, description?: string) {
  await act(async () => root.render(<GameLeaveGuardProvider navigate={navigate}><Probe active={active} description={description} /></GameLeaveGuardProvider>))
}
async function click(label: string) {
  const button = [...document.querySelectorAll<HTMLButtonElement>('button')].find(item => item.textContent === label)!
  await act(async () => button.click())
}
function unloading() {
  const event = new Event('beforeunload', { cancelable: true }); window.dispatchEvent(event); return event.defaultPrevented
}
beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  window.history.replaceState(null, '', '/cards/house-holdem')
  vi.spyOn(window.history, 'back').mockImplementation(() => {})
  vi.spyOn(window.history, 'forward').mockImplementation(() => {})
  vi.spyOn(window.history, 'go').mockImplementation(() => {})
  navigate = vi.fn<(href: string) => void>(); action = vi.fn<() => void>(); fetch = vi.fn<() => void>(); vi.stubGlobal('fetch', fetch)
  host = document.createElement('div'); document.body.append(host); root = createRoot(host)
})
afterEach(async () => { await act(async () => root.unmount()); host.remove(); vi.restoreAllMocks(); vi.unstubAllGlobals() })

describe('accepted-round navigation presentation', () => {
  it('confirms programmatic navigation once and never calls a financial endpoint', async () => {
    await mount(true); await click('Programmatic navigation')
    expect(action).not.toHaveBeenCalled()
    const dialog = document.querySelector('[role="dialog"]')!
    expect(dialog.getAttribute('aria-modal')).toBe('true')
    expect(dialog.textContent).toContain('accepted round continues')
    expect(document.activeElement?.textContent).toBe('Stay in game')
    await click('Stay in game'); expect(action).not.toHaveBeenCalled()
    await click('Programmatic navigation'); await click('Leave page')
    expect(action).toHaveBeenCalledTimes(1)
    expect(fetch).not.toHaveBeenCalled()
    await click('Programmatic navigation')
    expect(document.querySelector('[role="dialog"]')).not.toBeNull()
  })

  it('captures an ordinary link while preserving new-tab navigation', async () => {
    await mount(true)
    await act(async () => host.querySelector<HTMLAnchorElement>('a')!.click())
    expect(navigate).not.toHaveBeenCalled()
    await click('Leave page')
    expect(new URL(navigate.mock.calls[0][0]).pathname).toBe('/home')
    await act(async () => host.querySelector<HTMLAnchorElement>('a[target="_blank"]')!.click())
    expect(document.querySelector('[role="dialog"]')).toBeNull()
    expect(fetch).not.toHaveBeenCalled()
  })

  it('traps focus, cancels with Escape and returns focus to the initiating control', async () => {
    await mount(true)
    const trigger = host.querySelector('button')!; trigger.focus()
    await click('Programmatic navigation')
    const buttons = [...document.querySelectorAll<HTMLButtonElement>('[role="dialog"] button')]
    buttons[1].focus()
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true }))
    expect(document.activeElement).toBe(buttons[0])
    await act(async () => document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true })))
    expect(document.querySelector('[role="dialog"]')).toBeNull()
    expect(document.activeElement).toBe(trigger)
    expect(action).not.toHaveBeenCalled()
  })

  it('preserves browser history through StrictMode, waiting phases and completion', async () => {
    const push = vi.spyOn(window.history, 'pushState')
    await act(async () => root.render(<StrictMode><GameLeaveGuardProvider navigate={navigate}><Probe active /></GameLeaveGuardProvider></StrictMode>))
    expect(unloading()).toBe(true)
    await act(async () => root.render(<StrictMode><GameLeaveGuardProvider navigate={navigate}><Probe active description="Other players are finishing the accepted hand." /></GameLeaveGuardProvider></StrictMode>))
    await click('Programmatic navigation')
    expect(document.querySelector('[role="dialog"]')?.textContent).toContain('Other players')
    await click('Stay in game'); await mount(false)
    expect(unloading()).toBe(false)
    expect(push).not.toHaveBeenCalled()
    expect(window.history.back).not.toHaveBeenCalled()
    expect(window.history.forward).not.toHaveBeenCalled()
    expect(window.history.go).not.toHaveBeenCalled()
    expect(fetch).not.toHaveBeenCalled()
  })

  it('does not leave an unload permission armed after a programmatic no-op', async () => {
    vi.useFakeTimers()
    try {
      await mount(true); await click('Programmatic navigation'); await click('Leave page')
      await act(async () => vi.runOnlyPendingTimers())
      expect(unloading()).toBe(true)
      expect(action).toHaveBeenCalledOnce()
    } finally { vi.useRealTimers() }
  })

  it('clears a pending prompt and all guards on completion or unmount', async () => {
    await mount(true); expect(unloading()).toBe(true)
    await click('Programmatic navigation')
    await mount(false)
    expect(document.querySelector('[role="dialog"]')).toBeNull()
    expect(unloading()).toBe(false)
    await click('Programmatic navigation'); expect(action).toHaveBeenCalledOnce()
    await mount(true); await act(async () => root.render(<p>Unmounted table</p>))
    expect(unloading()).toBe(false)
    requestGameNavigation(action); expect(action).toHaveBeenCalledTimes(2)
  })

  it('always exempts slot routes, including an accepted spin', async () => {
    window.history.replaceState(null, '', '/slots/reel-riches/demo')
    const push = vi.spyOn(window.history, 'pushState')
    await mount(true); await click('Programmatic navigation')
    expect(action).toHaveBeenCalledOnce()
    expect(push).not.toHaveBeenCalled()
    expect(unloading()).toBe(false)
    expect(document.querySelector('[role="dialog"]')).toBeNull()
  })

  it('does nothing on visibility, pagehide or background events', async () => {
    await mount(true)
    document.dispatchEvent(new Event('visibilitychange')); window.dispatchEvent(new Event('pagehide')); window.dispatchEvent(new Event('blur'))
    expect(fetch).not.toHaveBeenCalled(); expect(action).not.toHaveBeenCalled(); expect(navigate).not.toHaveBeenCalled()
    expect(document.querySelector('[role="dialog"]')).toBeNull()
    expect(unloading()).toBe(true)
  })
})
