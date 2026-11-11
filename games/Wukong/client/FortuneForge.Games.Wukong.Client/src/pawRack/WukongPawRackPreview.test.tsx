// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { isLoopbackPrototypeLocation } from './localOnly'
import { WukongPawRackPreview } from './WukongPawRackPreview'
import type { WukongPawRackPreviewProps } from './types'

let host: HTMLDivElement
let root: Root
let props: WukongPawRackPreviewProps

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  host = document.createElement('div')
  document.body.append(host)
  root = createRoot(host)
  props = {
    grants: [1, 2, 3].map(index => ({ id: `fixture-${index}`, wagerPoints: 100 })),
    wagerPoints: 100, wagerOptions: [50, 100, 200], pointValueInCents: 25,
    placement: null, phase: 'ready', pending: false, error: null, lastResult: null,
    reels: [['2', '3', '4', 'PAW_EARN_SCROLL'], ['5', '6', '7', 'PAW'], ['2', '4', '6', 'ACE'], ['3', '5', '7', '2'], ['4', '6', '2', '3']],
    observedEarnScrolls: 1, fixtureEarnSymbolInjection: true,
    earnRole: { symbolId: 'PAW_EARN_SCROLL', rate: null, threshold: null, wagerBinding: null },
    onPlace: vi.fn(), onClear: vi.fn(), onWager: vi.fn(), onSpin: vi.fn(), onNextPreview: vi.fn(),
    onResetFixture: vi.fn(), onFixtureEarnSymbolChange: vi.fn(),
  }
})

afterEach(() => { act(() => root.unmount()); host.remove(); vi.unstubAllGlobals() })

function render(overrides: Partial<WukongPawRackPreviewProps> = {}) {
  props = { ...props, ...overrides }
  act(() => root.render(<WukongPawRackPreview {...props} />))
}
function button(name: string) {
  const found = [...host.querySelectorAll<HTMLButtonElement>('button')]
    .find(item => (item.getAttribute('aria-label') ?? item.textContent?.trim()) === name)
  if (!found) throw new Error(`Missing button: ${name}`)
  return found
}
const paw = (index = 1) => button(`Select stored synthetic paw ${index} (100 points recorded)`)
const column = (number: number) => button(`Place selected synthetic paw on reel column ${number}`)
const click = (target: HTMLElement) => act(() => target.click())
function key(target: HTMLElement, value: string) {
  act(() => target.dispatchEvent(new KeyboardEvent('keydown', { key: value, bubbles: true, cancelable: true })))
}
function transfer(type = 'application/x-fortuneforge-prototype-paw', id = 'fixture-1') {
  const values = new Map([[type, id]])
  return { types: [type], effectAllowed: 'none', dropEffect: 'none', setData: (name: string, value: string) => values.set(name, value), getData: (name: string) => values.get(name) ?? '' }
}
function drag(target: HTMLElement, type: string, data: ReturnType<typeof transfer>) {
  const event = new Event(type, { bubbles: true, cancelable: true })
  Object.defineProperty(event, 'dataTransfer', { value: data })
  act(() => target.dispatchEvent(event))
}

describe('local Wukong banked paw rack', () => {
  it('labels synthetic inventory and the distinct proposed scroll without awarding anything', () => {
    render({ grants: [...props.grants, { id: 'fixture-4', wagerPoints: 100 }], observedEarnScrolls: 9 })
    expect(host.textContent).toContain('4 stored synthetic paws')
    expect(host.querySelectorAll('.wukong-paw-preview__grant')).toHaveLength(4)
    expect(host.textContent).toContain('9 earn scrolls observed')
    expect(host.textContent).toContain('earning threshold, rate and wager rule are undecided')
    expect(host.querySelector('[role="img"][aria-label="Proposed Celestial tally-scroll earn symbol"] svg')).not.toBeNull()
    expect(host.querySelector('[data-symbol="PAW"]')).not.toBeNull()
    expect(props.onPlace).not.toHaveBeenCalled()
    expect(props.onResetFixture).not.toHaveBeenCalled()
    expect(props.onSpin).not.toHaveBeenCalled()
  })

  it('supports select then tap on a full reel-column target, with one placement request and no client result editing', () => {
    render()
    const symbolsBefore = host.querySelectorAll('[data-symbol]').length
    click(paw())
    expect(paw().getAttribute('aria-pressed')).toBe('true')
    expect(column(3).disabled).toBe(false)
    expect(column(3).closest('.slot-reel')?.getAttribute('aria-label')).toBe('Reel 3')
    click(column(3))
    click(column(5))
    expect(props.onPlace).toHaveBeenCalledExactlyOnceWith('fixture-1', 2)
    expect(host.querySelectorAll('[data-symbol]')).toHaveLength(symbolsBefore)
  })

  it('shows exact wager-linked Rand token cash without the legacy production multiplier', () => {
    render({ reels: [['RAND_1', 'RAND_05', '3', 'PAW_EARN_SCROLL'], ...props.reels.slice(1)] })
    const label = (symbol: string) => host.querySelector(`[data-symbol="${symbol}"] .slot-symbol__artwork`)?.getAttribute('data-value-label')
    expect(label('RAND_1')).toBe('R25.00')
    expect(label('RAND_05')).toBe('R12.50')
    expect(label('3')).toBeNull()
    expect(host.textContent).toContain('Local in-memory fixture bank: survives page reloads, resets when the host restarts')
    render({ wagerPoints: 200 })
    expect(label('RAND_1')).toBe('R50.00')
    expect(label('RAND_05')).toBe('R25.00')
  })

  it('supports keyboard paw selection, column arrow navigation and Enter placement', () => {
    render()
    key(paw(2), 'Enter')
    column(1).focus()
    key(column(1), 'ArrowRight')
    expect(document.activeElement).toBe(column(2))
    key(column(2), 'End')
    expect(document.activeElement).toBe(column(5))
    key(column(5), ' ')
    expect(props.onPlace).toHaveBeenCalledExactlyOnceWith('fixture-2', 4)
  })

  it('accepts a stored fixture paw dragged onto a column', () => {
    render()
    const data = transfer()
    drag(paw(), 'dragstart', data)
    expect(data.effectAllowed).toBe('move')
    drag(column(4), 'dragover', data)
    expect(data.dropEffect).toBe('move')
    drag(column(4), 'drop', data)
    expect(props.onPlace).toHaveBeenCalledExactlyOnceWith('fixture-1', 3)
  })

  it('rejects external text and unknown grant drops rather than inventing inventory', () => {
    render()
    click(paw())
    drag(column(2), 'drop', transfer('text/plain', 'fixture-1'))
    drag(column(2), 'drop', transfer('application/x-fortuneforge-prototype-paw', 'not-owned'))
    expect(props.onPlace).not.toHaveBeenCalled()
    drag(column(2), 'drop', transfer())
    expect(props.onPlace).toHaveBeenCalledExactlyOnceWith('fixture-1', 1)
  })

  it('locks wager and movement after placement but permits explicit cancellation before admission', () => {
    render({ phase: 'placed', placement: { id: 'placement-1', grantId: 'fixture-1', column: 2, wagerPoints: 100 } })
    expect(host.querySelector('fieldset')?.disabled).toBe(true)
    expect(paw().disabled).toBe(true)
    expect(paw().draggable).toBe(false)
    expect(column(1).disabled).toBe(true)
    drag(column(1), 'drop', transfer())
    expect(props.onPlace).not.toHaveBeenCalled()
    click(button('Cancel placement before admission'))
    expect(props.onClear).toHaveBeenCalledOnce()
    render({ phase: 'ready', placement: null })
    expect(host.querySelector('fieldset')?.disabled).toBe(false)
    expect(props.grants).toHaveLength(3)
  })

  it('blocks cancel, reset, wager and placement throughout a pending or admitted spin', () => {
    render({ phase: 'spinning', pending: true, placement: { id: 'placement-1', grantId: 'fixture-1', column: 2, wagerPoints: 100 } })
    expect(button('Cancel placement before admission').disabled).toBe(true)
    expect(button('Reset synthetic fixture inventory').disabled).toBe(true)
    expect(button('Preview spin with placed paw').disabled).toBe(true)
    expect(host.querySelector('fieldset')?.disabled).toBe(true)
    drag(column(4), 'drop', transfer())
    click(button('Cancel placement before admission'))
    expect(props.onPlace).not.toHaveBeenCalled()
    expect(props.onClear).not.toHaveBeenCalled()
    expect(props.onResetFixture).not.toHaveBeenCalled()
    render({ phase: 'ready', placement: null, pending: true })
    expect(host.textContent).toContain('A preview request is being confirmed')
    expect(host.textContent).not.toContain('Preview admitted')
    expect(button('Preview spin · keep paws stored').disabled).toBe(true)
  })

  it('allows an unplaced preview without selecting or automatically using a stored paw', () => {
    render()
    click(button('Preview spin · keep paws stored'))
    click(button('Preview spin · keep paws stored'))
    expect(props.onSpin).toHaveBeenCalledOnce()
    expect(props.onPlace).not.toHaveBeenCalled()
    expect(props.onClear).not.toHaveBeenCalled()
    expect(host.textContent).toContain('3 stored synthetic paws')
  })

  it('shows a genuine zero result and keeps unused paws banked until explicit next setup', () => {
    render({ phase: 'settled', placement: { id: 'sealed-1', grantId: 'fixture-1', column: 2, wagerPoints: 100 },
      lastResult: { spinId: 'preview-1', pawCount: 1, moneyGrabPoints: 0, totalPoints: 0 } })
    expect(host.textContent).toContain('3 stored synthetic paws')
    expect(host.textContent).toContain('its grab can return zero')
    expect([...host.querySelectorAll('dd')].map(item => item.textContent)).toEqual(['1', '0 points', '0 points'])
    expect(column(5).disabled).toBe(true)
    expect(button('Cancel placement before admission').disabled).toBe(true)
    drag(column(5), 'drop', transfer())
    click(button('Set up next preview'))
    expect(props.onNextPreview).toHaveBeenCalledOnce()
    expect(props.onPlace).not.toHaveBeenCalled()
    expect(props.onResetFixture).not.toHaveBeenCalled()
    render({ phase: 'ready', placement: null })
    expect(host.textContent).toContain('Last sealed preview result')
    expect(host.textContent).toContain('3 stored synthetic paws')
  })

  it('delegates recorded-wager eligibility to the host instead of choosing exact or lower-wager rules', () => {
    render()
    click(button('50 points'))
    expect(props.onWager).toHaveBeenCalledExactlyOnceWith(50)
    render({ wagerPoints: 50 })
    click(paw())
    click(column(1))
    expect(props.onPlace).toHaveBeenCalledExactlyOnceWith('fixture-1', 0)
    render({ error: 'This proposed eligibility rule is not selected.' })
    expect(host.querySelector('[role="alert"]')?.textContent).toContain('not selected')
    expect(props.grants).toHaveLength(3)
  })

  it('keeps a zero bank usable for unplaced preview and resets only on explicit request', () => {
    render({ grants: [] })
    expect(host.textContent).toContain('0 stored synthetic paws')
    expect(button('Preview spin · keep paws stored').disabled).toBe(false)
    expect(props.onResetFixture).not.toHaveBeenCalled()
    click(button('Reset synthetic fixture inventory'))
    expect(props.onResetFixture).toHaveBeenCalledOnce()
  })

  it('changes only the illustrative-scroll callback and never awards from its observed count', () => {
    render({ observedEarnScrolls: 100 })
    const checkbox = host.querySelector<HTMLInputElement>('input[type="checkbox"]')!
    click(checkbox)
    expect(props.onFixtureEarnSymbolChange).toHaveBeenCalledExactlyOnceWith(false)
    expect(props.onResetFixture).not.toHaveBeenCalled()
    expect(props.onPlace).not.toHaveBeenCalled()
    expect(host.textContent).toContain('3 stored synthetic paws')
    render({ phase: 'placed', placement: { id: 'placement-1', grantId: 'fixture-1', column: 0, wagerPoints: 100 } })
    expect(checkbox.disabled).toBe(true)
  })

  it('fails closed for malformed reel or wager state', () => {
    render({ reels: props.reels.slice(0, 4), wagerPoints: Number.NaN })
    expect(host.textContent).toContain('No spin can be submitted')
    expect(button('Preview spin · keep paws stored').disabled).toBe(true)
    expect(paw().disabled).toBe(true)
    expect(props.onSpin).not.toHaveBeenCalled()
  })
})

describe('standalone preview entry boundary', () => {
  it.each(['127.0.0.1', 'localhost', '[::1]'])('permits only HTTP loopback host %s', hostname => {
    expect(isLoopbackPrototypeLocation({ hostname, protocol: 'http:' })).toBe(true)
  })
  it.each(['fortuneforgegame.web.app', 'localhost.evil.test', '127.0.0.1.evil.test', ''])('rejects non-loopback host %s', hostname => {
    expect(isLoopbackPrototypeLocation({ hostname, protocol: 'https:' })).toBe(false)
  })
  it('does not permit a file or other protocol even with a loopback-looking hostname', () => {
    expect(isLoopbackPrototypeLocation({ hostname: 'localhost', protocol: 'file:' })).toBe(false)
  })
})
