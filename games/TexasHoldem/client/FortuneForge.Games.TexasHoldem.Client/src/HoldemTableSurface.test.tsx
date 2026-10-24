// @vitest-environment jsdom
import { act, cleanup, render } from '@testing-library/react'
import { renderToStaticMarkup } from 'react-dom/server'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { arrangeSeats, ChipPile, HoldemTableSurface } from './HoldemTableSurface'
import type { HoldemCard, HoldemSeat, HoldemTable } from './contracts'

const faceUp: HoldemCard = { rank: 'ace', suit: 'spades', hidden: false }
const faceDown: HoldemCard = { rank: null, suit: null, hidden: true }

function seat(number: number, overrides: Partial<HoldemSeat> = {}): HoldemSeat {
  return {
    seatId: `seat-${number}`, displayName: `Player ${number}`, seat: number,
    startingStack: 2500, stack: 2375, committed: 125, committedRound: 125,
    status: 'active', holeCards: [faceDown, faceDown], isCurrentPlayer: false,
    ...overrides,
  }
}

function table(overrides: Partial<HoldemTable> = {}): HoldemTable {
  return {
    matchId: 'match-one', status: 'active', street: 'pre-flop', handNumber: 2,
    dealerSeat: 1, activeSeat: 0, pot: 375, currentBet: 125,
    minimumRaiseTo: 250, maximumRaiseTo: 2500, communityCards: [],
    seats: [seat(0, { isCurrentPlayer: true, holeCards: [faceUp, faceUp] }), seat(1), seat(2)],
    legalActions: ['fold', 'call', 'raise'], winningSeatIds: [], winningAmount: 0,
    startedAtUtc: '2026-10-24T12:00:00Z', matchDeadlineAtUtc: '2026-10-24T12:10:00Z',
    remainingActionMilliseconds: 20000,
    ...overrides,
  }
}

function cardRenderer() {
  return vi.fn((card: HoldemCard, index: number, scope: string) =>
    <span className="injected-shared-card" data-scope={scope} data-index={index}
      data-hidden={String(card.hidden)}>{card.hidden ? 'Shared card back' : `${card.rank} of ${card.suit}`}</span>)
}

function markup(state: HoldemTable) {
  const renderCard = cardRenderer()
  const container = document.createElement('div')
  container.innerHTML = renderToStaticMarkup(<HoldemTableSurface table={state} renderCard={renderCard} />)
  return { container, renderCard }
}

afterEach(() => {
  cleanup()
  vi.useRealTimers()
})

describe('HoldemTableSurface seat placement', () => {
  it('anchors the viewer at the bottom and rotates ascending server seat order without mutating it', () => {
    const seats = Object.freeze([seat(4), seat(1), seat(2, { isCurrentPlayer: true }), seat(0)])
    const arranged = arrangeSeats(seats)
    expect(arranged.map(entry => entry.seat.seat)).toEqual([2, 4, 0, 1])
    expect(arranged.map(entry => entry.position)).toEqual([0, 1, 2, 4])
    expect(seats.map(entry => entry.seat)).toEqual([4, 1, 2, 0])
    expect(arranged[0].seat).toBe(seats[2])
  })

  it.each([
    [1, [0]], [2, [0, 2]], [3, [0, 2, 3]], [4, [0, 1, 2, 4]], [5, [0, 1, 2, 3, 4]],
  ] as const)('renders %i occupied seats once and leaves the remaining slots empty', (count, positions) => {
    const seats = Array.from({ length: count }, (_, index) => seat(index, { isCurrentPlayer: index === 0 }))
    const { container } = markup(table({ seats }))
    const occupied = [...container.querySelectorAll('article.ff-holdem-seat')]
    const empty = [...container.querySelectorAll('.ff-holdem-open-seat')]
    expect(occupied).toHaveLength(count)
    expect(empty).toHaveLength(5 - count)
    expect(occupied.map(element => [...element.classList].find(name => name.startsWith('seat-pos-'))))
      .toEqual(positions.map(position => `seat-pos-${position}`))
    const allPositions = [...occupied, ...empty].map(element => [...element.classList].find(name => name.startsWith('seat-pos-')))
    expect(new Set(allPositions).size).toBe(5)
    expect(empty.every(element => element.getAttribute('aria-label') === 'Open seat')).toBe(true)
  })

  it('handles no seats and a spectator view without inventing a current player', () => {
    expect(arrangeSeats([])).toEqual([])
    expect(markup(table({ seats: [] })).container.querySelectorAll('.ff-holdem-open-seat')).toHaveLength(5)
    const seats = [seat(4), seat(0), seat(2)]
    expect(arrangeSeats(seats).map(entry => entry.seat.seat)).toEqual([4, 0, 2])
    expect(markup(table({ seats })).container.querySelectorAll('.is-current')).toHaveLength(0)
  })
})

describe('HoldemTableSurface server-owned information', () => {
  it.each([false, true])('keeps the viewer hand label inside their seat action area (winner: %s)', winner => {
    const { container } = markup(table({
      status: winner ? 'completed' : 'active',
      winningSeatIds: winner ? ['seat-0'] : [],
      seats: [seat(0, { isCurrentPlayer: true, lastAction: 'call', handName: 'Pair of kings' }),
        seat(1, { handName: 'Private opponent hand' })],
    }))
    const label = container.querySelector('.ff-holdem-seat-hand')
    const ownSeat = container.querySelector('.ff-holdem-seat.is-current')

    expect(container.querySelectorAll('.ff-holdem-seat-hand')).toHaveLength(1)
    expect(label?.textContent).toBe('Pair of kings')
    expect(label?.parentElement?.className).toBe('ff-holdem-seat-action')
    expect(label?.closest('article')).toBe(ownSeat)
    expect(ownSeat?.querySelector('.ff-holdem-seat-plaque')?.textContent).not.toContain('Pair of kings')
    expect(ownSeat?.querySelector('.ff-holdem-seat-action strong')?.textContent).toBe(winner ? 'Winner' : undefined)
    expect(container.querySelector('.ff-holdem-hand-name')).toBeNull()
    expect(container.textContent).not.toContain('Private opponent hand')
  })

  it('passes only the cards actually supplied by the server to the injected shared renderer', () => {
    const ownCards: HoldemCard[] = [{ rank: 'king', suit: 'hearts', hidden: false }, faceUp]
    const opponentCards: HoldemCard[] = [{ hidden: true }, faceDown]
    const board: HoldemCard[] = [faceUp, { rank: 'two', suit: 'clubs', hidden: false }, faceDown]
    const state = table({
      communityCards: board,
      seats: [seat(0, { isCurrentPlayer: true, holeCards: ownCards, handName: 'Pair of kings' }),
        seat(1, { holeCards: opponentCards, handName: 'Private opponent hand' }), seat(2, { holeCards: [] })],
    })
    const { container, renderCard } = markup(state)
    expect(renderCard).toHaveBeenCalledTimes(board.length + ownCards.length + opponentCards.length)
    const expected = [...board, ...ownCards, ...opponentCards]
    renderCard.mock.calls.forEach(([card], index) => expect(card).toBe(expected[index]))
    board.forEach((card, index) => expect(renderCard).toHaveBeenCalledWith(card, index, 'match-one:2-board'))
    ownCards.forEach((card, index) => expect(renderCard).toHaveBeenCalledWith(card, index, 'seat-0'))
    opponentCards.forEach((card, index) => expect(renderCard).toHaveBeenCalledWith(card, index, 'seat-1'))
    expect(container.querySelectorAll('.injected-shared-card')).toHaveLength(7)
    expect(container.querySelectorAll('.ff-holdem-card-empty')).toHaveLength(2)
    expect(container.textContent).toContain('Pair of kings')
    expect(container.textContent).not.toContain('Private opponent hand')
    expect(container.querySelector('[aria-label="Player 1’s cards"]')?.textContent).toBe('Shared card backShared card back')
    expect(container.querySelector('[aria-label="Player 2’s cards"]')?.textContent).toBe('')
  })

  it('does not reveal hidden hole cards when the server marks a hand complete', () => {
    const { container, renderCard } = markup(table({ status: 'completed', winningSeatIds: ['seat-1'] }))
    const hiddenCalls = renderCard.mock.calls.filter(([card]) => card.hidden)
    expect(hiddenCalls).toHaveLength(4)
    expect(container.querySelector('[aria-label="Player 1’s cards"]')?.textContent).toBe('Shared card backShared card back')
    expect(container.querySelector('[aria-label="Player 2’s cards"]')?.textContent).toBe('Shared card backShared card back')
  })

  it('uses server winner IDs only and never attributes the aggregate award to every winner', () => {
    const { container } = markup(table({
      status: 'completed', pot: 12345, winningSeatIds: ['seat-0', 'seat-2'], winningAmount: 987654,
      seats: [seat(0, { isCurrentPlayer: true }), seat(1), seat(2)],
    }))
    const seats = [...container.querySelectorAll('article.ff-holdem-seat')]
    expect(seats.map(element => element.classList.contains('is-winner'))).toEqual([true, false, true])
    expect(container.querySelectorAll('.ff-holdem-seat-action strong')).toHaveLength(2)
    expect(container.textContent).not.toContain('9876.54')
    expect(container.textContent).not.toContain('+R')
  })
})

describe('HoldemTableSurface amounts and states', () => {
  it('labels the viewer’s remaining table chips without expanding opponent amounts or treating it as a wallet balance', () => {
    const { container } = markup(table({
      seats: [seat(0, { isCurrentPlayer: true, stack: 8250 }), seat(1, { stack: 7950 })],
    }))
    const ownAmount = container.querySelector('.is-current .ff-holdem-seat-identity span')
    const opponentAmount = container.querySelector('.ff-holdem-seat:not(.is-current) .ff-holdem-seat-identity span')

    expect(ownAmount?.textContent).toBe('Table chips R82.50')
    expect(ownAmount?.getAttribute('aria-label')).toBe('Your remaining table chips R82.50')
    expect(ownAmount?.getAttribute('title')).toBe('Your remaining table chips, not your account balance.')
    expect(opponentAmount?.textContent).toBe('R79.50')
    expect(opponentAmount?.getAttribute('aria-label')).toBe('Player 1’s remaining table chips R79.50')
  })

  it('formats table chip fields as cents and table rule stakes as decimal credits', () => {
    const { container } = markup(table({
      pot: 12345,
      seats: [seat(0, { isCurrentPlayer: true, stack: 98765, committedRound: 125 }), seat(1, { committedRound: 0 })],
      tableRule: { id: 'test', name: 'Test stakes', description: '', smallBlindCredits: 0.25,
        bigBlindCredits: 0.5, anteCredits: 0, maximumTableStackCredits: 1000 },
    }))
    expect(container.querySelector('.ff-holdem-pot')?.getAttribute('aria-label')).toBe('Total pot R123.45')
    expect(container.querySelector('.ff-holdem-seat.is-current .ff-holdem-seat-identity')?.textContent).toContain('R987.65')
    expect(container.querySelector('.ff-holdem-seat-bet')?.getAttribute('aria-label')).toBe('Player 0 bet R1.25')
    expect(container.querySelectorAll('.ff-holdem-seat-bet')).toHaveLength(1)
    expect(container.querySelector('.ff-holdem-table-meta')?.textContent).toContain('R0.25 / R0.50')
  })

  it('marks dealer, current player, active player, folded player, and all-in state independently', () => {
    const { container } = markup(table({
      activeSeat: 2, dealerSeat: 0,
      seats: [seat(0, { isCurrentPlayer: true, lastAction: 'call' }),
        seat(1, { status: 'folded', lastAction: 'fold' }), seat(2, { status: 'all-in' })],
    }))
    expect(container.querySelector('.is-current [aria-label="Dealer"]')?.textContent).toBe('D')
    expect(container.querySelectorAll('[aria-label="Dealer"]')).toHaveLength(1)
    expect(container.querySelector('.is-active')?.getAttribute('aria-label')).toBe('Player 2, to act')
    expect(container.querySelector('.is-folded .ff-holdem-seat-action')?.textContent).toBe('fold')
    expect(container.querySelector('.is-active .ff-holdem-seat-action')?.textContent).toBe('all in')
    expect(container.querySelector('[role="status"]')?.textContent).toBe('Player 2 to act')
    expect(container.querySelector('.is-current')?.classList.contains('is-active')).toBe(false)
  })

  it('announces the viewer turn and removes active turn styling after completion', () => {
    const active = markup(table()).container
    expect(active.querySelector('[role="status"]')?.textContent).toBe('Your turn')
    expect(active.querySelector('[role="status"]')?.classList.contains('is-your-turn')).toBe(true)
    expect(active.querySelector('.is-current')?.classList.contains('is-active')).toBe(true)
    const completed = markup(table({ status: 'completed' })).container
    expect(completed.querySelector('[role="status"]')?.textContent).toBe('Showdown')
    expect(completed.querySelectorAll('.is-active, .is-your-turn')).toHaveLength(0)
    expect(completed.querySelector('[aria-label="Player 0, you, to act"]')).toBeNull()
  })

  it('renders a bounded decorative chip stack without replacing exact money labels', () => {
    const container = document.createElement('div')
    container.innerHTML = renderToStaticMarkup(<ChipPile amount={1000000} />)
    const pile = container.querySelector('.ff-holdem-chip-pile')
    expect(pile?.getAttribute('aria-hidden')).toBe('true')
    const chips = [...container.querySelectorAll('img')]
    expect(chips).toHaveLength(4)
    expect(chips.map(chip => chip.style.transform)).toEqual(['translateY(0px)', 'translateY(-3px)', 'translateY(-6px)', 'translateY(-9px)'])
    expect(chips.every(chip => chip.alt === '' && chip.draggable === false)).toBe(true)
    expect(renderToStaticMarkup(<ChipPile amount={0} />)).not.toContain('<img')
  })
})

describe('HoldemTableSurface community reveal', () => {
  it('reveals newly received community cards in sequence and clears the previous hand before revealing a new one', () => {
    vi.useFakeTimers()
    const renderCard = cardRenderer()
    const state = table()
    const { container, rerender } = render(<HoldemTableSurface table={state} renderCard={renderCard} revealDelay={150} />)
    const visibleBoardCards = () => container.querySelectorAll('.ff-holdem-community .injected-shared-card').length
    expect(visibleBoardCards()).toBe(0)
    rerender(<HoldemTableSurface table={{ ...state, communityCards: [faceUp, faceUp, faceUp] }} renderCard={renderCard} revealDelay={150} />)
    expect(visibleBoardCards()).toBe(0)
    for (let count = 1; count <= 3; count++) {
      act(() => vi.advanceTimersByTime(150))
      expect(visibleBoardCards()).toBe(count)
    }
    rerender(<HoldemTableSurface table={{ ...state, handNumber: 3, communityCards: [faceDown] }} renderCard={renderCard} revealDelay={150} />)
    expect(visibleBoardCards()).toBe(0)
    expect(container.querySelectorAll('.ff-holdem-card-empty')).toHaveLength(5)
    act(() => vi.advanceTimersByTime(150))
    expect(visibleBoardCards()).toBe(1)
    expect(container.querySelector('.ff-holdem-community .injected-shared-card')?.getAttribute('data-scope')).toBe('match-one:3-board')
  })
})
