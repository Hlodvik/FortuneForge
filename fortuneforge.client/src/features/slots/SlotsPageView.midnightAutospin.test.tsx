// @vitest-environment jsdom
import { act, useState, type ReactNode } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { DRAGON_HOARD_SLOT_GAME } from '../../games/slots/dragonHoard/manifest'
import { OCEAN_ODYSSEY_SLOT_GAME } from '../../games/slots/oceanOdyssey/manifest'
import type { SlotExperienceSet } from './config/slotExperienceSets'
import { SlotsPageView } from './SlotsPageView'
import { useSlotsPageController } from './useSlotsPageController'

const mocks = vi.hoisted(() => ({ clearError: vi.fn(), noop: vi.fn() }))
vi.mock('../../components/InGameShell', () => ({ InGameShell: ({ children }: { children: ReactNode }) => <>{children}</> }))
vi.mock('./components/SlotMachine', () => ({ SlotMachine: () => null }))
vi.mock('./services/slotsApi', async importOriginal => ({
  ...await importOriginal<typeof import('./services/slotsApi')>(),
  requestDemoAvailability: vi.fn(async () => undefined),
}))
vi.mock('./hooks/useSlotAudio', () => ({ useSlotAudio: () => ({
  preferences: { volume: 50, mode: 'all' }, startLoop: mocks.noop, stopLoop: mocks.noop,
  playCue: mocks.noop, playSequence: mocks.noop, stopResultCues: mocks.noop,
  setVolume: mocks.noop, toggleMuted: mocks.noop, toggleResultsOnly: mocks.noop,
}) }))
vi.mock('./hooks/usePrefersReducedMotion', () => ({ usePrefersReducedMotion: () => true }))

let host: HTMLDivElement
let root: Root
function Harness({ experience, disabled = false }: { experience: SlotExperienceSet; disabled?: boolean }) {
  const controller = useSlotsPageController({ demoMode: true, experienceSet: experience })
  // Exercise the view's real toggle handler without starting engine autoplay.
  const [isAutoSpinning, setIsAutoSpinning] = useState(false)
  return <SlotsPageView {...controller} isAutoSpinning={isAutoSpinning} setIsAutoSpinning={setIsAutoSpinning}
    isDemoSpinDisabled={disabled} setSpinError={mocks.clearError} />
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

  it('keeps another game’s existing text control and styling hook unchanged', async () => {
    const button = await mount(OCEAN_ODYSSEY_SLOT_GAME.experience)
    expect(button.classList.contains('midnight-vault__autospin')).toBe(false)
    expect(button.querySelector('strong')?.textContent).toBe('Autospin')
    expect(button.querySelector('svg')).toBeNull()
  })
})
