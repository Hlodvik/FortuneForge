import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it, vi } from 'vitest'
import type { BlackjackTableStatus } from '../../../games/cards/blackjack/blackjackTableApi'
import { BlackjackHowToPlay, BlackjackTableContent } from './BlackjackTablePage'

describe('Blackjack table composition', () => {
  it('renders a clean live-table lobby', () => {
    const markup = render({
      kind: 'ready',
      status,
      session: { contractVersion: 'cards.blackjack.table.v2', kind: 'idle', version: 0 },
    })

    expect(markup).not.toContain('Free to join')
    expect(markup).not.toContain('Dealer stands')
    expect(markup).toContain('Join live table')
    expect(markup).not.toContain('Choose your wager at the table')
  })

  it('keeps the finding screen while a queue ticket waits for a table', () => {
    const markup = render({
      kind: 'ready',
      status,
      session: {
        contractVersion: 'cards.blackjack.table.v2',
        kind: 'queue',
        version: 1,
        ticketId: 'ticket-1',
        position: 2,
        joinedAtUtc: '2026-08-15T12:00:00Z',
        humanGraceEndsAtUtc: '2026-08-15T12:00:05Z',
        players: [],
      },
    })

    expect(markup).toContain('Finding table…')
    expect(markup).not.toContain('Checking live tables')
    expect(markup).not.toContain('Queue position')
    expect(markup).not.toContain('Your seat is coming up')
  })

  it('centers the joining state in an arriving player seat without waiting copy', () => {
    const markup = render({
      kind: 'ready',
      status,
      session: {
        ...tableSession,
        table: {
          ...tableSession.table,
          phase: 'betting',
          seats: tableSession.table.seats.map((seat) => seat.isCurrentPlayer ? {
            ...seat,
            status: 'joining-next-round',
            hand: { ...seat.hand, cards: [], score: null },
          } : seat),
        },
      },
    })

    expect(markup).toContain('class="blackjack-seat__joining">Joining next round</span>')
    expect(markup).not.toContain('>Waiting</span>')
    expect(markup).not.toContain('>Joins next round</small>')
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
    expect(markup).not.toContain('60s turn limit')
    expect(markup).not.toContain('Table rules')
    expect(markup).not.toContain('missed turns release')
    expect(markup).not.toContain('Strategy help')
    expect(markup).not.toContain('Next player is thinking')
    expect(markup).not.toContain('Round 1')
    expect(markup).not.toContain('>Playing</small>')
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

  it('clears the completed deal while everybody places the next-round wager', () => {
    const markup = render({
      kind: 'ready',
      status,
      session: {
        ...tableSession,
        table: {
          ...tableSession.table,
          phase: 'betting',
          activeSeat: 0,
          legalActions: [],
          transition: 'human-wager',
          wagerDeadlineAtUtc: '2026-08-15T12:00:40Z',
          seats: tableSession.table.seats.map((seat) => seat.isCurrentPlayer ? {
            ...seat,
            outcome: 'player-win',
            payout: 10,
          } : {
            ...seat,
            outcome: 'dealer-blackjack',
            status: 'completed',
          }),
        },
      },
    })

    expect(markup).not.toContain('ff-card-slot')
    expect(markup).toContain('Dealer')
    expect(markup).toContain('Wager amount')
    expect(markup).toContain('Min R0.50 · Max R100.00')
    expect(markup).toContain('>Wager</button>')
    expect(markup).toContain('>Sit out</button>')
    expect(markup).toContain('>20s</time>')
    expect(markup).not.toContain('Quick chip values')
    expect(markup).toContain('blackjack-balance-bubble')
    expect(markup).toMatch(/blackjack-actions[\s\S]*blackjack-balance-bubble[\s\S]*Wager amount/)
    expect(markup).not.toContain('Round won')
    expect(markup).not.toContain('You won R10.00')
    expect(markup).not.toContain('>Lost</small>')
    expect(markup).not.toContain('Dealer Blackjack')
  })

  it('keeps a settled win out of the next betting phase and does not open an outcome panel', () => {
    const markup = render({
      kind: 'ready',
      status,
      session: {
        ...tableSession,
        table: {
          ...tableSession.table,
          phase: 'betting',
          activeSeat: null,
          legalActions: [],
          seats: tableSession.table.seats.map((seat) => seat.isCurrentPlayer ? {
            ...seat,
            outcome: 'player-blackjack',
            payout: 12.5,
          } : seat),
        },
      },
    })

    expect(markup).not.toContain('+R7.50')
    expect(markup).not.toContain('ff-card-slot')
    expect(markup).not.toContain('Round won')
    expect(markup).not.toContain('You won R12.50')
  })

  it('shows the cleared board countdown before wager turns begin', () => {
    const markup = render({
      kind: 'ready',
      status,
      session: {
        ...tableSession,
        table: {
          ...tableSession.table,
          phase: 'betting',
          activeSeat: null,
          legalActions: [],
          transition: 'next-round-countdown',
          nextTransitionAtUtc: '2026-08-15T12:00:25Z',
        },
      },
    })

    expect(markup).toContain('Next round 5')
    expect(markup).toContain('class="blackjack-next-round"')
    expect(markup).not.toContain('Wager amount')
    expect(markup).not.toContain('ff-card-slot')
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

  it('moves table rules into the how-to-play control', () => {
    const closed = renderToStaticMarkup(createElement(BlackjackHowToPlay, { status }))
    const open = renderToStaticMarkup(createElement(BlackjackHowToPlay, { status, defaultOpen: true }))

    expect(closed).toContain('aria-label="How to play Blackjack"')
    expect(closed).toContain('title="How to play"')
    expect(closed).not.toContain('Dealer stands on all 17s')
    expect(open).toContain('How to play')
    expect(open).toContain('Dealer stands on all 17s')
    expect(open).toContain('60 seconds')
    expect(open).toContain('Two missed turns release your seat')
    expect(open).toContain('<dt>Double</dt><dd>Allowed</dd>')
    expect(open).toContain('<dt>Split</dt><dd>Not available</dd>')
  })
})

function render(availability: Parameters<typeof BlackjackTableContent>[0]['availability']) {
  return renderToStaticMarkup(createElement(BlackjackTableContent, {
    availability,
    balanceCredits: 100,
    balanceChange: null,
    wager: 5,
    busy: false,
    pending: null,
    now: Date.parse('2026-08-15T12:00:20Z'),
    onWagerChange: vi.fn(),
    onJoin: vi.fn(),
    onCancel: vi.fn(),
    onWager: vi.fn(),
    onSitOut: vi.fn(),
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
