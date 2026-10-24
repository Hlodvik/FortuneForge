// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { BaccaratGame } from './BaccaratGame'
import type { BaccaratGateway, BaccaratRound } from './contracts'

const round: BaccaratRound = {
  roundId: 'mobile-layout-round', balance: 100.25, betSide: 'player', stake: .25, phase: 'settled',
  playerCards: [{ rank: 'nine', suit: 'hearts' }, { rank: 'king', suit: 'clubs' }],
  bankerCards: [{ rank: 'seven', suit: 'diamonds' }, { rank: 'queen', suit: 'spades' }],
  playerTotal: 9, bankerTotal: 7, outcome: 'player', endedOnNatural: true,
  disposition: 'win', profit: .25, totalReturn: .5,
}
const gateway: BaccaratGateway = {
  getStatus: async () => ({ available: true, minimumStake: .25, maximumStake: 100, stakeIncrement: .25, balance: 100, mode: 'test', supportsPairBets: true }),
  createRound: async () => round,
}

afterEach(() => { cleanup(); sessionStorage.clear(); localStorage.clear(); vi.unstubAllGlobals() })

describe('Baccarat mobile-safe board presentation', () => {
  it('keeps all five betting labels and tap targets in normal HTML, not WebKit foreignObject content', async () => {
    vi.stubGlobal('innerWidth', 390)
    vi.stubGlobal('innerHeight', 844)
    render(<BaccaratGame gateway={gateway} />)
    const board = screen.getByRole('group', { name: 'Baccarat betting areas' })
    expect(board.tagName).toBe('DIV')
    expect(board.querySelector('foreignObject')).toBeNull()
    expect(board.querySelectorAll('.ff-baccarat__bet-cell')).toHaveLength(5)
    for (const label of ['Player Pair', 'Banker Pair', 'Tie', 'Banker', 'Player']) {
      const button = screen.getByRole('button', { name: new RegExp(`^${label},`) }) as HTMLButtonElement
      await waitFor(() => expect(button.disabled).toBe(false))
      expect(button.namespaceURI).toBe('http://www.w3.org/1999/xhtml')
      expect(button.querySelector('strong')?.textContent).toBe(label)
    }
    fireEvent.click(screen.getByRole('button', { name: /^Player Pair,/ }))
    fireEvent.click(screen.getByRole('button', { name: /^Player,/ }))
    expect(screen.getByLabelText('Table amounts').textContent).toContain('Bet R0.50')
  })

  it('uses a real centered vector deal/redeal icon rather than platform-dependent recycle glyphs', async () => {
    vi.stubGlobal('matchMedia', vi.fn().mockReturnValue({ matches: true }))
    render(<BaccaratGame gateway={gateway} />)
    const player = screen.getByRole('button', { name: /^Player,/ }) as HTMLButtonElement
    await waitFor(() => expect(player.disabled).toBe(false))
    const deal = screen.getByRole('button', { name: 'Deal' })
    expect(deal.querySelector('svg.ff-baccarat__action-icon')).not.toBeNull()
    expect(deal.querySelector('svg')?.getAttribute('viewBox')).toBe('0 0 24 24')
    fireEvent.click(player)
    fireEvent.click(deal)
    const redeal = await screen.findByRole('button', { name: 'Deal Again' })
    expect(redeal.querySelector('svg.ff-baccarat__action-icon')).not.toBeNull()
  })

  it('uses the supplied shared face renderer for every revealed card without changing unrevealed backs', async () => {
    vi.stubGlobal('matchMedia', vi.fn().mockReturnValue({ matches: true }))
    const renderCardFace = vi.fn(card => <span data-testid="canonical-card-face">{card.rank} of {card.suit}</span>)
    render(<BaccaratGame gateway={gateway} renderCardFace={renderCardFace} />)
    const player = screen.getByRole('button', { name: /^Player,/ }) as HTMLButtonElement
    await waitFor(() => expect(player.disabled).toBe(false))
    expect(renderCardFace).not.toHaveBeenCalled()
    fireEvent.click(player)
    fireEvent.click(screen.getByRole('button', { name: 'Deal' }))
    await screen.findByRole('button', { name: 'Deal Again' })
    expect(screen.getAllByTestId('canonical-card-face')).toHaveLength(4)
    expect(document.querySelectorAll('.ff-baccarat__corner')).toHaveLength(0)
    expect(document.querySelector('.ff-baccarat__shared-card-face')?.textContent).toBe('nine of hearts')
  })
})
