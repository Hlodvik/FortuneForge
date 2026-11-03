// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { PaymentCheckout } from '../features/payments/services/paymentsApi'
import { PaymentAlertsMenu } from './PaymentAlertsMenu'

const api = vi.hoisted(() => ({ list: vi.fn() }))
vi.mock('../features/payments/services/paymentsApi', () => ({ listPaymentInvoices: api.list }))

let host: HTMLDivElement, root: Root
let anchor: DOMRect
const originalWidth = Object.getOwnPropertyDescriptor(window, 'innerWidth')!
const originalHeight = Object.getOwnPropertyDescriptor(window, 'innerHeight')!
const invoice = { checkoutId: 'order-1', invoiceId: 'invoice /1', status: 'processing',
  amountMinor: 12000, currency: 'ZAR', locale: 'en-ZA', createdAtUtc: '2026-11-02T00:00:00Z' } as PaymentCheckout

function viewport(width: number, height: number) {
  Object.defineProperty(window, 'innerWidth', { configurable: true, value: width })
  Object.defineProperty(window, 'innerHeight', { configurable: true, value: height })
}

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  api.list.mockReset().mockResolvedValue([invoice])
  viewport(1280, 720)
  anchor = new DOMRect(1200, 16, 40, 40)
  // jsdom has no layout. Supply the trigger and a long panel's measured box;
  // actual CSS clipping, scrolling and hit targets remain browser-QA checks.
  vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function (this: HTMLElement) {
    if (this.tagName === 'SUMMARY') return anchor
    if (this.classList.contains('payment-alerts__panel')) return new DOMRect(
      Number.parseFloat(this.style.left) || 0, Number.parseFloat(this.style.top) || 0,
      Number.parseFloat(this.style.width) || 368, Math.min(480, Number.parseFloat(this.style.maxHeight) || 480))
    return new DOMRect()
  })
  host = document.createElement('div'); document.body.append(host); root = createRoot(host)
})

afterEach(() => {
  act(() => root.unmount()); host.remove()
  Object.defineProperty(window, 'innerWidth', originalWidth)
  Object.defineProperty(window, 'innerHeight', originalHeight)
  vi.restoreAllMocks(); vi.unstubAllGlobals()
})

async function open(accountTheme = false) {
  if (accountTheme) host.className = 'home-lobby'
  await act(async () => root.render(<PaymentAlertsMenu />))
  const details = host.querySelector('details')!
  await act(async () => { details.open = true; details.dispatchEvent(new Event('toggle')) })
  return { details, trigger: host.querySelector('summary')!, panel: document.querySelector<HTMLElement>('.payment-alerts__panel')! }
}

describe('payment alerts viewport placement', () => {
  it.each([[390, 320], [900, 320], [1280, 720]])('keeps a bottom-toolbar popup above its trigger at %sx%s', async (width, height) => {
    viewport(width, height); anchor = new DOMRect(width - 56, height - 56, 40, 40)
    const { panel } = await open()
    const bounds = panel.getBoundingClientRect()
    expect(panel.parentElement).toBe(document.body)
    expect(host.contains(panel)).toBe(false)
    expect(bounds.left).toBeGreaterThanOrEqual(16)
    expect(bounds.right).toBeLessThanOrEqual(width - 16)
    expect(bounds.top).toBeGreaterThanOrEqual(16)
    expect(bounds.bottom).toBeLessThanOrEqual(anchor.top)
    expect(bounds.bottom).toBeLessThanOrEqual(height - 16)
  })

  it.each([[390, 320], [900, 320], [1280, 720]])('keeps a navbar popup below its trigger at %sx%s', async (width, height) => {
    viewport(width, height); anchor = new DOMRect(20, 16, 40, 40)
    const { panel } = await open(true)
    const bounds = panel.getBoundingClientRect()
    expect(bounds.top).toBeGreaterThanOrEqual(anchor.bottom)
    expect(bounds.left).toBeGreaterThanOrEqual(16)
    expect(bounds.right).toBeLessThanOrEqual(width - 16)
    expect(bounds.bottom).toBeLessThanOrEqual(height - 16)
    expect(panel.classList.contains('payment-alerts__panel--account')).toBe(true)
    expect(panel.querySelector('a')?.getAttribute('href')).toBe('/home/invoices')
    expect(panel.querySelector('li a')?.getAttribute('href')).toBe('/home/invoices/invoice%20%2F1')
    expect(api.list).toHaveBeenCalledTimes(1)
    expect(api.list).toHaveBeenCalledWith(8)
  })

  it('reanchors on resize, preserves keyboard access and restores the bell on Escape', async () => {
    const { trigger, panel, details } = await open()
    viewport(390, 320); anchor = new DOMRect(330, 264, 40, 40)
    await act(async () => window.dispatchEvent(new Event('resize')))
    expect(panel.getBoundingClientRect().right).toBeLessThanOrEqual(374)
    expect(panel.getBoundingClientRect().bottom).toBeLessThanOrEqual(264)
    trigger.focus()
    await act(async () => trigger.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true })))
    expect(document.activeElement).toBe(panel.querySelector('a'))
    await act(async () => document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true })))
    expect(details.open).toBe(false)
    expect(document.querySelector('.payment-alerts__panel')).toBeNull()
    expect(document.activeElement).toBe(trigger)
    expect(api.list).toHaveBeenCalledTimes(1)
    expect(api.list).toHaveBeenCalledWith(8)
  })

  it('dismisses outside without moving focus or fetching orders again', async () => {
    const { details } = await open()
    const outside = document.createElement('button'); document.body.append(outside); outside.focus()
    await act(async () => outside.dispatchEvent(new MouseEvent('pointerdown', { bubbles: true })))
    expect(details.open).toBe(false)
    expect(document.querySelector('.payment-alerts__panel')).toBeNull()
    expect(document.activeElement).toBe(outside)
    expect(api.list).toHaveBeenCalledTimes(1)
    expect(api.list).toHaveBeenCalledWith(8)
    outside.remove()
  })
})
