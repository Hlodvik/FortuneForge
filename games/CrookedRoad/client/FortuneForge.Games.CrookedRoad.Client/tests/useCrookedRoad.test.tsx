// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useCrookedRoad, type CrookedRoadController } from '../src/useCrookedRoad'
import { CrookedRoadRequestError } from '../src/api'
import type { CrookedRoadGameProps, CrookedRoadState, CrookedRoadSpinResult } from '../src/types'
import type { RoadBonus } from '../src/road'

const mocks = vi.hoisted(() => ({ getState: vi.fn(), spin: vi.fn(), resumeSpin: vi.fn(), action: vi.fn(), pending: vi.fn(), noop: vi.fn() }))
vi.mock('../src/api', async original => ({
  ...await original<typeof import('../src/api')>(), createCrookedRoadApi: () => ({ ...mocks }),
}))
vi.mock('@fortuneforge/slot-client-core', async original => ({
  ...await original<typeof import('@fortuneforge/slot-client-core')>(),
  usePrefersReducedMotion: () => true,
  useSlotAudio: () => ({ preferences: { mode: 'all', volume: 65 }, playCue: mocks.noop,
    startLoop: mocks.noop, stopLoop: mocks.noop, stopResultCues: mocks.noop, toggleMuted: mocks.noop }),
  startSpinAnimation: () => ({}), cancelSpinAnimation: mocks.noop, finishSpinAnimation: mocks.noop,
  requestQuickStop: mocks.noop,
  stopSpinAnimationWithCadence: async ({ snapToTarget }: { snapToTarget: () => void }) => snapToTarget(),
}))

function snapshot(revision = 0, bonus: RoadBonus | null = null, balance = 100): CrookedRoadState {
  return { gameFeatureState: { contractVersion: 1, revision, board: Array(9).fill(null), hand: [], queuedTiles: [],
    reserveTileCounts: { straight: 0, corner: 0, junction: 0 }, tickets: [], reachableDestinations: [], lastTravel: null,
    funding: { spins: 0, requiredSpins: 20, wagerPointsTotal: 0 }, activeBonus: bonus, completedJourneys: bonus ? 0 : 1 },
    freeSpinsRemaining: bonus ? 3 - bonus.step : 0, freeSpinWagerPoints: bonus?.fundedWagerPoints ?? null,
    freeSpinFeatureMode: bonus?.kind ?? null, featureActionRequired: bonus?.choice === null,
    slotsCreditsBalance: balance, pendingOperation: null }
}
function well(step = 0, choice: number | null = null): RoadBonus {
  return { id: 'well-ticket', kind: 'well', step, totalSteps: 3, awardPoints: 0, fundedWagerPoints: 300,
    choice, board: step ? Array.from({ length: 5 }, () => ['2', '3', '4']) : null,
    held: null, respinsRemaining: 0, doors: [] }
}
function result(next = snapshot(1, null, 97), free = false): CrookedRoadSpinResult {
  return { ...next, spinId: '00000000-0000-4000-8000-000000000001', gameId: 'crooked-road-v1',
    symbolSetId: 'crooked-road-v1-symbols', reelSetId: 'crooked-road-independent-reels-v1', paytableId: 'crooked-road-math-v1',
    wagerPoints: 300, pointValueInCents: 1, reelStops: [0, 0, 0, 0, 0],
    reels: Array.from({ length: 5 }, () => ['2', '3', '4'] as const), payout: { totalPoints: 0, paylines: [] },
    isFreeSpin: free, chargedWagerCents: free ? 0 : 300, spinFeature: null }
}
const player = (userId = 'local-player') => ({ userId, role: 'player', balances: { slotsCredits: 100 } })
let host: HTMLDivElement
let root: Root
let c: CrookedRoadController
function Harness(props: CrookedRoadGameProps) { c = useCrookedRoad(props); return null }
async function mount(account = player(), authenticatedFetch?: CrookedRoadGameProps['authenticatedFetch']) {
  await act(async () => root.render(<Harness account={account} authenticatedFetch={authenticatedFetch} />))
}
beforeEach(() => {
  vi.useFakeTimers()
  vi.clearAllMocks()
  mocks.pending.mockReset().mockReturnValue(undefined)
  mocks.getState.mockReset().mockResolvedValue(snapshot())
  mocks.spin.mockReset().mockResolvedValue(result())
  mocks.resumeSpin.mockReset().mockResolvedValue(result())
  mocks.action.mockReset().mockResolvedValue(snapshot(2))
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  host = document.createElement('div'); document.body.append(host); root = createRoot(host)
})
afterEach(() => { act(() => root.unmount()); host.remove(); vi.useRealTimers(); vi.unstubAllGlobals() })

describe('owned paid input and recovery lifecycle', () => {
  it('shows an invalid saved-operation recovery error and admits no new paid intent', async () => {
    mocks.pending.mockImplementation(() => { throw new Error('The saved play requires account recovery.') })
    await mount()
    expect(c.spinError).toContain('requires account recovery')
    expect(c.spinDisabled).toBe(true)
    await act(async () => c.handleSpinButtonClick())
    expect(mocks.spin).not.toHaveBeenCalled()
  })
  it('stops auto and excludes button and keyboard spin while the server requires a spring choice', async () => {
    mocks.spin.mockResolvedValueOnce(result(snapshot(1, well(), 97)))
    await mount()
    await act(async () => { c.setIsAutoSpinning(true); await vi.advanceTimersByTimeAsync(1000) })
    await act(async () => { await vi.advanceTimersByTimeAsync(1000) })
    expect(mocks.spin).toHaveBeenCalledTimes(1)
    expect(c.isFeatureActionRequired).toBe(true)
    expect(c.isAutoSpinning).toBe(false)
    await act(async () => { c.handleSpinButtonClick(); c.setIsAutoSpinning(true)
      window.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', cancelable: true }))
      await vi.advanceTimersByTimeAsync(5000) })
    expect(mocks.spin).toHaveBeenCalledTimes(1)
    mocks.action.mockResolvedValueOnce(snapshot(2, well(0, 1), 97))
    await act(async () => c.performGameFeatureAction({ action: 'choose-spring', choice: 1, expectedRevision: 1 }))
    expect(c.isFeatureActionRequired).toBe(false)
    mocks.spin.mockResolvedValueOnce(result(snapshot(3, well(1, 1), 97), true))
    await act(async () => c.handleSpinButtonClick())
    expect(mocks.spin).toHaveBeenLastCalledWith(300, true)
  })

  it('blocks a spin and duplicate action synchronously until an authoritative action reply arrives', async () => {
    let finish!: (next: CrookedRoadState) => void
    mocks.action.mockImplementationOnce(() => new Promise<CrookedRoadState>(resolve => { finish = resolve }))
    await mount()
    let work!: Promise<void>
    act(() => { work = c.performGameFeatureAction({ action: 'remove', cellIndex: 0, expectedRevision: 0 })
      void c.performGameFeatureAction({ action: 'remove', cellIndex: 0, expectedRevision: 0 }); c.handleSpinButtonClick() })
    expect(mocks.action).toHaveBeenCalledTimes(1)
    expect(mocks.spin).not.toHaveBeenCalled()
    expect(c.state?.gameFeatureState.revision).toBe(0)
    await act(async () => { finish(snapshot(1)); await work })
    expect(c.state?.gameFeatureState.revision).toBe(1)
  })

  it('refreshes a rejected stale revision before the player retries a road action', async () => {
    await mount()
    mocks.action.mockRejectedValueOnce(new CrookedRoadRequestError('Refresh the road.', 409))
    mocks.getState.mockResolvedValueOnce(snapshot(9))
    await act(async () => { await expect(c.performGameFeatureAction({ action: 'remove', cellIndex: 0, expectedRevision: 0 })).rejects.toThrow('Refresh') })
    expect(c.state?.gameFeatureState.revision).toBe(9)
    mocks.action.mockResolvedValueOnce(snapshot(10))
    await act(async () => c.performGameFeatureAction({ action: 'remove', cellIndex: 0, expectedRevision: 9 }))
    expect(mocks.action).toHaveBeenLastCalledWith({ action: 'remove', cellIndex: 0, expectedRevision: 9 })
    expect(c.state?.gameFeatureState.revision).toBe(10)
  })

  it.each(['account', 'transport'] as const)('discards old action replies after the active %s changes', async change => {
    let finish!: (next: CrookedRoadState) => void
    mocks.action.mockImplementationOnce(() => new Promise<CrookedRoadState>(resolve => { finish = resolve }))
    await mount()
    let work!: Promise<void>
    act(() => { work = c.performGameFeatureAction({ action: 'remove', cellIndex: 0, expectedRevision: 0 }) })
    mocks.getState.mockResolvedValueOnce(snapshot(7, null, 23))
    await mount(player(change === 'account' ? 'replacement-player' : 'local-player'), change === 'transport' ? vi.fn() : undefined)
    await act(async () => { finish(snapshot(999, null, 999)); await work })
    expect(c.state?.gameFeatureState.revision).toBe(7)
    expect(c.balance).toBe(23)
    expect(c.isGameFeatureUpdating).toBe(false)
  })

  it('stops before another cash spin and holds the completed journey for local review', async () => {
    mocks.getState.mockResolvedValueOnce(snapshot(4, well(2, 1)))
    mocks.spin.mockResolvedValueOnce({ ...result(snapshot(5, null), true),
      spinFeature: { kind: 'well', completed: true, bonus: well(3, 1) } })
    await mount()
    await act(async () => c.setIsAutoSpinning(true))
    await act(async () => { await vi.advanceTimersByTimeAsync(1000) })
    expect(c.isAutoSpinning).toBe(false)
    expect(c.isFeaturePresenting).toBe(true)
    await act(async () => { c.handleSpinButtonClick(); await vi.advanceTimersByTimeAsync(5000) })
    expect(mocks.spin).toHaveBeenCalledTimes(1)
    await act(async () => c.setFeaturePresenting(false))
    await act(async () => { await vi.advanceTimersByTimeAsync(5000) })
    expect(mocks.spin).toHaveBeenCalledTimes(1)
  })

  it('retries the saved paid operation instead of admitting a new wager after a lost reply', async () => {
    mocks.spin.mockImplementationOnce(async () => {
      mocks.pending.mockReturnValue({ operationId: 'pending-operation-001', wagerPoints: 300, useFreeSpin: false, useSpecialBoost: false })
      throw new Error('Reply lost')
    })
    await mount()
    await act(async () => c.handleSpinButtonClick())
    expect(c.spinError).toBe('Reply lost')
    await act(async () => c.handleSpinButtonClick())
    expect(mocks.resumeSpin).toHaveBeenCalledTimes(1)
    expect(mocks.spin).toHaveBeenCalledTimes(1)
  })
})
