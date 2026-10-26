import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it, vi } from 'vitest'
import type { ThreeCardPokerPlaySession, ThreeCardPokerSeat, ThreeCardPokerSession, ThreeCardPokerStatus } from '../../../games/cards/threeCardPoker/threeCardPokerApi'
import { validThreeCardPokerWager } from '../../../games/cards/threeCardPoker/threeCardPokerApi'
import { ThreeCardPokerContent, ThreeCardPokerHowToPlay } from './ThreeCardPokerPage'

describe('Three Card Poker table', () => {
  it('shows its title artwork only while the initial table snapshot is pending', () => {
    const pending = render(session, { availability: { kind: 'loading' } })
    expect(pending).toContain('data-game-loading="three-card-poker"')
    expect(pending).toContain('Loading Three Card Poker…')
    expect(pending).toContain('role="status"')
    expect(pending).not.toMatch(/<button|role="alert"|ff-playing-card/)
    expect(render(session)).not.toContain('data-game-loading')
  })

  it('preserves the offline error and retry control instead of showing loading artwork', () => {
    const markup = render(session, { availability: { kind: 'error', message: 'The Three Card Poker table could not be reached. No wager was accepted.' } })
    expect(markup).toContain('role="alert"')
    expect(markup).toContain('<h1>The table is offline.</h1>')
    expect(markup).toContain('The Three Card Poker table could not be reached. No wager was accepted.')
    expect(markup).toContain('>Try again</button>')
    expect(markup).not.toContain('data-game-loading')
  })

  it('keeps a disabled table unavailable with its existing refresh control', () => {
    const markup = render(session, { availability: { kind: 'disabled', message: 'The Three Card Poker table is currently unavailable.' } })
    expect(markup).toContain('role="status"')
    expect(markup).toContain('<h1>Three Card Poker is unavailable.</h1>')
    expect(markup).toContain('The Three Card Poker table is currently unavailable.')
    expect(markup).toContain('>Try again</button>')
    expect(markup).not.toMatch(/data-game-loading|role="alert"|aria-label="Deal"/)
  })

  it('starts a private player-versus-dealer table without a multiplayer lobby', () => {
    const idle = render({ contractVersion: 'cards.three-card-poker.table.v1', kind: 'idle', version: 0 })
    expect(idle).toContain('Three Card Poker')
    expect(idle).toContain('>Play</button>')
    expect(idle).not.toMatch(/Join live table|Finding table|other players|open seats/i)
    expect(idle).not.toContain('Place chip on Ante')
    const queue = render({ contractVersion: 'cards.three-card-poker.table.v1', kind: 'queue', version: 1, ticketId: 'ticket-1', position: 1, joinedAtUtc: date, humanGraceEndsAtUtc: date, players: [] })
    expect(queue).toContain('Opening table…')
    expect(queue).toContain('Cancel')
  })
  it('shows only the player and dealer using shared playing cards', () => {
    const markup = render(session)
    expect(markup.match(/ff-playing-card--face/g)).toHaveLength(3)
    expect(markup.match(/ff-playing-card--back/g)).toHaveLength(3)
    expect(markup).toContain('three-card-poker-player')
    expect(markup).not.toContain('Ada')
    expect(markup).not.toMatch(/Mina|Open seat|other players|Finding table/i)
    expect(markup).toContain('High Card')
    expect(playSpot(markup)).toContain('aria-label="Wager R5.00"')
    expect(playSpot(markup)).not.toContain('disabled')
    expect(markup).toContain('aria-label="Fold"')
    expect(markup).toContain('three-card-poker-action-orb--fold')
    expect(markup).not.toMatch(/blackjack-timer|three-card-poker-timer/i)
    expect(markup).not.toMatch(/\bbot\b|skill|seed|actor/i)
  })
  it('enables only actions advertised by the server for the current turn', () => {
    const markup = render({ ...session, table: { ...session.table, legalActions: ['fold'] } })
    expect(playSpot(markup)).toContain('aria-label="Wager R5.00"')
    expect(playSpot(markup)).toContain('disabled=""')
    expect(markup).toContain('aria-label="Fold"')
    const otherTurn = render({ ...session, table: { ...session.table, activeSeat: null, seats: [{ ...current, isCurrentPlayer: false }] } })
    expect(playSpot(otherTurn)).toBeNull()
    expect(otherTurn).toMatch(/aria-label="Fold" disabled=""/)
  })
  it('puts diamond-shaped Ante, optional Pair Plus and Wager spots on the felt', () => {
    const markup = render({ ...session, table: { ...session.table, phase: 'betting', transition: 'human-wager', wagerDeadlineAtUtc: deadline, legalActions: [] } })
    expect(markup).toContain('class="three-card-poker-bets"')
    expect(markup).toContain('Place chip on Ante')
    expect(markup).toContain('Place chip on Pair Plus')
    expect(markup).toContain('Wager spot')
    expect(markup).toContain('<span class="three-card-poker-bet-spot__label">Wager</span>')
    expect(markup).toContain('three-card-poker-bet-spot--ante')
    expect(markup).toContain('three-card-poker-bet-spot--pair-plus')
    expect(markup).toContain('three-card-poker-bet-spot--play')
    expect(markup).toContain('aria-label="Choose chip value, currently R1.00" aria-expanded="false"')
    expect(markup).not.toContain('Select R2.00 chip')
    expect(markup).toContain('aria-label="Deal"')
    expect(markup).toContain('three-card-poker-action-orb--deal')
    expect(markup).not.toMatch(/blackjack-timer|three-card-poker-timer|remaining wager|wager countdown/i)
    expect(markup).not.toContain('ff-playing-card--face')
    expect(markup).not.toContain('ff-playing-card--back')
  })
  it('blocks wagers beyond the balance or R500 spot cap and requires a R1 Ante', () => {
    const betting = { ...session, table: { ...session.table, phase: 'betting', transition: 'human-wager', wagerDeadlineAtUtc: deadline } }
    expect(render(betting, { ante: 20, pairPlus: 10, balanceCredits: 25 })).toMatch(/aria-label="Deal" disabled=""/)
    expect(render(betting, { ante: 501, balanceCredits: 1000 })).toMatch(/aria-label="Deal" disabled=""/)
    expect(render(betting, { ante: 0 })).toMatch(/aria-label="Deal" disabled=""/)
    expect(validThreeCardPokerWager(0, status, true)).toBe(true)
    expect(validThreeCardPokerWager(0, status)).toBe(false)
    expect(validThreeCardPokerWager(NaN, status, true)).toBe(false)
    expect(validThreeCardPokerWager(.75, status)).toBe(false)
    expect(validThreeCardPokerWager(1, status)).toBe(true)
    expect(validThreeCardPokerWager(500, status)).toBe(true)
    expect(validThreeCardPokerWager(501, status)).toBe(false)
  })
  it('renders server settlement including independent Pair Plus after a fold', () => {
    const settled = { ...session, table: { ...session.table, phase: 'settled', legalActions: [], activeSeat: null, seats: [{ ...current, lastAction: 'fold', outcome: 'fold', pairPlus: 5, totalWager: 10, payout: 10, pairPlusPayout: 10 }] } }
    const markup = render(settled)
    expect(markup).toContain('<dt>Pair Plus</dt><dd>R10.00</dd>')
    expect(markup).toContain('+R0.00')
    expect(markup).not.toContain('Wager R5.00</button>')
  })
  it('clears the board between solo hands without a visible countdown', () => {
    const markup = render({ ...session, table: { ...session.table, phase: 'betting', activeSeat: null, legalActions: [], transition: 'next-round-countdown', nextTransitionAtUtc: '2026-10-12T12:00:25Z' } })
    expect(markup).not.toContain('Next round 5')
    expect(markup).not.toContain('Place chip on Ante')
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
const status: ThreeCardPokerStatus = { available: true, minimumWager: 1, maximumWager: 500, wagerIncrement: 1, minimumStartOccupancy: 1, tableCapacity: 1, humanGraceSeconds: 0, actionDeadlineSeconds: 30, dealerRule: 'Queen high or better qualifies', pairPlusAllowed: true, anteBonusPaytable: 'Straight 1:1; trips 4:1; straight flush 5:1', pairPlusPaytable: 'Pair 1:1; flush 3:1; straight 6:1; trips 30:1; straight flush 40:1', pairPlusPaysOnFold: true }
const current: ThreeCardPokerSeat = { seatId: 'seat-1', displayName: 'Ada', seat: 0, status: 'playing', ante: 5, pairPlus: 0, playWager: 0, totalWager: 5, payout: 0, anteBonusPayout: 0, pairPlusPayout: 0, hand: { cards: [{ hidden: false, rank: 'Q', suit: 'spades' }, { hidden: false, rank: '6', suit: 'hearts' }, { hidden: false, rank: '4', suit: 'diamonds' }], rank: 'high-card' }, isCurrentPlayer: true }
const session: ThreeCardPokerPlaySession = { contractVersion: 'cards.three-card-poker.table.v1', kind: 'table', version: 8, table: { tableId: 'table-1', phase: 'active', round: 1, dealer: { cards: [{ hidden: true }, { hidden: true }, { hidden: true }], rank: null }, seats: [current], activeSeat: 0, legalActions: ['play', 'fold'], createdAtUtc: date, updatedAtUtc: date, actionDeadlineAtUtc: deadline, wagerDeadlineAtUtc: null, transition: null, nextTransitionAtUtc: null, remainingActionMilliseconds: 10_000, remainingWagerMilliseconds: 0, remainingTransitionMilliseconds: 0 } }
function render(currentSession: ThreeCardPokerSession, overrides: Partial<Parameters<typeof ThreeCardPokerContent>[0]> = {}) {
  return renderToStaticMarkup(createElement(ThreeCardPokerContent, { availability: { kind: 'ready', status, session: currentSession }, balanceCredits: 100, ante: 5, pairPlus: 0, busy: false, pending: null, onAnteChange: vi.fn(), onPairPlusChange: vi.fn(), onJoin: vi.fn(), onCancel: vi.fn(), onWager: vi.fn(), onSitOut: vi.fn(), onAction: vi.fn(), onLeave: vi.fn(), onRefresh: vi.fn(), ...overrides }))
}
function playSpot(markup: string): string | null {
  return markup.match(/<button[^>]*class="[^"]*three-card-poker-bet-spot--play[^"]*"[^>]*>/)?.[0] ?? null
}
