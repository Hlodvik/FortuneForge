import { act } from 'react'
import { createRoot } from 'react-dom/client'
import { afterEach, expect, it, vi } from 'vitest'
import { useNeonNights, type NeonNightsController } from '../src/useNeonNights'
import { NeonRequestError } from '../src/api'
import { emptyNeonState } from '../src/roundState'

const mocks = vi.hoisted(() => ({ state: vi.fn(), spin: vi.fn(), trigger: vi.fn(), availability: vi.fn(), noop: vi.fn() }))
vi.mock('../src/api.js', async importOriginal => ({ ...await importOriginal<typeof import('../src/api')>(),
  createNeonApi: () => ({ state: mocks.state, spin: mocks.spin, trigger: mocks.trigger, availability: mocks.availability }) }))
vi.mock('@fortuneforge/slot-client-core', async importOriginal => ({ ...await importOriginal<typeof import('@fortuneforge/slot-client-core')>(),
  usePrefersReducedMotion: () => true,
  useSlotAudio: () => ({ preferences: { mode: 'all', volume: 50 }, getLoopPlaybackSeconds: mocks.noop,
    startLoop: mocks.noop, stopLoop: mocks.noop, playCue: mocks.noop, stopResultCues: mocks.noop,
    setVolume: mocks.noop, toggleMuted: mocks.noop, toggleResultsOnly: mocks.noop }),
}))
const account = { userId: 'neon-test', role: 'admin', balances: { slotsCredits: 20 } }
afterEach(() => { vi.useRealTimers(); vi.unstubAllGlobals(); vi.clearAllMocks() })
it('recovers the authoritative locked wager after an awarded-round conflict', async () => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  const root = createRoot(document.createElement('div'))
  let c!: NeonNightsController
  function Harness() { c = useNeonNights({ account }); return null }
  mocks.state.mockResolvedValueOnce(emptyNeonState).mockResolvedValueOnce({ ...emptyNeonState, freeSpinsRemaining: 7, freeSpinWagerPoints: 150 })
  mocks.spin.mockRejectedValueOnce(new NeonRequestError('Play your awarded spins first.', 409, { code: 'free-spins-required' }))
  try {
    await act(async () => root.render(<Harness />))
    await act(async () => c.handleSpinButtonClick())
    expect(mocks.spin).toHaveBeenCalledWith(expect.objectContaining({ wagerPoints: 50, useFreeSpin: false }), false)
    expect(mocks.state).toHaveBeenCalledTimes(2)
    expect(c.state.freeSpinsRemaining).toBe(7)
    expect(c.activeWager).toBe(1.5)
    expect(c.balance).toBe(20)
    expect(c.spinError).toBeNull()
  } finally { act(() => root.unmount()) }
})
it('the Arcade intro expands only its own cabinet before any next spin', async () => {
  vi.useFakeTimers()
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  const root = createRoot(document.createElement('div'))
  let c!: NeonNightsController
  function Harness() { c = useNeonNights({ account }); return null }
  mocks.state.mockResolvedValueOnce(emptyNeonState)
  mocks.trigger.mockResolvedValueOnce({ ...emptyNeonState, freeSpinsRemaining: 7, freeSpinWagerPoints: 50, freeSpinFeatureMode: 'arcade-rows' })
  try {
    await act(async () => root.render(<Harness />))
    await act(async () => c.triggerAdmin('SEAL_RAND'))
    expect(c.specialIntro?.symbol).toBe('SEAL_RAND')
    act(() => { c.setIsHelpOpen(true); c.finishSpecialIntro('SEAL_RAND') })
    expect(c.arcadeExpanding).toBe(true)
    expect(c.displayedReels.every(reel => reel.length === 7)).toBe(true)
    expect(mocks.noop).toHaveBeenCalledWith('arcade-expansion')
    act(() => { vi.advanceTimersByTime(3499) })
    expect(c.arcadeExpanding).toBe(true)
    act(() => { vi.advanceTimersByTime(1) })
    expect(c.arcadeExpanding).toBe(false)
    expect(c.specialActive).toBe(true)
    expect(mocks.spin).not.toHaveBeenCalled()
  } finally { act(() => root.unmount()) }
})
