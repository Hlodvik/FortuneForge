// @vitest-environment jsdom
import { act, type ReactNode } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { RAINBOW_REALM_SLOT_GAME } from '../../games/slots/rainbowRealm/manifest'
import { OCEAN_ODYSSEY_SLOT_GAME } from '../../games/slots/oceanOdyssey/manifest'
import type { SlotExperienceSet } from './config/slotExperienceSets'
import { SlotsPageView } from './SlotsPageView'
import type { SpinResult } from './types/slots'
import { useSlotsPageController, type SlotsPageController } from './useSlotsPageController'

const mocks = vi.hoisted(() => ({ request: vi.fn(), state: vi.fn(), noop: vi.fn() }))
vi.mock('../../components/InGameShell', () => ({ InGameShell: ({ children }: { children: ReactNode }) => <>{children}</> }))
vi.mock('../../components/PaymentAlertsMenu', () => ({ PaymentAlertsMenu: () => null }))
vi.mock('./components/SlotMachine', () => ({ SlotMachine: () => null }))
vi.mock('./services/slotsApi', async importOriginal => ({
  ...await importOriginal<typeof import('./services/slotsApi')>(),
  requestDemoAvailability: vi.fn(async () => undefined),
  requestDemoSpin: mocks.request, requestSpin: mocks.request, requestSlotState: mocks.state,
}))
vi.mock('./hooks/useSlotAudio', () => ({ useSlotAudio: () => ({
  preferences: { volume: 50, mode: 'all' }, startLoop: mocks.noop, stopLoop: mocks.noop,
  playCue: mocks.noop, playSequence: mocks.noop, stopResultCues: mocks.noop,
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

const experience = RAINBOW_REALM_SLOT_GAME.experience
function response(remaining: number, points = 0, isFreeSpin = false): SpinResult {
  return {
    spinId: `presentation-${remaining}-${points}`, gameId: experience.rules.gameId,
    reelSetId: 'presentation', symbolSetId: experience.symbols.serverSymbolSetId!, paytableId: 'presentation',
    wagerPoints: experience.rules.wagerOptions[0], pointValueInCents: experience.rules.pointValueInCents,
    reelStops: experience.rules.initialReels.map(() => 0), reels: experience.rules.initialReels.map(reel => [...reel]),
    consecutiveFiveMisses: 0, fiveMatchPityTriggered: false, isFreeSpin,
    freeSpinsAwarded: isFreeSpin ? 0 : remaining, freeSpinsRemaining: remaining,
    freeSpinWagerPoints: remaining > 0 ? experience.rules.wagerOptions[0] : null,
    specialPointsAwarded: 0, specialPointsBalance: 0, energyAwarded: 0, energyBalance: 0,
    energyMultiplierApplied: false, payoutMultiplier: 1, monkeyPawCount: 0,
    moneyGrabPoints: 0, bananaBonusPoints: 0, sealsAwarded: {}, sealCollections: [],
    freeSpinFeatureMode: null, specialBoostApplied: false, slotsCreditsBalance: null,
    payout: { totalPoints: points, paylines: [] },
  }
}

let host: HTMLDivElement
let root: Root
let controller: SlotsPageController
function Harness({ selectedExperience, demoMode }: { selectedExperience: SlotExperienceSet; demoMode: boolean }) {
  controller = useSlotsPageController({ demoMode, experienceSet: selectedExperience })
  return <SlotsPageView {...controller} />
}
async function mount(selectedExperience = experience, demoMode = true) {
  await act(async () => root.render(<Harness selectedExperience={selectedExperience} demoMode={demoMode} />))
}
async function spin() {
  await act(async () => { controller.handleSpinButtonClick() })
}
async function completeBonus(points: number[]) {
  mocks.request.mockResolvedValueOnce(response(points.length, 999))
  await spin()
  for (const [index, amount] of points.entries()) {
    mocks.request.mockResolvedValueOnce(response(points.length - index - 1, amount, true))
    await spin()
  }
}

beforeEach(() => {
  vi.useFakeTimers()
  vi.clearAllMocks()
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  host = document.createElement('div')
  document.body.append(host)
  root = createRoot(host)
  mocks.state.mockResolvedValue({ freeSpinsRemaining: 0, freeSpinWagerPoints: null,
    freeSpinFeatureMode: null, specialPointsBalance: 0, energyBalance: 0, sealCollections: [] })
})
afterEach(() => {
  act(() => root.unmount())
  host.remove()
  vi.useRealTimers()
  vi.unstubAllGlobals()
})

describe('Rainbow Realm Orchard Rush presentation', () => {
  it('shows the ready intro from the server bonus balance and waits for Spin', async () => {
    await mount()
    expect(host.querySelector('.slots-page__meter-stack')).toBeNull()
    mocks.request.mockResolvedValueOnce(response(10))
    await spin()
    expect(host.querySelector('[data-rainbow-intro="true"]')).not.toBeNull()
    expect(host.querySelector('.rainbow-realm__bonus-intro')?.textContent).toContain('10 bonus spins are ready')
    await act(async () => { await vi.advanceTimersByTimeAsync(5_000) })
    expect(mocks.request).toHaveBeenCalledTimes(1)
    expect(controller.rainbowBonusSummary).toBeNull()
  })

  it('totals only completed bonus payouts, keeps the recap open and blocks every spin route until Continue', async () => {
    await mount()
    const points = [13, 29, 0, 1, 0, 0, 0, 0, 0, 67]
    await completeBonus(points)
    expect(controller.rainbowBonusSummary?.totalRand).toBe(110 * experience.rules.pointValueInCents / 100)
    const recap = host.querySelector<HTMLElement>('.rainbow-realm__bonus-summary')!
    expect(recap.getAttribute('role')).toBe('dialog')
    expect(recap.getAttribute('aria-modal')).toBe('true')
    expect(recap.querySelector('button')).toBe(document.activeElement)
    expect(controller.isAutoSpinning).toBe(false)
    const before = controller.balance
    await act(async () => controller.setIsAutoSpinning(true))
    await spin()
    act(() => window.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', bubbles: true, cancelable: true })))
    await act(async () => { await vi.advanceTimersByTimeAsync(5_000) })
    expect(mocks.request).toHaveBeenCalledTimes(11)
    expect(controller.balance).toBe(before)
    expect(host.querySelector('.rainbow-realm__bonus-summary')).toBe(recap)
    await act(async () => { recap.querySelector<HTMLButtonElement>('button')!.click() })
    expect(controller.rainbowBonusSummary).toBeNull()
    expect(host.querySelector('[data-rainbow-bonus]')).toBeNull()
  })

  it('does not invent a complete payout total for a bonus resumed from persisted server state', async () => {
    mocks.state.mockResolvedValue({ freeSpinsRemaining: 1, freeSpinWagerPoints: experience.rules.wagerOptions[0],
      freeSpinFeatureMode: null, specialPointsBalance: 0, energyBalance: 0, sealCollections: [] })
    await mount(experience, false)
    mocks.request.mockResolvedValueOnce({ ...response(0, 37, true), slotsCreditsBalance: 42.47 })
    await spin()
    expect(controller.rainbowBonusSummary).toEqual({ totalRand: null })
    expect(host.querySelector('.rainbow-realm__bonus-summary')?.textContent).toContain('Your bonus round is complete.')
    expect(host.querySelector('.rainbow-realm__bonus-summary')?.textContent).not.toContain('Bonus win')
    expect(controller.balance).toBe(42.47)
  })

  it('keeps a failed final request out of the recap and completes a resumed round on its successful retry', async () => {
    mocks.state.mockResolvedValue({ freeSpinsRemaining: 1, freeSpinWagerPoints: experience.rules.wagerOptions[0],
      freeSpinFeatureMode: null, specialPointsBalance: 0, energyBalance: 0, sealCollections: [] })
    await mount(experience, false)
    mocks.request.mockRejectedValueOnce(new Error('Temporary failure'))
    await spin()
    expect(controller.rainbowBonusSummary).toBeNull()
    expect(controller.freeSpinsRemaining).toBe(1)
    mocks.request.mockResolvedValueOnce(response(0, 37, true))
    await spin()
    expect(controller.rainbowBonusSummary).toEqual({ totalRand: null })
    expect(mocks.request).toHaveBeenCalledTimes(2)
  })

  it('clears a finished recap when the game changes and does not add Rainbow UI to another theme', async () => {
    await mount()
    await completeBonus([1])
    expect(controller.rainbowBonusSummary).not.toBeNull()
    await mount(OCEAN_ODYSSEY_SLOT_GAME.experience)
    expect(controller.rainbowBonusSummary).toBeNull()
    expect(host.querySelector('[data-rainbow-bonus], .rainbow-realm__bonus-intro')).toBeNull()
  })
})
