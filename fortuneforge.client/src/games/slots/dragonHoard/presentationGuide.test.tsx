// @vitest-environment jsdom
import { act, createRef } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { renderToStaticMarkup } from 'react-dom/server'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { SymbolValueGuide } from '../../../features/slots/components/SymbolValueGuide'
import { getSlotOutcomePresentation, type SlotOutcomePresentationInput } from '../../../features/slots/presentation/spinPresentation'
import { WinHelpDialog } from '../../../features/slots/WinHelpDialog'
import { NEON_NIGHTS_DEFINITION } from '../neonNights/definition'
import { createShowcaseSlotGame } from '../shared/createShowcaseSlotGame'
import type { SlotGameManifest } from '../shared/slotGameManifest'
import { DRAGON_HOARD_SLOT_GAME } from './manifest'
import { midnightVaultMultiplier } from './math'

const target = DRAGON_HOARD_SLOT_GAME
const unchangedDefault = createShowcaseSlotGame(NEON_NIGHTS_DEFINITION)
let host: HTMLDivElement
let root: Root

beforeEach(() => {
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

function helpMarkup(game: SlotGameManifest) {
  return renderToStaticMarkup(<WinHelpDialog
    isOpen closeButtonRef={createRef<HTMLButtonElement>()}
    help={game.experience.help} symbolSet={game.experience.symbols} onClose={() => undefined}
  />)
}

function text(markup: string) {
  const element = document.createElement('div')
  element.innerHTML = markup
  return element.textContent ?? ''
}

function openValues(game: SlotGameManifest, showValueTokens: boolean) {
  act(() => root.render(<SymbolValueGuide symbolSet={game.experience.symbols} showValueTokens={showValueTokens} />))
  act(() => host.querySelector<HTMLButtonElement>('.symbol-value-guide__trigger')!.click())
  return document.body.querySelector<HTMLElement>('.symbol-value-guide--modal')!
}

function outcomeInput(): SlotOutcomePresentationInput {
  return {
    activeWager: 5, bestPayline: null, bestSymbolLabel: null,
    canAffordSelectedWager: true, demoAvailabilityMessage: null, energyLabel: null,
    gameTitle: 'Midnight Vault', hasCompletedSpin: true, isSpinning: false,
    lastEnergyAwarded: 0, lastEnergyMultiplierApplied: false, lastFreeSpinsAwarded: 0,
    lastWin: 0, spinError: null, spinStage: 'requesting', freeSpinsRemaining: 0,
    useFreeGameForNextSpin: false,
  }
}

describe('Midnight Vault optional presentation guides', () => {
  it('renders the target path count, uniform payline note and separate scatter/collection awards', () => {
    const markup = helpMarkup(target)
    const copy = text(markup)
    expect(markup.match(/aria-label="Valid five-symbol payline \d+"/g)).toHaveLength(23)
    expect(copy).toContain('All 23 full payline patterns use the same symbol multipliers.')
    expect(copy).not.toContain('The more central patterns pay more.')
    expect(copy).toContain('receive 10 Vault Breach spins')
    expect(copy).toContain('30 matching gems')
    expect(copy).toContain('10-spin enhanced Vault Breach round')
    expect(copy).toContain('Every gem color awards the same feature: six rows and extra Vault wilds.')
    expect(copy).toContain('Each paying match pays an exact multiple of the wager')
    expect(copy).toContain('rounded to the nearest R0.25')
    expect(copy).toContain('Ordinary four-symbol runs at either edge of a payline pay the three-symbol rate.')
    expect(copy).toContain('Four Vault wilds, or four-symbol runs using wild substitution, do not pay.')
    expect(copy).not.toContain('Two collectors double the haul')
    expect(copy).not.toContain('improves collection odds')
  })

  it('renders calibrated target entries without disabled energy, collector or value-token claims', () => {
    const modal = openValues(target, false)
    expect(modal.querySelectorAll('.symbol-value-guide__item')).toHaveLength(15)
    expect(modal.textContent).toContain('Cash briefcase')
    expect(modal.textContent).toContain('Triple sevens')
    expect(modal.textContent).toContain(midnightVaultMultiplier(14))
    expect(modal.textContent).toContain(target.experience.symbols.guideNote)
    const power = target.experience.symbols.guideEntries.find(entry => entry.symbol === 'POWER')!
    expect(power).toMatchObject({ firstLabel: '3–4', firstValue: midnightVaultMultiplier(2), secondValue: midnightVaultMultiplier(4) })
    expect(modal.querySelector('.symbol-value-guide__item--bolt')).toBeNull()
    expect(modal.querySelector('.symbol-value-guide__item--paw')).toBeNull()
    expect(modal.querySelector('[class*="symbol-value-guide__item--rand_"]')).toBeNull()
    expect(modal.textContent).not.toContain('Rand values multiply the selected wager.')
    expect(target.experience.features.energy).toBeUndefined()
    expect(target.experience.features.moneyGrab).toBeUndefined()
  })

  it('keeps another title’s guide distinct while applying its own audited rules', () => {
    const modal = openValues(unchangedDefault, false)
    expect(unchangedDefault.experience.symbols.guideNote).toContain('payout calibration')
    expect(modal.querySelector('.symbol-value-guide__item--bolt')).toBeNull()
    expect(modal.querySelector('.symbol-value-guide__item--paw')).toBeNull()
    expect(modal.textContent).toContain('3–4')
    expect(modal.textContent).not.toContain('Rand values multiply the selected wager.')
    expect(modal.textContent).not.toContain('1.076×')
  })

  it('preserves another title’s labels while correcting its uniform payline note', () => {
    const copy = text(helpMarkup(unchangedDefault))
    expect(copy).toContain('Free games')
    expect(copy).toContain('FREE GAME symbols')
    expect(copy).toContain('All 23 full payline patterns use the same symbol multipliers.')
    expect(copy).not.toContain('Vault Breach')
  })
})

describe('optional named-bonus outcome copy', () => {
  it('names the target bonus without changing its supplied award count or wager', () => {
    const input = { ...outcomeInput(), bonusName: 'Vault Breach', lastFreeSpinsAwarded: 6, freeSpinsRemaining: 6 }
    const copy = getSlotOutcomePresentation(input)
    expect(copy).toMatchObject({ title: '6 Vault Breach spins won', label: 'Feature unlocked', tone: 'milestone' })
    expect(copy.detail).toContain('6 bonus spins are ready')
    expect(copy.nextAction).toBe('Press Spin to enter Vault Breach.')
    expect(input.activeWager).toBe(5)
    expect(input.freeSpinsRemaining).toBe(6)
  })

  it('keeps default free-game award wording for other titles', () => {
    const copy = getSlotOutcomePresentation({ ...outcomeInput(), lastFreeSpinsAwarded: 6, freeSpinsRemaining: 6 })
    expect(copy.title).toBe('6 free games won')
    expect(copy.detail).toContain('6 free games are ready')
    expect(copy.nextAction).toBe('Press Spin to begin your free games.')
  })

  it('distinguishes named bonus continuation from the unchanged generic continuation', () => {
    const input = { ...outcomeInput(), freeSpinsRemaining: 1, useFreeGameForNextSpin: true }
    expect(getSlotOutcomePresentation({ ...input, bonusName: 'Vault Breach' })).toMatchObject({
      label: 'Vault Breach ready', nextAction: 'Press Spin to continue Vault Breach.',
    })
    expect(getSlotOutcomePresentation(input)).toMatchObject({
      label: 'Free game ready', nextAction: 'Press Spin for the next free game.',
    })
  })
})
