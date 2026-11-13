import { act } from 'react'
import { createRoot } from 'react-dom/client'
import { afterEach, expect, it, vi } from 'vitest'
import { useNeonNights, type NeonNightsController } from '../src/useNeonNights'
import { NeonRequestError } from '../src/api'
import { neonBaseMusic, neonCarMusicCueId } from '../src/audio'
import { emptyNeonState } from '../src/roundState'
import { NEON_CAR_SYMBOL_SET_ID, NEON_DISCO_SYMBOL_SET_ID, NEON_GAME_ID, type NeonCarRespinOffer, type NeonCarRespinResult, type NeonNitroChoice, type NeonSpinResult } from '../src/types'

const mocks = vi.hoisted(() => ({ state: vi.fn(), spin: vi.fn(), respin: vi.fn(), chooseNitro: vi.fn(), dismissRespin: vi.fn(), trigger: vi.fn(),
  availability: vi.fn(), startSyncedLoop: vi.fn(), stopLoop: vi.fn(), noop: vi.fn() }))
vi.mock('../src/api.js', async importOriginal => ({ ...await importOriginal<typeof import('../src/api')>(),
  createNeonApi: () => ({ state: mocks.state, spin: mocks.spin, respin: mocks.respin, chooseNitro: mocks.chooseNitro, dismissRespin: mocks.dismissRespin, trigger: mocks.trigger, availability: mocks.availability }) }))
vi.mock('@fortuneforge/slot-client-core', async importOriginal => ({ ...await importOriginal<typeof import('@fortuneforge/slot-client-core')>(),
  usePrefersReducedMotion: () => true,
  useSlotAudio: () => ({ preferences: { mode: 'all', volume: 50 }, getLoopPlaybackSeconds: mocks.noop,
    preloadLoop: mocks.noop, startLoop: mocks.noop, startSyncedLoop: mocks.startSyncedLoop,
    stopLoop: mocks.stopLoop, playCue: mocks.noop, stopResultCues: mocks.noop,
    setVolume: mocks.noop, toggleMuted: mocks.noop, toggleResultsOnly: mocks.noop }),
}))
const account = { userId: 'neon-test', role: 'admin', balances: { slotsCredits: 20 } }
afterEach(() => { vi.useRealTimers(); vi.unstubAllGlobals(); vi.clearAllMocks() })

it('adds the drum layer after four completed base spins', async () => {
  vi.useFakeTimers()
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  const visibility = vi.spyOn(document, 'hidden', 'get').mockReturnValue(false)
  const root = createRoot(document.createElement('div'))
  let c!: NeonNightsController
  function Harness() { c = useNeonNights({ account }); return null }
  const result: NeonSpinResult = {
    ...emptyNeonState, spinId: 'base-result', gameId: NEON_GAME_ID, symbolSetId: 'neon-arcade-symbols-v3',
    wagerPoints: 300, pointValueInCents: 1, reels: Array.from({ length: 5 }, () => ['2', '4', '5', '7']),
    isFreeSpin: false, freeSpinsAwarded: 0, slotsCreditsBalance: null, payout: { totalPoints: 0, paylines: [] },
    bananaBonusPoints: 0, neonArcade: { featureGamesEnabled: true, bonusSymbolCount: 0, activators: [] },
  }
  mocks.state.mockResolvedValueOnce(emptyNeonState)
  mocks.spin.mockRejectedValueOnce(new Error('Connection dropped'))
  for (let spin = 0; spin < 4; spin++) mocks.spin.mockResolvedValueOnce(result)
  try {
    await act(async () => root.render(<Harness />))
    for (let attempt = 0; attempt < 4; attempt++) {
      await act(async () => { c.handleSpinButtonClick(); await vi.runAllTimersAsync() })
      expect(mocks.startSyncedLoop).not.toHaveBeenCalled()
    }
    await act(async () => { c.handleSpinButtonClick(); await vi.runAllTimersAsync() })
    expect(mocks.startSyncedLoop).toHaveBeenCalledExactlyOnceWith(neonBaseMusic.drumCueId, neonBaseMusic.cueId, neonBaseMusic.barSeconds)
  } finally {
    act(() => root.unmount())
    visibility.mockRestore()
  }
})

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
    expect(mocks.spin).toHaveBeenCalledWith(expect.objectContaining({ wagerPoints: 300, useFreeSpin: false }), false)
    expect(mocks.state).toHaveBeenCalledTimes(2)
    expect(c.state.freeSpinsRemaining).toBe(7)
    expect(c.activeWager).toBe(1.5)
    expect(c.balance).toBe(20)
    expect(c.spinError).toBeNull()
  } finally { act(() => root.unmount()) }
})
it('Car Plays expand the cabinet and wait for a manual spin', async () => {
  vi.useFakeTimers()
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  const visibility = vi.spyOn(document, 'hidden', 'get').mockReturnValue(false)
  const root = createRoot(document.createElement('div'))
  let c!: NeonNightsController
  function Harness() { c = useNeonNights({ account }); return null }
  mocks.state.mockResolvedValueOnce(emptyNeonState)
  mocks.trigger.mockResolvedValueOnce({ ...emptyNeonState, freeSpinsRemaining: 7, freeSpinWagerPoints: 50, freeSpinFeatureMode: 'car-rows' })
  try {
    await act(async () => root.render(<Harness />))
    await act(async () => c.triggerAdmin('6'))
    expect(c.specialIntro?.symbol).toBe('6')
    expect(c.carCabinetExpanded).toBe(false)
    expect(mocks.noop).not.toHaveBeenCalledWith('arcade-expansion')
    act(() => { c.finishSpecialIntro('6') })
    expect(c.arcadeExpanding).toBe(true)
    expect(c.carCabinetExpanded).toBe(true)
    expect(c.displayedReels.every(reel => reel.length === 7)).toBe(true)
    expect(mocks.noop).toHaveBeenCalledWith(neonCarMusicCueId)
    expect(mocks.noop).toHaveBeenCalledWith('arcade-expansion')
    act(() => { vi.advanceTimersByTime(3499) })
    expect(c.arcadeExpanding).toBe(true)
    act(() => { vi.advanceTimersByTime(1) })
    expect(c.arcadeExpanding).toBe(false)
    expect(c.specialActive).toBe(true)
    act(() => { vi.advanceTimersByTime(6000) })
    expect(c.state.freeSpinsRemaining).toBe(7)
    expect(mocks.spin).not.toHaveBeenCalled()
  } finally { act(() => root.unmount()); visibility.mockRestore() }
})

const finishedCarOffer: NeonCarRespinOffer = {
  spinId: 'car-respin-test', eligibleReels: [1, 3], roundWinPoints: 350,
  reels: Array.from({ length: 5 }, () => ['2', '4', '5', '7', '2', '4', '5']),
}

it('keeps the Car cabinet and soundtrack active while the final respin settles its Nitro choice', async () => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  const visibility = vi.spyOn(document, 'hidden', 'get').mockReturnValue(false)
  const root = createRoot(document.createElement('div'))
  const activeRenders: boolean[] = []
  let c!: NeonNightsController
  function Harness() { c = useNeonNights({ account }); activeRenders.push(c.specialActive); return null }
  const initialReels = finishedCarOffer.reels.map(reel => [...reel])
  initialReels[3][2] = 'NITRO'
  initialReels[4][1] = 'NITRO'
  const nitro = { trigger: { reel: 3, row: 2 }, secondNitro: { reel: 4, row: 1 }, eligibleReel: 4, initialReels }
  const choice: NeonNitroChoice = {
    sourceKind: 'car-respin', spinId: finishedCarOffer.spinId, reelIndex: 4, roundWinPoints: 350,
    wagerPoints: 50, freeSpinsRemaining: 0, initialReels, eligibleReels: [0, 1, 2, 3, 4],
    neonNitroWild: { ...nitro, expandedReel: null, requiresChoice: true },
  }
  const reels = initialReels.map((reel, index) => index === 1 ? reel.map(() => 'ACE' as const) : reel)
  const result: NeonCarRespinResult = {
    sourceSpinId: choice.spinId, reelIndex: choice.reelIndex, stopIndex: 13, initialReels, reels,
    neonNitroWild: { ...nitro, expandedReel: 1 }, originalPayoutPoints: 0, improvementPoints: 65,
    payout: { totalPoints: 65, paylines: [] }, slotsCreditsBalance: 20.65, alreadyApplied: false,
  }
  mocks.state.mockResolvedValueOnce({ ...emptyNeonState, pendingNeonNitroChoice: choice })
  mocks.chooseNitro.mockResolvedValueOnce(result)
  try {
    await act(async () => root.render(<Harness />))
    expect(c.carCabinetExpanded).toBe(true)
    activeRenders.length = 0
    mocks.stopLoop.mockClear()
    mocks.noop.mockClear()
    await act(async () => c.chooseNitroReel(1))
    expect(activeRenders).not.toContain(false)
    expect(mocks.stopLoop).not.toHaveBeenCalledWith(neonCarMusicCueId)
    expect(mocks.noop).not.toHaveBeenCalledWith(neonCarMusicCueId)
    expect(c.roundSummary?.totalRand).toBe(4.15)
    await act(async () => c.dismissRoundSummary())
    expect(c.specialActive).toBe(false)
    expect(mocks.stopLoop).toHaveBeenCalledWith(neonCarMusicCueId)
  } finally {
    act(() => root.unmount())
    visibility.mockRestore()
  }
})

it.each(['landed', 'reloaded'] as const)('holds a %s final Car spin until the required Nitro reel is settled', async mode => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  const root = createRoot(document.createElement('div'))
  let c!: NeonNightsController
  function Harness() { c = useNeonNights({ account }); return null }
  const initialReels = finishedCarOffer.reels.map(reel => [...reel])
  initialReels[3][2] = 'NITRO'
  initialReels[4][1] = 'NITRO'
  const evidence = { trigger: { reel: 3, row: 2 }, secondNitro: { reel: 4, row: 1 }, eligibleReel: 4, initialReels }
  const choice: NeonNitroChoice = {
    sourceKind: 'spin', spinId: 'nitro-final-spin', wagerPoints: 50, freeSpinsRemaining: 0,
    initialReels, eligibleReels: [0, 1, 2, 3, 4], neonNitroWild: { ...evidence, expandedReel: null, requiresChoice: true },
  }
  const reels = initialReels.map((reel, index) => index === 1 ? reel.map(() => 'ACE' as const) : reel)
  const settled: NeonSpinResult = {
    ...emptyNeonState, spinId: choice.spinId, gameId: NEON_GAME_ID, symbolSetId: NEON_CAR_SYMBOL_SET_ID,
    wagerPoints: 50, pointValueInCents: 1, reels, isFreeSpin: true, freeSpinsAwarded: 0,
    slotsCreditsBalance: 22, payout: { totalPoints: 250, paylines: [] }, bananaBonusPoints: 0,
    neonCarRoundWinPoints: 900, neonArcade: { featureGamesEnabled: true, bonusSymbolCount: 0, activators: [] },
    neonNitroWild: { ...evidence, expandedReel: 1 },
  }
  const starting = { ...emptyNeonState, freeSpinsRemaining: 1, freeSpinWagerPoints: 50, freeSpinFeatureMode: 'car-rows' }
  mocks.state.mockResolvedValueOnce(mode === 'landed' ? starting : { ...starting, pendingNeonNitroChoice: choice })
  if (mode === 'landed') mocks.spin.mockResolvedValueOnce({ pendingNeonNitroChoice: choice })
  mocks.chooseNitro.mockRejectedValueOnce(new TypeError('Connection dropped')).mockResolvedValueOnce(settled)
  try {
    await act(async () => root.render(<Harness />))
    if (mode === 'landed') await act(async () => c.handleSpinButtonClick())
    expect(c.nitroChoice).toEqual(choice)
    expect(c.displayedReels).toEqual(initialReels)
    expect(c.balance).toBe(20)
    expect(c.roundSummary).toBeNull()
    await act(async () => c.handleSpinButtonClick())
    expect(mocks.spin).toHaveBeenCalledTimes(mode === 'landed' ? 1 : 0)
    await act(async () => c.chooseNitroReel(1))
    expect(c.nitroChoiceReels).toEqual([1])
    await act(async () => c.chooseNitroReel(2))
    expect(mocks.chooseNitro).toHaveBeenCalledTimes(1)
    await act(async () => c.chooseNitroReel(1))
    expect(mocks.chooseNitro.mock.calls).toEqual([[choice, 1], [choice, 1]])
    expect(c.nitroChoice).toBeNull()
    expect(c.displayedReels).toEqual(reels)
    expect(c.balance).toBe(22)
    expect(c.roundSummary).toEqual({ totalRand: 9, mode: 'car-rows' })
  } finally { act(() => root.unmount()) }
})

it('restores a finished Car round and retries only the same free respin without another wager', async () => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  const root = createRoot(document.createElement('div'))
  let c!: NeonNightsController
  function Harness() { c = useNeonNights({ account }); return null }
  const offer = finishedCarOffer
  const changedReels = offer.reels.map((reel, index) => index === 1 ? [...reel.slice(0, 6), 'ACE' as const] : reel)
  const result: NeonCarRespinResult = {
    sourceSpinId: offer.spinId, reelIndex: 1, stopIndex: 13, initialReels: changedReels, reels: changedReels,
    payout: { totalPoints: 100, paylines: [] }, originalPayoutPoints: 35, improvementPoints: 65,
    slotsCreditsBalance: 20.65, alreadyApplied: true,
  }
  mocks.state.mockResolvedValueOnce({ ...emptyNeonState, neonCarRespinOffer: offer })
    .mockResolvedValueOnce({ ...emptyNeonState, neonCarRespinOffer: offer })
  mocks.respin.mockRejectedValueOnce(new TypeError('Connection dropped')).mockResolvedValueOnce(result)
  try {
    await act(async () => root.render(<Harness />))
    expect(c.roundSummary).toEqual({ totalRand: 3.5, mode: 'car-rows' })
    expect(c.displayedReels).toEqual(offer.reels)
    expect(c.respinSelecting).toBe(true)
    await act(async () => c.chooseRespinReel(0))
    expect(mocks.respin).not.toHaveBeenCalled()
    await act(async () => c.chooseRespinReel(1))
    expect(c.respinReels).toEqual([1])
    expect(c.balance).toBe(20)
    await act(async () => c.chooseRespinReel(3))
    expect(mocks.respin).toHaveBeenCalledTimes(1)
    await act(async () => c.chooseRespinReel(1))
    expect(mocks.respin.mock.calls).toEqual([[offer, 1], [offer, 1]])
    expect(c.balance).toBe(20.65)
    expect(c.roundSummary?.totalRand).toBe(4.15)
    expect(c.displayedReels).toEqual(changedReels)
    expect(c.canChooseRespin).toBe(false)
    expect(mocks.spin).not.toHaveBeenCalled()
    await act(async () => c.dismissRoundSummary())
    expect(mocks.dismissRespin).not.toHaveBeenCalled()
    expect(c.roundSummary).toBeNull()
  } finally { act(() => root.unmount()) }
})

it('offers only Continue on an all-locked board and dismisses its finished-round offer', async () => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  const root = createRoot(document.createElement('div'))
  let c!: NeonNightsController
  function Harness() { c = useNeonNights({ account }); return null }
  const offer = { ...finishedCarOffer, eligibleReels: [] }
  mocks.state.mockResolvedValueOnce({ ...emptyNeonState, neonCarRespinOffer: offer })
  try {
    await act(async () => root.render(<Harness />))
    expect(c.canChooseRespin).toBe(false)
    await act(async () => c.dismissRoundSummary())
    expect(mocks.dismissRespin).toHaveBeenCalledWith(offer.spinId)
    expect(c.roundSummary).toBeNull()
  } finally { act(() => root.unmount()) }
})

it('retains the server round total after reloading a Car round whose free respin was used', async () => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  const root = createRoot(document.createElement('div'))
  let c!: NeonNightsController
  function Harness() { c = useNeonNights({ account }); return null }
  mocks.state.mockResolvedValueOnce({ ...emptyNeonState, freeSpinsRemaining: 1, freeSpinWagerPoints: 50,
    freeSpinFeatureMode: 'car-rows', neonCarRoundWinPoints: 650 })
  const result: NeonSpinResult = {
    ...emptyNeonState, spinId: 'last-car-spin', gameId: NEON_GAME_ID, symbolSetId: NEON_CAR_SYMBOL_SET_ID,
    wagerPoints: 50, pointValueInCents: 1, reels: finishedCarOffer.reels, isFreeSpin: true, freeSpinsAwarded: 0,
    slotsCreditsBalance: 20, payout: { totalPoints: 50, paylines: [] }, bananaBonusPoints: 0,
    neonArcade: { featureGamesEnabled: true, bonusSymbolCount: 0, activators: [] },
  }
  mocks.spin.mockResolvedValueOnce(result)
  try {
    await act(async () => root.render(<Harness />))
    expect(c.roundSummary).toBeNull()
    await act(async () => c.handleSpinButtonClick())
    expect(c.roundSummary).toEqual({ totalRand: 7, mode: 'car-rows' })
    expect(c.carCabinetExpanded).toBe(true)
    await act(async () => c.dismissRoundSummary())
    expect(c.carCabinetExpanded).toBe(false)
  } finally { act(() => root.unmount()) }
})

it('restores the Disco subtotal and uses the settled total on the final spin', async () => {
  vi.useFakeTimers()
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  const root = createRoot(document.createElement('div'))
  let c!: NeonNightsController
  function Harness() { c = useNeonNights({ account }); return null }
  mocks.state.mockResolvedValueOnce({ ...emptyNeonState, neonDiscoSpinsRemaining: 1, neonDiscoRoundWinPoints: 650 })
  const reels = Array.from({ length: 5 }, () => ['2', '4', '5', '7'] as const)
    .map(reel => [...reel])
  const result: NeonSpinResult = {
    ...emptyNeonState, spinId: 'last-disco-spin', gameId: NEON_GAME_ID, symbolSetId: NEON_DISCO_SYMBOL_SET_ID,
    wagerPoints: 300, pointValueInCents: 1, reels, isFreeSpin: false, freeSpinsAwarded: 0,
    slotsCreditsBalance: 17, payout: { totalPoints: 0, paylines: [] }, bananaBonusPoints: 0,
    neonArcade: { featureGamesEnabled: true, bonusSymbolCount: 0, activators: [] },
    neonDiscoCascade: { initialReels: reels, steps: [], totalPoints: 0 },
    neonDiscoRoundWinPoints: 650,
  }
  mocks.spin.mockResolvedValueOnce(result)
  try {
    await act(async () => root.render(<Harness />))
    expect(c.discoActive).toBe(true)
    expect(c.roundWinnings).toBe(6.5)
    await act(async () => { c.handleSpinButtonClick(); await vi.runAllTimersAsync() })
    expect(c.roundSummary).toEqual({ totalRand: 6.5, mode: 'neon-disco' })
    expect(c.roundWinnings).toBe(6.5)
    await act(async () => c.dismissRoundSummary())
    expect(c.roundWinnings).toBe(0)
  } finally { act(() => root.unmount()) }
})
