// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { PharaohAscentGameProps } from '../src/types'
import { jsonReply, slotState, spinResult, tombRun, tombState } from './fixtures'

const audio = vi.hoisted(() => ({ preferences: { mode: 'muted', volume: 0 }, playCue: vi.fn(), playSequence: vi.fn(),
  startLoop: vi.fn(), stopLoop: vi.fn(), stopResultCues: vi.fn(), toggleMuted: vi.fn() }))
vi.mock('@fortuneforge/slot-client-core', async importActual => {
  const actual = await importActual<typeof import('@fortuneforge/slot-client-core')>()
  return { ...actual, useSlotAudio: () => audio, usePrefersReducedMotion: () => true,
    startSpinAnimation: () => ({}), finishSpinAnimation: vi.fn(), cancelSpinAnimation: vi.fn(), requestQuickStop: vi.fn(),
    stopSpinAnimationWithCadence: async (options: { snapToTarget: () => void }) => options.snapToTarget() }
})
import { usePharaohAscent, type PharaohAscentController } from '../src/usePharaohAscent'

let root: Root
let host: HTMLDivElement
let controller: PharaohAscentController
let props: PharaohAscentGameProps
let identity = 0
const user = () => ({ userId: `pharaoh-controller-${++identity}`, role: 'Player', balances: { slotsCredits: 100 } })
function Harness({ input }: { input: PharaohAscentGameProps }) { controller = usePharaohAscent(input); return null }
async function render() { await act(async () => { root.render(<Harness input={props} />) }) }

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  sessionStorage.clear()
  host = document.createElement('div')
  document.body.append(host)
  root = createRoot(host)
  props = { account: user(), authenticatedFetch: vi.fn(async () => jsonReply(slotState())) }
})
afterEach(async () => { await act(async () => root.unmount()); host.remove(); vi.clearAllMocks(); vi.unstubAllGlobals() })

describe('standalone Pharaoh controller', () => {
  it('starts at R3, respects both picker edges, and ignores out-of-range selections', async () => {
    await render()
    expect(controller.activeWager).toBe(3)
    expect(controller.wagerPoints).toBe(12)
    expect(controller.wagerOptions).toHaveLength(150)
    await act(async () => controller.selectWager(0))
    expect(controller.activeWager).toBe(.5)
    await act(async () => controller.selectWager(controller.wagerOptions.length - 1))
    expect(controller.activeWager).toBe(75)
    await act(async () => { controller.selectWager(controller.wagerOptions.length); controller.selectWager(-1) })
    expect(controller.activeWager).toBe(75)
  })

  it('keeps an existing funded reel wager locked even when it exceeds the new paid maximum', async () => {
    const old = slotState()
    const free = { ...old, freeSpinsRemaining: 1, freeSpinWagerPoints: 1501,
      gameFeatureState: { ...old.gameFeatureState, freeSpinsRemaining: 1, freeSpinWagerPoints: 1501 } }
    props = { ...props, authenticatedFetch: vi.fn(async () => jsonReply(free)) }
    await render()
    expect(controller.wagerOptions.at(-1)).toBe(300)
    expect(controller.wagerPoints).toBe(1501)
    expect(controller.activeWager).toBe(375.25)
    expect(controller.isWagerLocked).toBe(true)
    await act(async () => controller.selectWager(0))
    expect(controller.wagerPoints).toBe(1501)
  })

  it('restores confirmed account state and never replaces a newer account with an old response', async () => {
    let finishOld!: (response: Response) => void
    props = { ...props, authenticatedFetch: vi.fn(async () => new Promise<Response>(resolve => { finishOld = resolve })) }
    await render()
    expect(controller.spinAvailability).toBe('checking')
    props = { account: user(), authenticatedFetch: vi.fn(async () => jsonReply({ ...slotState(), slotsCreditsBalance: 250 })) }
    await render()
    expect(controller.balance).toBe(250)
    await act(async () => finishOld(jsonReply({ ...slotState(10), slotsCreditsBalance: 999 })))
    expect(controller.balance).toBe(250)
    expect(controller.state?.gameFeatureState.tomb.revision).toBe(0)
  })

  it('admits one paid spin for repeated clicks and displays only the server result', async () => {
    let finishSpin!: (response: Response) => void
    const fetch = vi.fn(async (path: RequestInfo | URL) => String(path).includes('/state?') ? jsonReply(slotState())
      : new Promise<Response>(resolve => { finishSpin = resolve }))
    props = { ...props, authenticatedFetch: fetch }
    await render()
    expect(controller.spinAvailability).toBe('available')
    await act(async () => { controller.handleSpinButtonClick(); controller.handleSpinButtonClick() })
    expect(fetch.mock.calls.filter(([path]) => String(path).includes('prepared-spins'))).toHaveLength(1)
    expect(controller.hasCompletedSpin).toBe(false)
    const result = { ...spinResult(), slotsCreditsBalance: 97 }
    await act(async () => finishSpin(jsonReply(result)))
    expect(controller.hasCompletedSpin).toBe(true)
    expect(controller.displayedReels).toEqual(result.reels)
    expect(controller.balance).toBe(97)
  })

  it('blocks reel and auto play while expedition choices are required', async () => {
    const active = slotState(4, { ...tombState(4), activeRun: tombRun() })
    const fetch = vi.fn(async () => jsonReply(active))
    props = { ...props, authenticatedFetch: fetch }
    await render()
    await act(async () => { controller.handleSpinButtonClick(); controller.setIsAutoSpinning(true) })
    expect(fetch).toHaveBeenCalledTimes(1)
    expect(controller.spinDisabled).toBe(true)
    expect(controller.isAutoSpinning).toBe(false)
  })

  it('discards a late settled spin after the authenticated account changes', async () => {
    let finishSpin!: (response: Response) => void
    const firstFetch = vi.fn(async (path: RequestInfo | URL) => String(path).includes('/state?') ? jsonReply(slotState())
      : new Promise<Response>(resolve => { finishSpin = resolve }))
    props = { ...props, authenticatedFetch: firstFetch }
    await render()
    await act(async () => controller.handleSpinButtonClick())
    props = { account: user(), authenticatedFetch: vi.fn(async () => jsonReply({ ...slotState(), slotsCreditsBalance: 22 })) }
    await render()
    expect(controller.balance).toBe(22)
    await act(async () => finishSpin(jsonReply({ ...spinResult(8), slotsCreditsBalance: 999 })))
    expect(controller.balance).toBe(22)
    expect(controller.hasCompletedSpin).toBe(false)
    expect(controller.state?.gameFeatureState.tomb.revision).toBe(0)
  })

  it('serializes expedition actions and waits for confirmed room progress', async () => {
    let finishAction!: (response: Response) => void
    const active = slotState(4, { ...tombState(4), activeRun: tombRun() })
    const fetch = vi.fn(async (path: RequestInfo | URL) => String(path).includes('/state?') ? jsonReply(active)
      : new Promise<Response>(resolve => { finishAction = resolve }))
    props = { ...props, authenticatedFetch: fetch }
    await render()
    const intent = { action: 'open-room', expectedRevision: 4, choiceId: tombRun().choices[2].id } as const
    let first!: Promise<void>
    await act(async () => { first = controller.performGameFeatureAction(intent); void controller.performGameFeatureAction(intent) })
    expect(fetch.mock.calls.filter(([path]) => String(path).includes('/actions'))).toHaveLength(1)
    expect(controller.state?.gameFeatureState.tomb.activeRun?.keys).toBe(0)
    const next = slotState(5, { ...tombState(5), activeRun: { ...tombRun(), keys: 1, torchesRemaining: 5,
      roomPoints: 12, bankedPoints: 72, path: [{ step: 1, kind: 'key', relic: null, awardPoints: 12 }] } })
    await act(async () => { finishAction(jsonReply(next)); await first })
    expect(controller.state?.gameFeatureState.tomb.activeRun?.keys).toBe(1)
    expect(controller.isGameFeatureUpdating).toBe(false)
  })

  it('keeps a lost paid reply recoverable with its original key and amount', async () => {
    const posts: RequestInit[] = []
    let fail = true
    const fetch = vi.fn(async (path: RequestInfo | URL, init?: RequestInit) => {
      if (String(path).includes('/state?')) return jsonReply(slotState())
      posts.push(init!)
      if (fail) throw new TypeError('Lost paid reply')
      return jsonReply(spinResult())
    })
    props = { ...props, authenticatedFetch: fetch }
    await render()
    await act(async () => { controller.handleSpinButtonClick() })
    expect(controller.spinError).toBe('Lost paid reply')
    expect(controller.isWagerLocked).toBe(true)
    fail = false
    await act(async () => { controller.selectWager(50); controller.handleSpinButtonClick() })
    expect(posts[1].body).toBe(posts[0].body)
    expect(new Headers(posts[1].headers).get('Idempotency-Key')).toBe(new Headers(posts[0].headers).get('Idempotency-Key'))
    expect(controller.isWagerLocked).toBe(false)
  })
})
