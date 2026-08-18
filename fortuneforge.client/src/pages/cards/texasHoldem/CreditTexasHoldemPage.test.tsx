/// <reference types="node" />

import { readFileSync } from 'node:fs'
import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import type { CreditHoldemTable } from '../../../games/cards/texasHoldem/creditHoldemApi'
import { CreditHoldemTableSurface, CreditTexasHoldemPreview } from './CreditTexasHoldemPage'

describe('credit Texas Hold’em v2 composition', () => {
  it('lays out the current human at the front and exposes per-seat action state', () => {
    const markup = renderToStaticMarkup(createElement(CreditHoldemTableSurface, {
      table,
      revealDelay: 0,
    }))

    expect(markup).toContain('seat-pos-0 is-current')
    expect(markup).toContain('seat-pos-2')
    expect(markup).toContain('Round R1.00')
    expect(markup).toContain('raise')
    expect(markup).toContain('Pot')
    expect(markup).toContain('Current bet')
    expect(markup).not.toMatch(/buy.?in|refund|claim|skill|seed|actor/i)
  })

  it('marks the winning seat with an animated surround and amount won', () => {
    const settled = {
      ...table,
      status: 'completed',
      activeSeat: -1,
      winningSeatIds: ['seat-one'],
      winningAmount: 950,
      legalActions: [],
    }
    const markup = renderToStaticMarkup(createElement(CreditHoldemTableSurface, {
      table: settled,
      revealDelay: 0,
    }))

    expect(markup).toContain('is-winner')
    expect(markup).toContain('+R9.50')
  })

  it('provides a deterministic actual-DOM thumbnail fixture', () => {
    const first = renderToStaticMarkup(createElement(CreditTexasHoldemPreview))
    const second = renderToStaticMarkup(createElement(CreditTexasHoldemPreview))

    expect(first).toBe(second)
    expect(first).toContain('credit-holdem-preview')
    expect(first).toContain('RiverMoss')
  })

  it('keeps server-validated credit play isolated from practice engines and local optimism', () => {
    const routeSource = source('../../../app/routes/TexasHoldemRoute.tsx')
    const pageSource = source('./CreditTexasHoldemPage.tsx')
    const apiSource = source('../../../games/cards/texasHoldem/creditHoldemApi.ts')

    expect(routeSource).toContain('CreditTexasHoldemPage')
    expect(pageSource).not.toContain('holdemEngine')
    expect(pageSource).not.toContain('botPracticeApi')
    expect(pageSource).not.toContain('optimistic')
    expect(apiSource).not.toContain('/slots')
    expect(apiSource).not.toMatch(/dismissCredit|claimCredit|buyInCredits/)
  })
})

const table: CreditHoldemTable = {
  matchId: 'match-1', status: 'active', street: 'turn', handNumber: 4,
  dealerSeat: 1, activeSeat: 0, pot: 950, currentBet: 200,
  minimumRaiseTo: 400, maximumRaiseTo: 2200, shortAllInRaiseTo: null,
  communityCards: [
    { rank: 'A', suit: 'hearts', hidden: false },
    { rank: '10', suit: 'clubs', hidden: false },
    { rank: '2', suit: 'spades', hidden: false },
    { rank: 'Q', suit: 'diamonds', hidden: false },
  ],
  seats: [
    {
      seatId: 'seat-one', displayName: 'Ada', seat: 0, startingStack: 2500,
      stack: 2200, committed: 300, committedRound: 100, status: 'active', lastAction: 'call',
      holeCards: [
        { rank: 'K', suit: 'hearts', hidden: false },
        { rank: 'Q', suit: 'hearts', hidden: false },
      ], handName: null, isCurrentPlayer: true,
    },
    {
      seatId: 'seat-two', displayName: 'Mina', seat: 1, startingStack: 2500,
      stack: 2100, committed: 400, committedRound: 200, status: 'active', lastAction: 'raise',
      holeCards: [{ hidden: true }, { hidden: true }], handName: null, isCurrentPlayer: false,
    },
    {
      seatId: 'seat-three', displayName: 'Nova', seat: 2, startingStack: 2500,
      stack: 2400, committed: 100, committedRound: 0, status: 'folded', lastAction: 'fold',
      holeCards: [{ hidden: true }, { hidden: true }], handName: null, isCurrentPlayer: false,
    },
  ],
  legalActions: ['fold', 'call', 'raise'], winningSeatIds: [], winningAmount: 0,
  startedAtUtc: '2026-08-16T12:00:00Z', matchDeadlineAtUtc: '2026-08-16T13:00:00Z',
  actionDeadlineAtUtc: '2026-08-16T12:00:30Z', remainingActionMilliseconds: 30000,
}

function source(relativePath: string): string {
  return readFileSync(new URL(relativePath, import.meta.url), 'utf8')
}
