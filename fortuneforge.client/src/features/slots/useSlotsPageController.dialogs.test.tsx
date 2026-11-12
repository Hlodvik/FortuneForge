// @vitest-environment jsdom
import { readFileSync } from 'node:fs'
import { URL as NodeURL } from 'node:url'
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { OCEAN_ODYSSEY_SLOT_GAME } from '@fortuneforge/games-ocean-odyssey/manifest'
import { DRAGON_HOARD_SLOT_GAME } from '../../games/slots/dragonHoard/manifest'
import type { SlotExperienceSet } from './config/slotExperienceSets'
import { CLASSIC_SLOT_SYMBOL_SET_ID } from './config/symbolSets'
import { useSlotsPageController, type SlotsPageController } from './useSlotsPageController'
import { getPresentedFreeSpinsAwarded } from './presentation/freeSpinAwards'
import type { SpinResult } from './types/slots'

const mocks = vi.hoisted(() => ({
  request: vi.fn(), startLoop: vi.fn(), stopLoop: vi.fn(), playCue: vi.fn(),
  playSequence: vi.fn(), stopResultCues: vi.fn(), noop: vi.fn(),
  preferences: { volume: 50, mode: 'all' as const },
}))
vi.mock('./services/slotsApi', async importOriginal => ({
  ...await importOriginal<typeof import('./services/slotsApi')>(),
  requestDemoAvailability: vi.fn(async () => undefined), requestDemoSpin: mocks.request,
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
function Harness({ experience }: { experience: SlotExperienceSet }) {
  controller = useSlotsPageController({ demoMode: true, experienceSet: experience })
  return <div />
}
async function mount(experience = OCEAN_ODYSSEY_SLOT_GAME.experience) {
  await act(async () => root.render(<Harness experience={experience} />))
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
