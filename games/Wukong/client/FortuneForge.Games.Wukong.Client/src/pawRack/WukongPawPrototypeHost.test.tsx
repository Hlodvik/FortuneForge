// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import WukongPawPrototypeHost from './WukongPawPrototypeHost'
import type { WukongPawRackPreviewProps } from './types'

vi.mock('./localOnly', () => ({ isLoopbackPrototypeLocation: () => true }))
vi.mock('./WukongPawRackPreview', () => ({
  WukongPawRackPreview: (props: WukongPawRackPreviewProps) => <div>
    <span data-testid="bank">{props.grants.length}</span>
    <span data-testid="phase">{props.phase}</span>
    <button type="button" disabled={props.pending} onClick={props.onSpin}>Preview spin</button>
  </div>,
}))

const initial = {
  sessionId: 'fixture-session', mode: 'synthetic-local', gameId: 'wukong-journey-v1',
  liveEarningEnabled: false, liveConsumptionEnabled: false,
  grants: [1, 2, 3].map(index => ({ id: `fixture-${index}`, wagerPoints: 100 })),
  wagerPoints: 100, wagerOptions: [50, 100, 200], pointValueInCents: 25,
  placement: null, phase: 'ready', reels: Array.from({ length: 5 }, () => ['2', '3', '4', '5']),
  lastResult: null, observedEarnScrolls: 0, fixtureEarnSymbolInjection: false,
  earnRole: { symbolId: 'PAW_EARN_SCROLL', rate: null, threshold: null, wagerBinding: null },
}
const response = (body: unknown, ok = true) => ({ ok, json: async () => body })
let container: HTMLDivElement
let root: Root
let fetchMock: ReturnType<typeof vi.fn>

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  vi.stubGlobal('crypto', { randomUUID: () => 'fixed-request-key' })
  fetchMock = vi.fn()
  vi.stubGlobal('fetch', fetchMock)
  container = document.createElement('div')
  document.body.append(container)
  root = createRoot(container)
})
afterEach(() => { act(() => root.unmount()); container.remove(); vi.unstubAllGlobals() })
async function flush(action: () => void) {
  await act(async () => { action(); for (let i = 0; i < 10; i++) await Promise.resolve() })
}
function button(name: string) {
  const match = [...container.querySelectorAll<HTMLButtonElement>('button')].find(item => item.textContent === name)
  if (!match) throw new Error(`Missing ${name}`)
  return match
}

it('retries the identical admitted command after a lost response and blocks another spin', async () => {
  fetchMock.mockResolvedValueOnce(response(initial))
    .mockRejectedValueOnce(new TypeError('lost response'))
    .mockResolvedValueOnce(response({ ...initial, grants: initial.grants.slice(1), phase: 'settled',
      lastResult: { spinId: 'same-server-receipt', pawCount: 1, moneyGrabPoints: 0, totalPoints: 0 } }))
  await flush(() => root.render(<WukongPawPrototypeHost />))
  await flush(() => button('Preview spin').click())
  expect(button('Preview spin').disabled).toBe(true)
  await flush(() => button('Preview spin').click())
  expect(fetchMock).toHaveBeenCalledTimes(2)
  await flush(() => button('Retry the same request').click())
  expect(fetchMock).toHaveBeenCalledTimes(3)
  expect(fetchMock.mock.calls[2][0]).toBe('/__prototype/wukong-paws/spin')
  expect(fetchMock.mock.calls[2][1].body).toBe(fetchMock.mock.calls[1][1].body)
  expect(JSON.parse(fetchMock.mock.calls[2][1].body).requestId).toBe('fixed-request-key')
  expect(container.querySelector('[data-testid="bank"]')?.textContent).toBe('2')
  expect(container.querySelector('[data-testid="phase"]')?.textContent).toBe('settled')
})

it('refuses a response that enables a live economy instead of the synthetic fixture', async () => {
  fetchMock.mockResolvedValueOnce(response({ ...initial, liveConsumptionEnabled: true }))
  await flush(() => root.render(<WukongPawPrototypeHost />))
  expect(container.textContent).toContain('disabled synthetic Wukong fixture')
  expect(container.querySelector('[data-testid="bank"]')).toBeNull()
  expect(fetchMock).toHaveBeenCalledTimes(1)
})
