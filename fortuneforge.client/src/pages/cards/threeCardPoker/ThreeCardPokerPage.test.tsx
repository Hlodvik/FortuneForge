import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it, vi } from 'vitest'
import type { ThreeCardPokerPlaySession, ThreeCardPokerSeat, ThreeCardPokerSession, ThreeCardPokerStatus } from '../../../games/cards/threeCardPoker/threeCardPokerApi'
import { validThreeCardPokerWager } from '../../../games/cards/threeCardPoker/threeCardPokerApi'
import { ThreeCardPokerContent, ThreeCardPokerHowToPlay } from './ThreeCardPokerPage'

describe('Three Card Poker table', () => {
  it('uses the same live-table joining and finding flow as Blackjack', () => {
    const idle = render({ contractVersion: 'cards.three-card-poker.table.v1', kind: 'idle', version: 0 })
    expect(idle).toContain('Three Card Poker')
    expect(idle).toContain('Join live table')
    expect(idle).not.toContain('Ante amount')
    const queue = render({ contractVersion: 'cards.three-card-poker.table.v1', kind: 'queue', version: 1, ticketId: 'ticket-1', position: 1, joinedAtUtc: date, humanGraceEndsAtUtc: date, players: [] })
    expect(queue).toContain('Finding table…')
    expect(queue).toContain('Cancel')
  })
  it('reuses shared playing cards and centers the current player seat', () => {
    const markup = render(session)
    expect(markup).toContain('ff-playing-card--face')
    expect(markup).toContain('ff-playing-card--back')
    expect(markup).toContain('blackjack-seat-slot--3 is-current-slot')
    expect(markup).toContain('Ada')
    expect(markup).toContain('Mina')
    expect(markup).toContain('High Card')
    expect(markup).toContain('Play R5.00')
    expect(markup).toContain('>Fold</button>')
    expect(markup).not.toMatch(/\bbot\b|skill|seed|actor/i)
  })
  it('enables only actions advertised by the server for the current turn', () => {
    const markup = render({ ...session, table: { ...session.table, legalActions: ['fold'] } })
    expect(markup).toContain('disabled="">Play R5.00</button>')
    expect(markup).toContain('>Fold</button>')
    const otherTurn = render({ ...session, table: { ...session.table, activeSeat: 1 } })
    expect(otherTurn).toContain('disabled="">Play R5.00</button>')
    expect(otherTurn).toContain('disabled="">Fold</button>')
  })
  it('offers Ante, optional Pair Plus, and sit out on the current wager turn', () => {
    const markup = render({ ...session, table: { ...session.table, phase: 'betting', transition: 'human-wager', wagerDeadlineAtUtc: deadline, legalActions: [] } })
    expect(markup).toContain('Ante amount')
    expect(markup).toContain('Pair Plus amount')
    expect(markup).toContain('>Wager</button>')
    expect(markup).toContain('>Sit out</button>')
    expect(markup).toContain('Play adds an equal ante wager.')
    expect(markup).not.toContain('ff-playing-card--face')
    expect(markup).not.toContain('ff-playing-card--back')
  })
  it('blocks wagers that exceed balance or violate the increment', () => {
    const betting = { ...session, table: { ...session.table, phase: 'betting', transition: 'human-wager', wagerDeadlineAtUtc: deadline } }
    expect(render(betting, { ante: 20, pairPlus: 10, balanceCredits: 25 })).toContain('disabled="">Wager</button>')
    expect(render(betting, { ante: .75 })).toContain('disabled="">Wager</button>')
    expect(validThreeCardPokerWager(0, status, true)).toBe(true)
    expect(validThreeCardPokerWager(0, status)).toBe(false)
    expect(validThreeCardPokerWager(NaN, status, true)).toBe(false)
  })
  it('renders server settlement including independent Pair Plus after a fold', () => {
    const settled = { ...session, table: { ...session.table, phase: 'settled', legalActions: [], activeSeat: null, seats: [{ ...current, lastAction: 'fold', outcome: 'fold', pairPlus: 5, totalWager: 10, payout: 10, pairPlusPayout: 10 }] } }
    const markup = render(settled)
    expect(markup).toContain('Pair Plus R10.00')
    expect(markup).toContain('+R0.00')
    expect(markup).not.toContain('Play R5.00</button>')
  })
  it('shows the next-round countdown on the cleared table', () => {
    const markup = render({ ...session, table: { ...session.table, phase: 'betting', activeSeat: null, legalActions: [], transition: 'next-round-countdown', nextTransitionAtUtc: '2026-10-12T12:00:25Z' } })
    expect(markup).toContain('Next round 5')
    expect(markup).not.toContain('Ante amount')
    expect(markup).not.toContain('ff-card-slot')
  })
  it('explains the three-card ordering, qualification, bonus and fold rules', () => {
    const markup = renderToStaticMarkup(createElement(ThreeCardPokerHowToPlay, { status, defaultOpen: true }))
    expect(markup).toContain('A straight beats a flush')
    expect(markup).toContain(status.dealerRule)
    expect(markup).toContain(status.anteBonusPaytable)
    expect(markup).toContain(status.pairPlusPaytable)
    expect(markup).toContain('It remains eligible when you fold.')
    expect(markup).toContain('bonus when you Play')
  })
})
const date = '2026-10-12T12:00:00Z'
const deadline = '2026-10-12T12:00:30Z'
const status: ThreeCardPokerStatus = { available: true, minimumWager: .5, maximumWager: 100, wagerIncrement: .5, minimumStartOccupancy: 3, tableCapacity: 5, humanGraceSeconds: 5, actionDeadlineSeconds: 30, dealerRule: 'Queen high or better qualifies', pairPlusAllowed: true, anteBonusPaytable: 'Straight 1:1; trips 4:1; straight flush 5:1', pairPlusPaytable: 'Pair 1:1; flush 3:1; straight 6:1; trips 30:1; straight flush 40:1', pairPlusPaysOnFold: true }
const current: ThreeCardPokerSeat = { seatId: 'seat-1', displayName: 'Ada', seat: 0, status: 'playing', ante: 5, pairPlus: 0, playWager: 0, totalWager: 5, payout: 0, anteBonusPayout: 0, pairPlusPayout: 0, hand: { cards: [{ hidden: false, rank: 'Q', suit: 'spades' }, { hidden: false, rank: '6', suit: 'hearts' }, { hidden: false, rank: '4', suit: 'diamonds' }], rank: 'high-card' }, isCurrentPlayer: true }
const session: ThreeCardPokerPlaySession = { contractVersion: 'cards.three-card-poker.table.v1', kind: 'table', version: 8, table: { tableId: 'table-1', phase: 'active', round: 1, dealer: { cards: [{ hidden: true }, { hidden: true }, { hidden: true }], rank: null }, seats: [current, { ...current, seatId: 'seat-2', displayName: 'Mina', seat: 1, isCurrentPlayer: false, hand: { cards: [{ hidden: true }, { hidden: true }, { hidden: true }], rank: null } }], activeSeat: 0, legalActions: ['play', 'fold'], createdAtUtc: date, updatedAtUtc: date, actionDeadlineAtUtc: deadline, wagerDeadlineAtUtc: null, transition: null, nextTransitionAtUtc: null, remainingActionMilliseconds: 10_000, remainingWagerMilliseconds: 0, remainingTransitionMilliseconds: 0 } }
function render(currentSession: ThreeCardPokerSession, overrides: Partial<Parameters<typeof ThreeCardPokerContent>[0]> = {}) {
  return renderToStaticMarkup(createElement(ThreeCardPokerContent, { availability: { kind: 'ready', status, session: currentSession }, balanceCredits: 100, ante: 5, pairPlus: 0, busy: false, pending: null, now: Date.parse('2026-10-12T12:00:20Z'), onAnteChange: vi.fn(), onPairPlusChange: vi.fn(), onJoin: vi.fn(), onCancel: vi.fn(), onWager: vi.fn(), onSitOut: vi.fn(), onAction: vi.fn(), onLeave: vi.fn(), onRefresh: vi.fn(), ...overrides }))
}
