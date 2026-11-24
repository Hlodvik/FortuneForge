// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { CrookedRoadView } from '../src/CrookedRoadView'
import { crookedRoadRules } from '../src/manifest'
import type { RoadBonus, RoadState } from '../src/road'
import type { CrookedRoadController } from '../src/useCrookedRoad'

function road(activeBonus: RoadBonus | null = null): RoadState {
  return { contractVersion: 1, revision: 31, board: Array(9).fill(null),
    hand: [{ id: 'ready', kind: 'straight', rotation: 0 }], queuedTiles: [],
    reserveTileCounts: { straight: 0, corner: 0, junction: 0 },
    funding: { spins: 7, requiredSpins: 20, wagerPointsTotal: 2100 },
    tickets: [], activeBonus, reachableDestinations: [], lastTravel: null, completedJourneys: 0 }
}
function journey(kind: RoadBonus['kind']): RoadBonus {
  return { id: 'journey', kind, step: 0, totalSteps: kind === 'fort' ? 27 : 3,
    awardPoints: 0, fundedWagerPoints: 300, choice: null, board: null,
    held: kind === 'fort' ? Array(9).fill(0) : null, respinsRemaining: kind === 'fort' ? 3 : 0,
    doors: kind === 'hill' ? [0, 1, 2].map(index => ({ index, selected: false, revealed: false, prizePoints: null })) : [] }
}
function state(activeBonus: RoadBonus | null = null) {
  return { gameFeatureState: road(activeBonus), freeSpinsRemaining: activeBonus ? 3 : 0,
    featureActionRequired: activeBonus?.kind === 'well' || activeBonus?.kind === 'hill' } as CrookedRoadController['state']
}
function controller(): CrookedRoadController {
  return {
    account: null, state: state(), balance: 100, displayedReels: crookedRoadRules.initialReels.map(reel => [...reel]),
    reelMotion: Array(5).fill('idle'), reelStripStyle: () => ({}), isSpinning: false, isAutoSpinning: false,
    setIsAutoSpinning: vi.fn(), isStopRequested: false, handleSpinButtonClick: vi.fn(),
    wagerIndex: 2, selectWager: vi.fn(), wagerOptions: crookedRoadRules.wagerOptions,
    wagerPoints: 300, activeWager: 3, isWagerLocked: false, controlsDisabled: false, spinDisabled: false,
    spinAvailability: 'available', spinError: null, isGameFeatureUpdating: false, gameFeatureError: null,
    performGameFeatureAction: vi.fn(async () => undefined), lastFeature: null, winningPaylines: [], resultKey: 0, spinKey: 0,
    hasCompletedSpin: false, lastWin: 0, isFeaturePresenting: false, setFeaturePresenting: vi.fn(), isFeatureActionRequired: false,
    isHelpOpen: false, setIsHelpOpen: vi.fn(), isSettingsOpen: false, setIsSettingsOpen: vi.fn(),
    isReloadPromptOpen: false, setIsReloadPromptOpen: vi.fn(), reducedMotion: true,
    onNavigate: vi.fn(), retryConnection: vi.fn(),
    audio: { preferences: { mode: 'all', volume: 60 }, toggleMuted: vi.fn(), toggleResultsOnly: vi.fn(), setVolume: vi.fn() },
  } as unknown as CrookedRoadController
}

let host: HTMLDivElement
let root: Root
let c: CrookedRoadController
async function render() { await act(async () => root.render(<CrookedRoadView controller={c} />)) }
async function click(element: HTMLButtonElement) { await act(async () => element.click()) }
const button = (label: string) => host.querySelector<HTMLButtonElement>(`button[aria-label="${label}"]`)!

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  host = document.createElement('div')
  document.body.append(host)
  root = createRoot(host)
  c = controller()
})
afterEach(() => {
  act(() => root.unmount())
  host.remove()
  vi.unstubAllGlobals()
})

describe('package-owned Crooked Road screen', () => {
  it('renders its five-by-three reels and keeps the standard Auto, Bet, Spin order', async () => {
    await render()
    expect(host.querySelectorAll('.slot-reel')).toHaveLength(5)
    expect(host.querySelectorAll('.slot-symbol')).toHaveLength(15)
    expect(host.querySelectorAll('.crooked-road__cell')).toHaveLength(9)
    const controls = host.querySelector('.sidecar-spin-controls')!
    expect([...controls.querySelectorAll('button')].map(element => element.getAttribute('aria-label')))
      .toEqual(['Start autospin', 'Change bet, currently R3', 'Spin the reels'])
    await click(button('Start autospin'))
    await click(button('Spin the reels'))
    expect(c.setIsAutoSpinning).toHaveBeenCalledWith(true)
    expect(c.handleSpinButtonClick).toHaveBeenCalledTimes(1)
    expect(host.querySelector('.symbol-value-guide')).toBeNull()
  })

  it('renders hydration and reconnect states without making a wager available', async () => {
    c = { ...c, state: null, spinAvailability: 'checking', controlsDisabled: true, spinDisabled: true }
    await render()
    expect(button('Spin the reels').disabled).toBe(true)
    expect(host.textContent).toContain('Checking your saved road')
    expect(host.querySelectorAll('.slot-symbol')).toHaveLength(15)
    c = { ...c, spinAvailability: 'unavailable', spinError: 'Connection interrupted.' }
    await render()
    await click(host.querySelector<HTMLButtonElement>('.crooked-road-game__error button')!)
    expect(c.retryConnection).toHaveBeenCalledTimes(1)
    expect(c.handleSpinButtonClick).not.toHaveBeenCalled()
  })

  it('keeps required journey choices usable while Spin waits for the server reply', async () => {
    const active = journey('well')
    c = { ...c, state: state(active), isFeatureActionRequired: true, spinDisabled: true, isWagerLocked: true }
    await render()
    expect(button('Spin the reels').disabled).toBe(true)
    const spring = host.querySelectorAll<HTMLButtonElement>('.crooked-road__spring-choices button')[1]!
    expect(spring.disabled).toBe(false)
    await click(spring)
    expect(c.performGameFeatureAction).toHaveBeenCalledExactlyOnceWith({ expectedRevision: 31, action: 'choose-spring', choice: 1 })
    expect(spring.getAttribute('aria-pressed')).toBe('false')
    expect(host.querySelector('.crooked-road__well-board')).toBeNull()
    c = { ...c, state: state({ ...active, choice: 1 }), isFeatureActionRequired: false, spinDisabled: false }
    await render()
    expect(button('Spin the reels').disabled).toBe(false)
    expect(host.querySelectorAll('.slot-symbol')).toHaveLength(15)
    expect(host.querySelector('.slots-page__special-game-tally')).toBeNull()
  })

  it('leaves Auto stop and quick Stop reachable during a running automatic spin', async () => {
    c = { ...c, isAutoSpinning: true, isSpinning: true, spinDisabled: true, controlsDisabled: true }
    await render()
    expect(button('Stop autospin').disabled).toBe(false)
    expect(button('Stop the spin').disabled).toBe(false)
    await click(button('Stop autospin'))
    await click(button('Stop the spin'))
    expect(c.setIsAutoSpinning).toHaveBeenCalledWith(false)
    expect(c.handleSpinButtonClick).toHaveBeenCalledTimes(1)
  })

  it('keeps Well autospin available after the spring choice while locking its funded wager', async () => {
    const active = { ...journey('well'), choice: 1 }
    c = { ...c, state: state(active), isWagerLocked: true }
    await render()
    expect(button('Start autospin').disabled).toBe(false)
    expect(button('Bet locked at R3').disabled).toBe(true)
    expect(button('Spin the reels').disabled).toBe(false)
    await click(button('Start autospin'))
    expect(c.setIsAutoSpinning).toHaveBeenCalledWith(true)
    c = { ...c, isAutoSpinning: true, isSpinning: true, spinDisabled: true, controlsDisabled: true }
    await render()
    expect(button('Stop autospin').disabled).toBe(false)
    await click(button('Stop autospin'))
    expect(c.setIsAutoSpinning).toHaveBeenLastCalledWith(false)
    c = { ...c, state: state(), isAutoSpinning: false, isSpinning: false,
      lastFeature: { kind: 'well', completed: true, bonus: { ...active, step: 3 } }, isFeaturePresenting: true }
    await render()
    expect(button('Start autospin').disabled).toBe(true)
    expect(button('Spin the reels').disabled).toBe(true)
    expect(c.performGameFeatureAction).not.toHaveBeenCalled()
  })

  it('owns completed journey review and releases it through a local Return action', async () => {
    c = { ...c, isFeaturePresenting: true, spinDisabled: true,
      lastFeature: { kind: 'hill', completed: true, bonus: { ...journey('hill'), step: 3, awardPoints: 900 } } }
    await render()
    expect(host.querySelector('.crooked-road')?.getAttribute('data-feature-kind')).toBe('hill')
    expect(host.querySelectorAll('.crooked-road__doors button')).toHaveLength(3)
    expect(host.textContent).toContain('Journey complete')
    expect(host.textContent).not.toContain('free games await')
    expect(host.querySelector('.slots-page__special-game-tally')).toBeNull()
    await click(host.querySelector<HTMLButtonElement>('.crooked-road__return')!)
    expect(c.setFeaturePresenting).toHaveBeenLastCalledWith(false)
    expect(c.performGameFeatureAction).not.toHaveBeenCalled()
    expect(c.handleSpinButtonClick).not.toHaveBeenCalled()
    expect(host.querySelector('.crooked-road')?.getAttribute('data-feature-kind')).toBeNull()
  })

  it('stops autoplay before opening accurate owned help and exposes the audio preferences', async () => {
    await render()
    button('How to win').focus()
    await click(button('How to win'))
    expect(c.onNavigate).toHaveBeenCalledTimes(1)
    expect(c.setIsHelpOpen).toHaveBeenCalledWith(true)
    expect(vi.mocked(c.onNavigate).mock.invocationCallOrder[0]).toBeLessThan(vi.mocked(c.setIsHelpOpen).mock.invocationCallOrder[0]!)
    c = { ...c, isHelpOpen: true }
    await render()
    expect(document.querySelectorAll('.crooked-road-help figure')).toHaveLength(10)
    expect(document.querySelector('[role="dialog"]')?.textContent).toContain('Three, four or five matching symbols')
    await act(async () => document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true })))
    expect(c.setIsHelpOpen).toHaveBeenLastCalledWith(false)
    c = { ...c, isHelpOpen: false, isSettingsOpen: true }
    await render()
    const modes = document.querySelectorAll<HTMLButtonElement>('.audio-settings__mode')
    await click(modes[0]!)
    await click(modes[1]!)
    expect(c.audio.toggleMuted).toHaveBeenCalledTimes(1)
    expect(c.audio.toggleResultsOnly).toHaveBeenCalledTimes(1)
    expect(document.querySelector<HTMLInputElement>('#game-volume')?.value).toBe('60')
  })
})
