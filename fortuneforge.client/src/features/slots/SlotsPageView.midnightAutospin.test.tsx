// @vitest-environment jsdom
import { act, useState, type ReactNode } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { DRAGON_HOARD_SLOT_GAME } from '../../games/slots/dragonHoard/manifest'
import { OCEAN_ODYSSEY_SLOT_GAME } from '../../games/slots/oceanOdyssey/manifest'
import { loadAllSlotGameManifests } from '../../games/slots/routeRegistry'
import { WUKONG_SLOT_GAME } from '../../games/slots/wukong/manifest'
import type { SlotExperienceSet } from './config/slotExperienceSets'
import { SlotsPageView } from './SlotsPageView'
import { useSlotsPageController } from './useSlotsPageController'

const mocks = vi.hoisted(() => ({ clearError: vi.fn(), spin: vi.fn(), toggleMuted: vi.fn(), noop: vi.fn() }))
vi.mock('../../components/InGameShell', () => ({ InGameShell: ({ children }: { children: ReactNode }) => <>{children}</> }))
vi.mock('./components/SlotMachine', () => ({ SlotMachine: () => null }))
vi.mock('./services/slotsApi', async importOriginal => ({
  ...await importOriginal<typeof import('./services/slotsApi')>(),
  requestDemoAvailability: vi.fn(async () => undefined),
}))
vi.mock('./hooks/useSlotAudio', () => ({ useSlotAudio: () => ({
  preferences: { volume: 50, mode: 'all' }, startLoop: mocks.noop, stopLoop: mocks.noop,
  playCue: mocks.noop, playSequence: mocks.noop, stopResultCues: mocks.noop,
  setVolume: mocks.noop, toggleMuted: mocks.toggleMuted, toggleResultsOnly: mocks.noop,
}) }))
vi.mock('./hooks/usePrefersReducedMotion', () => ({ usePrefersReducedMotion: () => true }))

let host: HTMLDivElement
let root: Root
function Harness({ experience, disabled = false }: { experience: SlotExperienceSet; disabled?: boolean }) {
  const controller = useSlotsPageController({ demoMode: true, experienceSet: experience })
  // Exercise the view's real toggle handler without starting engine autoplay.
  const [isAutoSpinning, setIsAutoSpinning] = useState(false)
  return <SlotsPageView {...controller} isAutoSpinning={isAutoSpinning} setIsAutoSpinning={setIsAutoSpinning}
    isSpinServiceDisabled={disabled} setSpinError={mocks.clearError} handleSpinButtonClick={mocks.spin} />
}
async function mount(experience: SlotExperienceSet = DRAGON_HOARD_SLOT_GAME.experience, disabled = false) {
  await act(async () => root.render(<Harness experience={experience} disabled={disabled} />))
  return host.querySelector<HTMLButtonElement>('.slots-page__auto-spin')!
}

beforeEach(() => {
  vi.clearAllMocks()
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  host = document.createElement('div')
  document.body.append(host)
  root = createRoot(host)
})
afterEach(() => {
  act(() => root.unmount())
  host.remove()
  vi.unstubAllGlobals()
})

describe('Midnight Vault autospin control', () => {
  it('uses native toggle-button semantics and changes play to stop when started, then back when stopped', async () => {
    const button = await mount()
    expect(button.classList.contains('midnight-vault__autospin')).toBe(true)
    expect(button.getAttribute('aria-label')).toBe('Start autospin')
    expect(button.getAttribute('aria-pressed')).toBe('false')
    expect(button.hasAttribute('role')).toBe(false)
    expect(button.hasAttribute('aria-checked')).toBe(false)
    expect(button.querySelector('svg')?.getAttribute('aria-hidden')).toBe('true')
    expect(button.querySelector('.midnight-vault__autospin-action')?.tagName.toLowerCase()).toBe('path')
    const loop = button.querySelector('svg > path')?.getAttribute('d')

    act(() => button.click())
    expect(button.getAttribute('aria-label')).toBe('Stop autospin')
    expect(button.getAttribute('aria-pressed')).toBe('true')
    expect(button.querySelector('.midnight-vault__autospin-action')?.tagName.toLowerCase()).toBe('rect')
    expect(button.querySelector('svg > path')?.getAttribute('d')).toBe(loop)
    expect(mocks.clearError).toHaveBeenLastCalledWith(null)

    act(() => button.click())
    expect(button.getAttribute('aria-pressed')).toBe('false')
    expect(button.querySelector('.midnight-vault__autospin-action')?.tagName.toLowerCase()).toBe('path')
  })

  it('retains the existing disabled guard', async () => {
    const button = await mount(DRAGON_HOARD_SLOT_GAME.experience, true)
    expect(button.disabled).toBe(true)
    act(() => button.click())
    expect(button.getAttribute('aria-pressed')).toBe('false')
    expect(mocks.clearError).not.toHaveBeenCalled()
  })

  it('keeps Wukong’s existing text control and styling hook unchanged', async () => {
    const button = await mount(WUKONG_SLOT_GAME.experience)
    expect(button.classList.contains('midnight-vault__autospin')).toBe(false)
    expect(button.querySelector('strong')?.textContent).toBe('Autospin')
    expect(button.querySelector('svg')).toBeNull()
  })
})

describe('Shared sidecar slot controls', () => {
  it('keeps Ocean’s autospin, wager, and spin callbacks connected to the new icon controls', async () => {
    await mount(OCEAN_ODYSSEY_SLOT_GAME.experience)
    expect(host.querySelector('.slots-page__auto-spin')).toBeNull()
    const mute = host.querySelector<HTMLButtonElement>('.slots-page__mute-shortcut')!
    expect(mute.getAttribute('aria-pressed')).toBe('false')
    act(() => mute.click())
    expect(mocks.toggleMuted).toHaveBeenCalledOnce()
    const auto = host.querySelector<HTMLButtonElement>('.pirates-spin-controls__small[aria-label="Start autospin"]')!
    expect(auto.querySelector('svg')?.getAttribute('aria-hidden')).toBe('true')
    act(() => auto.click())
    expect(auto.getAttribute('aria-pressed')).toBe('true')
    expect(auto.getAttribute('aria-label')).toBe('Stop autospin')
    expect(mocks.clearError).toHaveBeenLastCalledWith(null)
    act(() => auto.click())

    const before = host.querySelector('.pirates-spin-controls__bet-readout strong')?.textContent
    const bet = host.querySelector<HTMLButtonElement>('.pirates-spin-controls__bet-wrap > button')!
    act(() => bet.click())
    const increase = document.body.querySelector<HTMLButtonElement>('[aria-label="Increase bet"]')!
    act(() => increase.click())
    expect(host.querySelector('.pirates-spin-controls__bet-readout strong')?.textContent).not.toBe(before)
    const spin = host.querySelector<HTMLButtonElement>('.spin-button--pirate-loop')!
    act(() => spin.click())
    expect(mocks.spin).toHaveBeenCalledTimes(1)
    expect(document.body.querySelector('[aria-label="Choose bet"]')).toBeNull()
  })

  it('uses the shared placement hook for all 17 eligible catalog games and keeps the three exclusions on their existing controls', async () => {
    const games = await loadAllSlotGameManifests()
    expect(games).toHaveLength(20)
    const excluded = new Set(['dragon-hoard', 'wukong-journey-to-the-west', 'pirates-fortune'])
    let sharedCount = 0
    for (const game of games) {
      await mount(game.experience)
      const page = host.querySelector('.slots-page')!
      const keyboardRows = page.querySelectorAll('.slots-page__keyboard-help')
      expect(keyboardRows, game.id).toHaveLength(game.id === 'dragon-hoard' ? 0 : 1)
      if (game.id !== 'dragon-hoard') {
        expect(page.querySelector(':scope > .slots-page__keyboard-help > button.slots-page__mute-shortcut'), game.id).not.toBeNull()
      }
      if (!excluded.has(game.id)) {
        expect(page.getAttribute('data-slot-controls'), game.id).toBe('sidecar')
        expect(page.querySelector('.spin-button--pirate-loop'), game.id).not.toBeNull()
        expect(page.querySelector('.slots-page__utility-buttons--pirates'), game.id).not.toBeNull()
        sharedCount += 1
      } else {
        expect(page.hasAttribute('data-slot-controls'), game.id).toBe(false)
        const originalVariant = game.id === 'dragon-hoard' ? '.spin-button--vault-dial'
          : game.id === 'wukong-journey-to-the-west' ? '.spin-button--custom-icon'
          : '.spin-button--pirate-loop'
        expect(page.querySelector(originalVariant), game.id).not.toBeNull()
      }
    }
    expect(sharedCount).toBe(17)
  }, 20_000)
})
