// @vitest-environment jsdom
import { readFileSync } from 'node:fs'
import { URL as NodeURL } from 'node:url'
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ARCANE_ARCHIVES_EXPERIENCE_SET } from '@fortuneforge/games-arcane-archives/manifest'
import { OCEAN_ODYSSEY_SLOT_GAME } from '@fortuneforge/games-ocean-odyssey/manifest'
import { FIFTH_SUN_SLOT_GAME } from '@fortuneforge/games-robot-revolution/manifest'
import { WUKONG_EXPERIENCE_SET } from '@fortuneforge/games-wukong/manifest'
import { createPreparedSlotSpinStore } from '@fortuneforge/slot-client-core/preparedSpin'
import { SpinButton } from '@fortuneforge/slot-client-core/components/SpinButton'
import type { SlotTomeState } from '@fortuneforge/slot-game-runtime/experience'
import { DRAGON_HOARD_SLOT_GAME } from '../../games/slots/dragonHoard/manifest'
import type { SlotExperienceSet } from './config/slotExperienceSets'
import { CLASSIC_SLOT_SYMBOL_SET_ID } from './config/symbolSets'
import { useSlotsPageController, type SlotsPageController, type SlotsPageProps } from './useSlotsPageController'
import { getPresentedFreeSpinsAwarded } from './presentation/freeSpinAwards'
import type { SpinResult } from './types/slots'

const mocks = vi.hoisted(() => ({
  request: vi.fn(), startLoop: vi.fn(), stopLoop: vi.fn(), playCue: vi.fn(),
  paidRequest: vi.fn(), slotState: vi.fn(), selectTome: vi.fn(), usePotion: vi.fn(),
  playSequence: vi.fn(), stopResultCues: vi.fn(), noop: vi.fn(),
  preferences: { volume: 50, mode: 'all' as const },
}))
vi.mock('./services/slotsApi', async importOriginal => ({
  ...await importOriginal<typeof import('./services/slotsApi')>(),
  requestDemoAvailability: vi.fn(async () => undefined), requestDemoSpin: mocks.request,
  requestSpin: mocks.paidRequest, requestSlotState: mocks.slotState, requestArcaneTomePage: mocks.selectTome,
  requestArcanePotion: mocks.usePotion,
}))
vi.mock('./hooks/useSlotAudio', () => ({ useSlotAudio: () => ({
  preferences: mocks.preferences, startLoop: mocks.startLoop, stopLoop: mocks.stopLoop,
  playCue: mocks.playCue, playSequence: mocks.playSequence, stopResultCues: mocks.stopResultCues,
  setVolume: mocks.noop, toggleMuted: mocks.noop, toggleResultsOnly: mocks.noop,
}) }))
vi.mock('./hooks/usePrefersReducedMotion', () => ({ usePrefersReducedMotion: () => true }))
vi.mock('./animation/slotAnimation', () => ({
  startSpinAnimation: vi.fn(() => ({})),
  stopSpinAnimationWithCadence: vi.fn(async ({ snapToTarget }: { snapToTarget: () => void }) => snapToTarget()),
  finishSpinAnimation: vi.fn(), cancelSpinAnimation: vi.fn(), requestQuickStop: vi.fn(), reelLandingSettleDurationMs: 0,
}))
vi.mock('./presentation/spinLifecycle', async importOriginal => ({
  ...await importOriginal<typeof import('./presentation/spinLifecycle')>(),
  waitForPresentation: vi.fn(async () => true), waitForPresentationFrame: vi.fn(async () => true),
}))

const fixture = JSON.parse(readFileSync(new NodeURL('./__fixtures__/controller-dialog-engine.json', import.meta.url), 'utf8')) as {
  collection: { response: SpinResult }[]; baseLoss: { response: SpinResult }
}
const withCurrentSymbolSet = (response: SpinResult): SpinResult =>
  ({ ...response, symbolSetId: CLASSIC_SLOT_SYMBOL_SET_ID })
let host: HTMLDivElement
let root: Root
let controller: SlotsPageController
function Harness({ experience, account, withSpinControl = false }: {
  experience: SlotExperienceSet; account?: SlotsPageProps['account']; withSpinControl?: boolean
}) {
  controller = useSlotsPageController({ demoMode: account === undefined, account, experienceSet: experience })
  return withSpinControl ? <SpinButton onSpin={controller.handleSpinButtonClick} isSpinning={controller.isSpinning} /> : <div />
}
async function mount(experience = OCEAN_ODYSSEY_SLOT_GAME.experience, account?: SlotsPageProps['account']) {
  await act(async () => root.render(<Harness experience={experience} account={account} />))
}
async function spin() {
  await act(async () => { controller.handleSpinButtonClick() })
}

beforeEach(() => {
  vi.useFakeTimers()
  vi.clearAllMocks()
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  host = document.createElement('div')
  document.body.append(host)
  root = createRoot(host)
  mocks.request.mockResolvedValue(withCurrentSymbolSet(fixture.baseLoss.response))
})
afterEach(() => {
  act(() => root.unmount())
  host.remove()
  vi.useRealTimers()
  vi.unstubAllGlobals()
})

it('keeps wager and spin shortcuts after a mouse-focused Spin button', async () => {
  await act(async () => root.render(<Harness experience={OCEAN_ODYSSEY_SLOT_GAME.experience} withSpinControl />))
  const button = host.querySelector<HTMLButtonElement>('.spin-button')!
  button.focus()
  await act(async () => button.click())
  await act(async () => { await vi.advanceTimersByTimeAsync(5_000) })
  expect(mocks.request).toHaveBeenCalledTimes(1)
  expect(document.activeElement).toBe(button)

  const before = controller.wagerIndex
  const arrow = new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true, cancelable: true })
  await act(async () => { button.dispatchEvent(arrow) })
  expect(arrow.defaultPrevented).toBe(true)
  expect(controller.wagerIndex).toBe(before + 1)

  const space = new KeyboardEvent('keydown', { key: ' ', bubbles: true, cancelable: true })
  await act(async () => { button.dispatchEvent(space) })
  await act(async () => { await vi.advanceTimersByTimeAsync(5_000) })
  expect(space.defaultPrevented).toBe(true)
  expect(mocks.request).toHaveBeenCalledTimes(2)
})

it('stops Arcane autoplay and blocks manual and keyboard spins until the Tome boost is activated', async () => {
  const experience = ARCANE_ARCHIVES_EXPERIENCE_SET
  const initial: SlotTomeState = {
    selectedPage: 'herbalism', activePage: null, boostedSpinsRemaining: 0, grimoireReady: false,
    ingredients: {}, pages: [{ id: 'herbalism', label: 'Herbalism', ingredients: ['MANDRAKE_ROOT', 'NIGHTSHADE_BERRIES'] }],
  }
  const active = { ...initial, activePage: 'herbalism', boostedSpinsRemaining: 5 }
  mocks.slotState.mockResolvedValueOnce({
    arcaneTome: initial, freeSpinsRemaining: 0, freeSpinWagerPoints: null, freeSpinFeatureMode: null,
    specialPointsBalance: 0, energyBalance: 0, sealCollections: [],
  })
  const result: SpinResult = {
    ...fixture.baseLoss.response, gameId: experience.rules.gameId,
    symbolSetId: experience.symbols.serverSymbolSetId ?? experience.symbols.id,
    reels: experience.rules.initialReels.map(reel => [...reel]) as SpinResult['reels'], slotsCreditsBalance: 97,
  }
  mocks.paidRequest.mockResolvedValueOnce({ ...result, arcaneTome: { ...initial, grimoireReady: true } })
    .mockResolvedValueOnce({ ...result, arcaneTome: { ...active, boostedSpinsRemaining: 4 } })
  mocks.selectTome.mockResolvedValueOnce(active)
  await mount(experience, {
    userId: 'test-user', playerName: 'Player', email: 'player@example.test', createdAtUtc: '2026-11-20T00:00:00Z',
    balances: { slotsCredits: 100, freeGames: 0 }, role: 'player',
    slots: { spinsPlayed: 0, wins: 0, losses: 0, creditsWagered: 0, creditsWon: 0, netCredits: 0 },
  })

  await act(async () => controller.setIsAutoSpinning(true))
  await act(async () => { await vi.advanceTimersByTimeAsync(100) })
  expect(mocks.paidRequest).toHaveBeenCalledTimes(1)
  expect(controller.isTomeActivationRequired).toBe(true)
  expect(controller.isAutoSpinning).toBe(false)

  await spin()
  act(() => window.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', bubbles: true, cancelable: true })))
  await act(async () => { await vi.advanceTimersByTimeAsync(5_000) })
  expect(mocks.paidRequest).toHaveBeenCalledTimes(1)

  await act(async () => { await controller.changeTomePage('herbalism', true) })
  expect(mocks.selectTome).toHaveBeenCalledWith('herbalism', true)
  expect(controller.isTomeActivationRequired).toBe(false)
  await spin()
  expect(mocks.paidRequest).toHaveBeenCalledTimes(2)
  expect(controller.tomeState?.boostedSpinsRemaining).toBe(4)
})

it('uses the earned potion wager for all three paid spins and restores the selected wager', async () => {
  const experience = ARCANE_ARCHIVES_EXPERIENCE_SET
  const bottle = { id: 1, ingredientId: 'MANDRAKE_ROOT', name: 'Mandrake Tonic', effect: 'Boost', potency: 0, lockedWagerPoints: 2 }
  const potions = { capacity: 8, majority: 5, pot: [], bottles: [bottle], preview: null, active: null }
  const arcaneTome: SlotTomeState = {
    selectedPage: 'herbalism', activePage: null, boostedSpinsRemaining: 0, grimoireReady: false, ingredients: {},
    pages: [{ id: 'herbalism', label: 'Herbalism', ingredients: ['MANDRAKE_ROOT', 'NIGHTSHADE_BERRIES'] }],
  }
  mocks.slotState.mockResolvedValueOnce({
    arcaneTome, arcanePotions: potions, freeSpinsRemaining: 0, freeSpinWagerPoints: null, freeSpinFeatureMode: null,
    specialPointsBalance: 0, energyBalance: 0, sealCollections: [],
  })
  mocks.usePotion.mockResolvedValueOnce({ ...potions, bottles: [], active: { bottle, spinsRemaining: 3 } })
  const result: SpinResult = {
    ...fixture.baseLoss.response, gameId: experience.rules.gameId,
    symbolSetId: experience.symbols.serverSymbolSetId ?? experience.symbols.id,
    reels: experience.rules.initialReels.map(reel => [...reel]) as SpinResult['reels'], wagerPoints: 2,
  }
  for (const spinsRemaining of [2, 1, 0]) {
    mocks.paidRequest.mockResolvedValueOnce({
      ...result, slotsCreditsBalance: 18.5 + spinsRemaining * .5,
      arcanePotions: { ...potions, bottles: [], active: spinsRemaining > 0 ? { bottle, spinsRemaining } : null },
    })
  }

  await mount(experience, {
    userId: 'test-user', playerName: 'Player', email: 'player@example.test', createdAtUtc: '2026-11-20T00:00:00Z',
    balances: { slotsCredits: 20, freeGames: 0 }, role: 'player',
    slots: { spinsPlayed: 0, wins: 0, losses: 0, creditsWagered: 0, creditsWon: 0, netCredits: 0 },
  })
  await act(async () => controller.selectWager(controller.wagerOptions.indexOf(400)))
  expect(controller.activeWagerDisplay).toBe(100)

  await act(async () => { await controller.activatePotion(bottle.id) })
  expect(controller.activeWagerDisplay).toBe(.5)
  expect(controller.canAffordSelectedWager).toBe(true)
  act(() => controller.changeWager(-1))
  act(() => controller.selectWager(0))
  expect(controller.preferredWager).toBe(100)

  for (let index = 0; index < 3; index++) await spin()
  expect(mocks.paidRequest).toHaveBeenCalledTimes(3)
  for (const [request] of mocks.paidRequest.mock.calls) expect(request.wagerPoints).toBe(2)
  expect(controller.isPotionWagerLocked).toBe(false)
  expect(controller.activeWagerDisplay).toBe(100)
  expect(controller.canAffordSelectedWager).toBe(false)
})

it('recovers a saved paid round at zero credits and then refreshes the authoritative balance', async () => {
  const accountId = 'wukong-reloaded-after-charge'
  const store = createPreparedSlotSpinStore('wukong')
  const pending = { operationId: 'wukong-spent-final-wager', wagerPoints: 12, useFreeSpin: false, useSpecialBoost: false }
  const experience = WUKONG_EXPERIENCE_SET
  const state = {
    freeSpinsRemaining: 0, freeSpinWagerPoints: null, freeSpinFeatureMode: null,
    specialPointsBalance: 0, energyBalance: 0, sealCollections: [], slotsCreditsBalance: 0,
  }
  store.remember(accountId, pending)
  try {
    mocks.slotState.mockResolvedValueOnce(state).mockResolvedValueOnce({ ...state, slotsCreditsBalance: 4.25 })
    mocks.paidRequest.mockResolvedValueOnce({
      ...fixture.baseLoss.response, gameId: experience.rules.gameId,
      symbolSetId: experience.symbols.serverSymbolSetId ?? experience.symbols.id,
      reels: experience.rules.initialReels.map(reel => [...reel]), slotsCreditsBalance: 0,
    })
    await mount(experience, {
      userId: accountId, playerName: 'Player', email: 'player@example.test', createdAtUtc: '2026-11-20T00:00:00Z',
      balances: { slotsCredits: 0, freeGames: 0 }, role: 'player',
      slots: { spinsPlayed: 0, wins: 0, losses: 0, creditsWagered: 0, creditsWon: 0, netCredits: 0 },
    })
    expect(controller.canAffordSelectedWager).toBe(true)
    await spin()
    expect(mocks.paidRequest.mock.calls[0][0]).toMatchObject({ gameId: experience.rules.gameId, wagerPoints: 12 })
    expect(mocks.slotState).toHaveBeenLastCalledWith(experience.rules.gameId, accountId)
    expect(controller.balance).toBe(4.25)
  } finally {
    store.forget(accountId, pending.operationId)
  }
})

it('resumes all five paid Eclipse rounds at the saved wager and charges each round', async () => {
  const experience = OCEAN_ODYSSEY_SLOT_GAME.experience
  const mode = { mode: 'solar-eclipse', remaining: 5, wagerPoints: 12 }
  mocks.slotState.mockResolvedValueOnce({
    paidMode: mode, freeSpinsRemaining: 0, freeSpinWagerPoints: null, freeSpinFeatureMode: null,
    specialPointsBalance: 0, energyBalance: 0, sealCollections: [], slotsCreditsBalance: 20,
  })
  for (const remaining of [4, 3, 2, 1, 0]) mocks.paidRequest.mockResolvedValueOnce({
    ...fixture.baseLoss.response, gameId: experience.rules.gameId,
    symbolSetId: experience.symbols.serverSymbolSetId ?? experience.symbols.id,
    reels: experience.rules.initialReels.map(reel => [...reel]), wagerPoints: 12,
    isFreeSpin: false, freeSpinsAwarded: 0, freeSpinsRemaining: 0, freeSpinFeatureMode: null,
    payout: { ...fixture.baseLoss.response.payout, totalPoints: 0, paylines: [] },
    paidMode: remaining ? { ...mode, remaining } : null, slotsCreditsBalance: 5 + remaining * 3,
  })
  await mount(experience, {
    userId: 'eclipse-paid-carry', playerName: 'Player', email: 'player@example.test', createdAtUtc: '2026-11-20T00:00:00Z',
    balances: { slotsCredits: 20, freeGames: 0 }, role: 'player',
    slots: { spinsPlayed: 0, wins: 0, losses: 0, creditsWagered: 0, creditsWon: 0, netCredits: 0 },
  })
  expect(controller.activeWagerDisplay).toBe(3)
  expect(controller.isPaidModeWagerLocked).toBe(true)
  act(() => controller.changeWager(1))
  act(() => controller.selectWager(0))
  expect(controller.activeWagerDisplay).toBe(3)
  for (let index = 0; index < 5; index++) await spin()
  expect(mocks.paidRequest).toHaveBeenCalledTimes(5)
  for (const [request] of mocks.paidRequest.mock.calls) expect(request).toMatchObject({ wagerPoints: 12, useFreeSpin: false })
  expect(controller.balance).toBe(5)
  expect(controller.freeSpinsRemaining).toBe(0)
  expect(controller.paidMode).toBeNull()
  expect(controller.isPaidModeWagerLocked).toBe(false)
})

it('pauses manual keyboard and autoplay spins while the Eclipse wheel presents', async () => {
  await mount()
  await act(async () => { controller.setSpinFeaturePresenting(true); controller.setIsAutoSpinning(true) })
  await spin()
  act(() => window.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', bubbles: true, cancelable: true })))
  await act(async () => { await vi.advanceTimersByTimeAsync(5_000) })
  expect(mocks.request).not.toHaveBeenCalled()
  await act(async () => controller.setSpinFeaturePresenting(false))
  await act(async () => { await vi.advanceTimersByTimeAsync(1) })
  expect(mocks.request).toHaveBeenCalledTimes(1)
})

it('forwards the native paid Eclipse state between demo rounds', async () => {
  const native = JSON.parse(readFileSync(new NodeURL('./__fixtures__/controller-eclipse-native.json', import.meta.url), 'utf8')) as {
    trigger: SpinResult; round: SpinResult
  }
  mocks.request.mockResolvedValueOnce(native.trigger).mockResolvedValueOnce(native.round)
  await mount(FIFTH_SUN_SLOT_GAME.experience)
  await spin()
  expect(controller.paidMode).toEqual(native.trigger.paidMode)
  await spin()
  expect(mocks.request.mock.calls[1][0]).toMatchObject({
    paidMode: native.trigger.paidMode, wagerPoints: 12, useFreeSpin: false,
  })
  expect(controller.paidMode).toEqual(native.round.paidMode)
  expect(controller.freeSpinsRemaining).toBe(0)
})

describe('symbol guide pauses slot controls', () => {
  it('blocks queued autoplay, manual calls and keyboard spin while the guide is open', async () => {
    await mount()
    await act(async () => { controller.setIsSymbolGuideOpen(true); controller.setIsAutoSpinning(true) })
    await act(async () => { await vi.advanceTimersByTimeAsync(5_000) })
    act(() => window.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', bubbles: true, cancelable: true })))
    await spin()
    expect(mocks.request).not.toHaveBeenCalled()
    expect(controller.isAutoSpinning).toBe(true)
    await act(async () => controller.setIsSymbolGuideOpen(false))
    await act(async () => { await vi.advanceTimersByTimeAsync(1) })
    expect(mocks.request).toHaveBeenCalledTimes(1)
  })

  it('pauses an existing bonus timer and resumes using the actual server balance', async () => {
    mocks.request.mockResolvedValueOnce(withCurrentSymbolSet(fixture.collection[0].response))
      .mockResolvedValueOnce(withCurrentSymbolSet(fixture.collection[1].response))
      .mockResolvedValueOnce(withCurrentSymbolSet(fixture.collection[2].response))
    await mount()
    await spin()
    expect(controller.lastFreeSpinsAwarded).toBe(6)
    expect(controller.lastSpinOutcome?.kind).toBe('bonus')
    expect(controller.freeSpinsRemaining).toBe(6)
    await spin()
    expect(controller.freeSpinsRemaining).toBe(5)
    expect(controller.isSpecialSpinDelayActive).toBe(true)
    await act(async () => controller.setIsSymbolGuideOpen(true))
    await act(async () => { await vi.advanceTimersByTimeAsync(3_000) })
    expect(mocks.request).toHaveBeenCalledTimes(2)
    expect(controller.freeSpinsRemaining).toBe(5)
    await act(async () => controller.setIsSymbolGuideOpen(false))
    await act(async () => { await vi.advanceTimersByTimeAsync(2_400) })
    expect(mocks.request).toHaveBeenCalledTimes(3)
    expect(controller.freeSpinsRemaining).toBe(4)
  })

  it('requests theme ambience on mount and retries when the page becomes active and visible', async () => {
    const visibility = vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('hidden')
    const ambience = DRAGON_HOARD_SLOT_GAME.experience.sounds.events.ambience
    const dispatchPageEvents = () => act(() => {
      window.dispatchEvent(new Event('focus'))
      window.dispatchEvent(new Event('pageshow'))
      document.dispatchEvent(new Event('visibilitychange'))
    })
    try {
      await mount(DRAGON_HOARD_SLOT_GAME.experience)
      expect(mocks.startLoop.mock.calls).toEqual([[ambience]])
      dispatchPageEvents()
      expect(mocks.startLoop).toHaveBeenCalledTimes(1)

      visibility.mockReturnValue('visible')
      act(() => {
        window.dispatchEvent(new Event('pointerdown'))
        window.dispatchEvent(new KeyboardEvent('keydown', { key: 'x' }))
      })
      expect(mocks.startLoop).toHaveBeenCalledTimes(1)
      dispatchPageEvents()
      expect(mocks.startLoop.mock.calls).toEqual([[ambience], [ambience], [ambience], [ambience]])

      await mount(OCEAN_ODYSSEY_SLOT_GAME.experience)
      expect(mocks.startLoop).toHaveBeenLastCalledWith(OCEAN_ODYSSEY_SLOT_GAME.experience.sounds.events.ambience)
    } finally {
      visibility.mockRestore()
    }
  })
})

it('reads real collection awards for presentation without mutating the response or inventing spent free spins', () => {
  const result = fixture.collection[0].response
  const original = JSON.stringify(result)
  expect(result.freeSpinsAwarded).toBe(0)
  expect(getPresentedFreeSpinsAwarded(result, 0, false)).toBe(6)
  expect(getPresentedFreeSpinsAwarded(fixture.collection[1].response, 6, true)).toBe(0)
  expect(JSON.stringify(result)).toBe(original)
  expect(getPresentedFreeSpinsAwarded({ freeSpinsAwarded: 2, freeSpinsRemaining: Number.NaN }, 0, false)).toBe(2)
})
