// @vitest-environment jsdom
import { act } from 'react'
import { createRoot } from 'react-dom/client'
import { expect, it, vi } from 'vitest'
import { GODS_OF_OLYMPUS_SLOT_GAME } from '../../games/slots/godsOfOlympus/manifest'
import { GODS_TUMBLE_INITIAL_REELS } from '../../games/slots/godsOfOlympus/tumble'
import { NEON_NIGHTS_SLOT_GAME } from '../../games/slots/neonNights/manifest'
import { SpinRequestError } from './services/slotsApi'
import type { SpinResult } from './types/slots'
import { useSlotsPageController, type SlotsPageController } from './useSlotsPageController'

const mocks = vi.hoisted(() => ({ request: vi.fn(), state: vi.fn(), noop: vi.fn() }))
vi.mock('./services/slotsApi', async (importOriginal) => ({
  ...await importOriginal<typeof import('./services/slotsApi')>(),
  requestSpin: mocks.request,
  requestSlotState: mocks.state,
}))
vi.mock('./hooks/useSlotAudio', () => ({ useSlotAudio: () => ({
  preferences: { volume: 50, mode: 'all' }, startLoop: mocks.noop, stopLoop: mocks.noop,
  playCue: mocks.noop, playSequence: mocks.noop, stopResultCues: mocks.noop,
  setVolume: mocks.noop, toggleMuted: mocks.noop, toggleResultsOnly: mocks.noop,
}) }))
vi.mock('./hooks/usePrefersReducedMotion', () => ({ usePrefersReducedMotion: () => true }))
vi.mock('./presentation/spinLifecycle', async (importOriginal) => ({
  ...await importOriginal<typeof import('./presentation/spinLifecycle')>(),
  waitForPresentationFrame: vi.fn(async () => true),
}))

const experience = GODS_OF_OLYMPUS_SLOT_GAME.experience
const wagerPoints = experience.rules.wagerOptions[0]

function response(reels: SpinResult['reels'], isFreeSpin: boolean, balance: number): SpinResult {
  return {
    spinId: `olympus-${isFreeSpin}`, gameId: experience.rules.gameId,
    reelSetId: 'olympus', symbolSetId: experience.symbols.serverSymbolSetId ?? experience.symbols.id,
    paytableId: 'olympus', wagerPoints, pointValueInCents: experience.rules.pointValueInCents,
    reelStops: reels.map(() => 0), reels, isFreeSpin,
    consecutiveFiveMisses: 0, fiveMatchPityTriggered: false,
    freeSpinsAwarded: 0, freeSpinsRemaining: 0, freeSpinWagerPoints: null,
    specialPointsAwarded: 0, specialPointsBalance: 0, energyAwarded: 0, energyBalance: 0,
    energyMultiplierApplied: false, payoutMultiplier: 1, monkeyPawCount: 0,
    moneyGrabPoints: 0, bananaBonusPoints: 0, sealsAwarded: {}, sealCollections: [],
    freeSpinFeatureMode: null, specialBoostApplied: false, slotsCreditsBalance: balance,
    payout: { totalPoints: 0, paylines: [] },
  }
}

it('plays an owed extra-row Trial before switching to paid tumbles and settles the server balance', async () => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  const root = createRoot(document.createElement('div'))
  let controller!: SlotsPageController
  function Harness() {
    controller = useSlotsPageController({ experienceSet: experience })
    return null
  }

  const legacyReels: SpinResult['reels'] = experience.rules.initialReels.map((reel) => [...reel, '2', '3'])
  mocks.state.mockResolvedValueOnce({
    freeSpinsRemaining: 1, freeSpinWagerPoints: wagerPoints, freeSpinFeatureMode: 'rows',
    specialPointsBalance: 0, energyBalance: 0, sealCollections: [],
  })
  mocks.request.mockResolvedValueOnce(response(legacyReels, true, 42.75))
    .mockResolvedValueOnce({
      ...response(GODS_TUMBLE_INITIAL_REELS, false, 39.25),
      olympusTumble: { initialReels: GODS_TUMBLE_INITIAL_REELS, steps: [], totalPoints: 0 },
    })

  try {
    await act(async () => root.render(<Harness />))
    await act(async () => controller.handleSpinButtonClick())
    expect(mocks.request.mock.calls[0][0]).toMatchObject({ wagerPoints, useFreeSpin: true })
    expect(controller.displayedReels).toEqual(legacyReels)
    expect(controller.balance).toBe(42.75)

    await act(async () => controller.handleSpinButtonClick())
    expect(mocks.request.mock.calls[1][0]).toEqual({
      gameId: experience.rules.gameId, wagerPoints, useFreeSpin: false, useSpecialBoost: false,
    })
    expect(controller.displayedReels).toEqual(GODS_TUMBLE_INITIAL_REELS)
    expect(controller.balance).toBe(39.25)
  } finally {
    act(() => root.unmount())
    vi.unstubAllGlobals()
  }
})

it('recovers paid Neon Special Plays and the original wager after a stale cash spin', async () => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  vi.clearAllMocks()
  const root = createRoot(document.createElement('div'))
  const experienceSet = NEON_NIGHTS_SLOT_GAME.experience
  const account = {
    userId: 'paid-neon-test', playerName: 'Neon Test', email: 'neon@example.test', createdAtUtc: '', role: 'player',
    balances: { slotsCredits: 20, freeGames: 0 },
    slots: { spinsPlayed: 0, wins: 0, losses: 0, creditsWagered: 0, creditsWon: 0, netCredits: 0 },
  }
  let controller!: SlotsPageController
  function Harness() {
    controller = useSlotsPageController({ experienceSet, account })
    return null
  }

  const staleState = {
    freeSpinsRemaining: 0, freeSpinWagerPoints: null, freeSpinFeatureMode: null,
    specialPointsBalance: 0, energyBalance: 0, sealCollections: [],
  }
  mocks.state.mockResolvedValueOnce(staleState).mockResolvedValueOnce({
    ...staleState, freeSpinsRemaining: 7, freeSpinWagerPoints: 150,
  })
  mocks.request.mockRejectedValueOnce(new SpinRequestError('Play your awarded spins first.', 409, { code: 'free-spins-required' }))

  try {
    await act(async () => root.render(<Harness />))
    await act(async () => controller.handleSpinButtonClick())
    expect(mocks.request).toHaveBeenCalledOnce()
    expect(mocks.request).toHaveBeenCalledWith(expect.objectContaining({ useFreeSpin: false }))
    expect(mocks.state.mock.calls).toEqual([['neon-nights-v1'], ['neon-nights-v1']])
    expect(controller.freeSpinsRemaining).toBe(7)
    expect(controller.useFreeGameForNextSpin).toBe(true)
    expect(controller.activeWagerDisplay).toBe(1.5)
    expect(controller.balance).toBe(20)
    expect(controller.spinError).toBeNull()
  } finally {
    act(() => root.unmount())
    vi.unstubAllGlobals()
  }
})
