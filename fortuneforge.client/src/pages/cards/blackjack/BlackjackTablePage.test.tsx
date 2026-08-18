import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it, vi } from 'vitest'
import type { BlackjackTableStatus } from '../../../games/cards/blackjack/blackjackTableApi'
import { BlackjackTableContent } from './BlackjackTablePage'

describe('Blackjack table composition', () => {
  it('renders a free five-seat lobby', () => {
    const markup = render({
      kind: 'ready',
      status,
      session: { contractVersion: 'cards.blackjack.table.v2', kind: 'idle', version: 0 },
    })

    expect(markup).toContain('Free to join')
    expect(markup).toContain('5 seats')
    expect(markup).toContain('Join live table')
    expect(markup).not.toContain('Join for R')
  })

  it('renders ordinary seats and enables only server legal actions', () => {
    const markup = render({ kind: 'ready', status, session: tableSession })

    expect(markup).toContain('>Ada</strong>')
    expect(markup).not.toContain('>You</strong>')
    expect(markup).toContain('Mina')
    expect(markup).toContain('class="blackjack-seat__timer"')
    expect(markup).toContain('>10s</time>')
    expect(markup).toContain('blackjack-seat-slot--3 is-current-slot')
    expect(markup).toContain('class="blackjack-hand"')
    expect(markup).toContain('blackjack-seat is-current is-active')
    expect(markup).toContain('>Hit</button>')
    expect(markup).toContain('>Stand</button>')
    expect(markup).toContain('aria-label="Hand total 11"')
    expect(markup).toContain('disabled="">Double</button>')
    expect(markup).toContain('disabled="">Split</button>')
    expect(markup).toContain('disabled="">Surrender</button>')
    expect(markup).toContain('Open seat')
    expect(markup).toContain('Leave table')
    expect(markup).not.toMatch(/\bbot\b|skill|seed|actor/i)
  })

  it('makes unknown future table phases nonactionable', () => {
    const markup = render({
      kind: 'ready',
      status,
      session: { ...tableSession, table: { ...tableSession.table, phase: 'future-paused' } },
    })

    expect(markup).not.toContain('>Hit</button>')
    expect(markup).not.toContain('>Stand</button>')
    expect(markup).toContain('>Leave table</button>')
  })

  it('clears the completed deal before showing next-round wager controls', () => {
    const markup = render({
      kind: 'ready',
      status,
      session: { ...tableSession, table: { ...tableSession.table, phase: 'betting', activeSeat: null, legalActions: [] } },
    })

    expect(markup).toContain('blackjack-dealer__idle')
    expect(markup).not.toContain('ff-card-slot')
    expect(markup).toContain('Round wager')
  })

  it('renders server-projected split hands and only the current player timer', () => {
    const splitHand = tableSession.table.seats[0].hand
    const splitSession = {
      ...tableSession,
      table: {
        ...tableSession.table,
        legalActions: ['hit', 'stand', 'double', 'surrender'] as const,
        seats: tableSession.table.seats.map((seat, index) => index === 0 ? {
          ...seat,
          hands: [
            { handNumber: 1, hand: splitHand, wager: 5, totalWager: 5, payout: 0, status: 'stood', outcome: null, lastAction: 'stand', active: false },
            { handNumber: 2, hand: splitHand, wager: 5, totalWager: 5, payout: 0, status: 'playing', outcome: null, lastAction: null, active: true },
          ],
          insuranceWager: 2.5,
          insurancePayout: 0,
        } : seat),
      },
    } as const

    const markup = render({ kind: 'ready', status, session: splitSession })

    expect(markup).toContain('Hand 1')
    expect(markup).toContain('Hand 2')
    expect(markup).toContain('Insurance R2.50')
    expect(markup.match(/blackjack-seat__timer/g)).toHaveLength(1)
    expect(markup).toContain('>Surrender</button>')
  })
})

function render(availability: Parameters<typeof BlackjackTableContent>[0]['availability']) {
  return renderToStaticMarkup(createElement(BlackjackTableContent, {
    availability,
    balanceCredits: 100,
    wager: 5,
    busy: false,
    pending: null,
    now: Date.parse('2026-08-15T12:00:20Z'),
    onWagerChange: vi.fn(),
    onJoin: vi.fn(),
    onCancel: vi.fn(),
    onWager: vi.fn(),
    onAction: vi.fn(),
    onLeave: vi.fn(),
    onRefresh: vi.fn(),
  }))
}

const status: BlackjackTableStatus = {
  available: true,
  minimumWager: 0.5,
  maximumWager: 100,
  wagerIncrement: 0.5,
  minimumStartOccupancy: 3,
  tableCapacity: 5,
  humanGraceSeconds: 5,
  actionDeadlineSeconds: 60,
  dealerRule: 'Dealer stands on all 17s',
  blackjackPayout: '3:2',
  doubleAllowed: true,
  splitAllowed: false,
  insuranceAllowed: false,
}

const tableSession = {
  contractVersion: 'cards.blackjack.table.v2',
  kind: 'table',
  version: 4,
  table: {
    tableId: 'table-1', phase: 'active', round: 1,
    dealer: { cards: [{ rank: '9', suit: 'clubs', hidden: false }, { hidden: true }], score: null, soft: false, blackjack: false, bust: false },
    seats: [{
      seatId: 'seat-1', displayName: 'Ada', seat: 0, status: 'playing', wager: 5,
      totalWager: 5, payout: 0, outcome: null, lastAction: null,
      hand: { cards: [{ rank: 'A', suit: 'spades', hidden: false }], score: 11, soft: true, blackjack: false, bust: false },
      isCurrentPlayer: true,
    }, {
      seatId: 'seat-2', displayName: 'Mina', seat: 1, status: 'stood', wager: 5,
      totalWager: 5, payout: 0, outcome: null, lastAction: 'stand',
      hand: { cards: [], score: null, soft: false, blackjack: false, bust: false },
      isCurrentPlayer: false,
    }],
    activeSeat: 0, legalActions: ['hit', 'stand'],
    createdAtUtc: '2026-08-15T12:00:00Z', updatedAtUtc: '2026-08-15T12:00:10Z',
    actionDeadlineAtUtc: '2026-08-15T12:00:30Z', wagerDeadlineAtUtc: null,
    transition: null, nextTransitionAtUtc: null,
    remainingActionMilliseconds: 20_000, remainingWagerMilliseconds: 0,
    remainingTransitionMilliseconds: 0,
  },
} as const
