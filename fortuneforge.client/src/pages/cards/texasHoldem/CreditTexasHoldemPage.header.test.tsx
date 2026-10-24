// @vitest-environment jsdom
import { act, type ReactNode } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { AccountSummary } from '../../../features/account/services/accountsApi'
import type { CreditHoldemTableRule } from '../../../games/cards/texasHoldem/creditHoldemApi'

const state = vi.hoisted(() => ({ clickAudio: vi.fn() }))
vi.mock('../../../games/cards/shared/cardAudio', () => ({ useCardAudioClick: () => state.clickAudio }))
vi.mock('../../../components/InGameShell', () => ({
  InGameShell: ({ children, title }: { children: ReactNode; title: string }) => <div data-game-title={title}>{children}</div>,
}))
vi.mock('../../../games/cards/texasHoldem/creditHoldemApi', async importOriginal => ({
  ...await importOriginal<typeof import('../../../games/cards/texasHoldem/creditHoldemApi')>(),
  getCreditHoldemStatus: async () => ({ available: true, minimumStartPlayers: 3, maximumSeats: 5, minimumRealPlayers: 2, smallBlindCredits: .5, bigBlindCredits: 1, actionDeadlineSeconds: 23, matchDeadlineSeconds: 300, tableRules: [rule] }),
  getCreditHoldemSession: async () => ({ contractVersion: 'cards.texas-holdem.credit.v2', kind: 'idle', version: 1 }),
}))

import { CreditHoldemGameHeader, CreditTexasHoldemPage } from './CreditTexasHoldemPage'

const rule: CreditHoldemTableRule = {
  id: 'friendly', name: 'Friendly', description: 'Automatic blinds', smallBlindCredits: .25,
  bigBlindCredits: .5, anteCredits: .1, maximumTableStackCredits: 25,
}
let host: HTMLDivElement
let root: Root
beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  state.clickAudio.mockClear()
  host = document.createElement('div')
  document.body.append(host)
  root = createRoot(host)
})
afterEach(() => {
  act(() => root.unmount())
  host.remove()
  document.body.style.overflow = ''
  vi.unstubAllGlobals()
})

function button(label: string): HTMLButtonElement {
  const found = host.querySelector<HTMLButtonElement>(`button[aria-label="${label}"]`)
  expect(found).not.toBeNull()
  return found!
}
function renderHeader(muted = false, onToggleSound = vi.fn()) {
  act(() => root.render(<CreditHoldemGameHeader muted={muted} onToggleSound={onToggleSound} tableRules={[rule]} actionDeadlineSeconds={23}/>))
}
function openRules() {
  const trigger = button('How to play')
  trigger.focus()
  act(() => trigger.click())
  return host.querySelector<HTMLDialogElement>('dialog')!
}

describe('credit Hold’em game header', () => {
  it('provides the display title, a labelled sound toggle and compact help control', () => {
    const toggle = vi.fn()
    renderHeader(false, toggle)
    expect(host.querySelector('.ff-holdem-game-header h1')?.textContent).toBe('Texas Hold’em')
    expect(button('Sound on').getAttribute('aria-pressed')).toBe('true')
    act(() => button('Sound on').click())
    expect(toggle).toHaveBeenCalledOnce()
    expect(button('How to play').textContent).toBe('?')
    expect(button('How to play').getAttribute('aria-expanded')).toBe('false')
    renderHeader(true, toggle)
    expect(button('Sound off').getAttribute('aria-pressed')).toBe('false')
  })

  it('explains actual table stakes, no-limit amounts, timeout actions and leaving', () => {
    renderHeader()
    const dialog = openRules()
    expect(dialog.open).toBe(true)
    expect(dialog.getAttribute('aria-modal')).toBe('true')
    expect(document.getElementById(dialog.getAttribute('aria-labelledby')!)?.textContent).toBe('How to play Texas Hold’em')
    expect(dialog.querySelector('tbody tr')?.textContent).toBe('FriendlyR0.25 / R0.50R0.10R0.50R25.00')
    expect(dialog.textContent).toContain('The displayed amount is your total bet, not the extra chips added.')
    expect(dialog.textContent).toContain('You have 23 seconds to act.')
    expect(dialog.textContent).toContain('otherwise it folds your hand')
    expect(dialog.textContent).toContain('does not pause the turn timer')
    expect(dialog.textContent).toContain('even if you are all-in')
    expect(dialog.textContent).toContain('Equal hands split the pot')
    expect(dialog.textContent).toContain('Choose your buy-in before joining.')
    expect(dialog.textContent).toContain('Cancelling the queue returns your reserved buy-in.')
    expect(dialog.textContent).toContain('Next hand keeps your current table chips')
    expect(dialog.textContent).toContain('does not buy more chips or refill from your account')
    expect(dialog.textContent).toContain('Winnings can grow above the maximum buy-in')
    expect(dialog.textContent).toContain('return your unspent table chips to your account')
  })

  it('focuses the dialog, traps Tab in both directions and restores help focus and scrolling on Escape', () => {
    document.body.style.overflow = 'scroll'
    renderHeader()
    const dialog = openRules()
    const close = button('Close how to play')
    const back = Array.from(dialog.querySelectorAll('button')).at(-1)!
    expect(document.activeElement).toBe(close)
    expect(document.body.style.overflow).toBe('hidden')
    act(() => close.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', shiftKey: true, bubbles: true, cancelable: true })))
    expect(document.activeElement).toBe(back)
    act(() => back.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true })))
    expect(document.activeElement).toBe(close)
    act(() => close.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true })))
    expect(host.querySelector('dialog')).toBeNull()
    expect(document.activeElement).toBe(button('How to play'))
    expect(document.body.style.overflow).toBe('scroll')
  })

  it('keeps the dialog open on content clicks and closes on its backdrop', () => {
    renderHeader()
    const dialog = openRules()
    act(() => dialog.querySelector('h2')!.click())
    expect(host.querySelector('dialog')).not.toBeNull()
    act(() => dialog.click())
    expect(host.querySelector('dialog')).toBeNull()
    expect(document.activeElement).toBe(button('How to play'))
  })

  it('handles native dialog cancellation and can reopen without stale focus or scroll state', () => {
    renderHeader()
    const first = openRules()
    const cancel = new Event('cancel', { cancelable: true })
    act(() => first.dispatchEvent(cancel))
    expect(cancel.defaultPrevented).toBe(true)
    expect(host.querySelector('dialog')).toBeNull()
    expect(document.body.style.overflow).toBe('')
    expect(openRules().open).toBe(true)
    act(() => button('Close how to play').click())
    expect(host.querySelector('dialog')).toBeNull()
    expect(document.body.style.overflow).toBe('')
  })

  it('gates game click sounds locally and preserves the shell title used by game reports', async () => {
    await act(async () => root.render(<CreditTexasHoldemPage account={account}/>))
    expect(host.querySelector('[data-game-title]')?.getAttribute('data-game-title')).toBe('Texas Hold’em')
    expect(button('Sound on')).toBeTruthy()
    act(() => button('How to play').click())
    expect(state.clickAudio).toHaveBeenCalledOnce()
    expect(host.querySelector('dialog')?.textContent).toContain('You have 23 seconds to act.')
    act(() => button('Close how to play').click())
    act(() => button('Sound on').click())
    const callsWhenMuted = state.clickAudio.mock.calls.length
    act(() => button('How to play').click())
    act(() => button('Close how to play').click())
    expect(state.clickAudio.mock.calls.length).toBe(callsWhenMuted)
    act(() => button('Sound off').click())
    act(() => button('How to play').click())
    expect(state.clickAudio.mock.calls.length).toBe(callsWhenMuted + 1)
  })
})

const account: AccountSummary = {
  userId: 'header-test', playerName: 'Player', email: 'player@example.test', createdAtUtc: '2026-10-24T00:00:00Z', role: 'player',
  balances: { slotsCredits: 100, freeGames: 0 },
  slots: { spinsPlayed: 0, wins: 0, losses: 0, creditsWagered: 0, creditsWon: 0, netCredits: 0 },
}
