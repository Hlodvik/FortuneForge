import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it, vi } from 'vitest'
import type { HouseHoldemRound, HouseHoldemStatus } from '../../../games/cards/houseHoldem/houseHoldemApi'
import { HouseHoldemContent, HouseHoldemHowToPlay } from './HouseHoldemPage'

const status: HouseHoldemStatus = { available: true, balance: 100, activeRoundId: null, minimumAnte: 1, maximumAnte: 100, wagerIncrement: 1, maximumTrips: 500 }
const card = (rank: string, suit: 'spades' | 'diamonds' | 'clubs' | 'hearts') => ({ rank, suit })
const round: HouseHoldemRound = {
  roundId: 'round-1', balance: 98, phase: 'preflop', ante: 1, blind: 1, trips: 0, play: 0, totalWager: 2,
  payout: 0, playerCards: [card('ace', 'spades'), card('king', 'hearts')], dealerCards: null, board: [],
  playerHand: null, dealerHand: null, result: null, allowedActions: ['check', 'play-3x', 'play-4x'], canAdvance: false,
  payoutBreakdown: null,
}
function render(current: HouseHoldemRound | null, props: Partial<Parameters<typeof HouseHoldemContent>[0]> = {}) {
  return renderToStaticMarkup(createElement(HouseHoldemContent, {
    loadState: { kind: 'ready', status }, round: current, balance: 100, ante: 1, trips: 0, placements: [],
    selectedChip: 1, chipPickerOpen: false, busy: false, pending: null, onPlace: vi.fn(), onUndo: vi.fn(),
    dealPresentation: null, onDismissResult: vi.fn(),
    onDeal: vi.fn(), onAct: vi.fn(), onSelectChip: vi.fn(), onTogglePicker: vi.fn(), onRefresh: vi.fn(), ...props,
  }))
}

describe('House Hold’em table', () => {
  it('shows its title artwork only while the initial table snapshot is pending', () => {
    const pending = render(null, { loadState: { kind: 'loading' } })
    expect(pending).toContain('data-game-loading="house-holdem"')
    expect(pending).toContain('Loading House Hold’em…')
    expect(pending).toContain('role="status"')
    expect(pending).not.toMatch(/<button|role="alert"|ff-playing-card/)
    expect(render(null)).not.toContain('data-game-loading')
  })

  it('preserves the offline error and retry control instead of showing loading artwork', () => {
    const markup = render(null, { loadState: { kind: 'error', message: 'The table could not be reached. No new wager was accepted.' } })
    expect(markup).toContain('role="alert"')
    expect(markup).toContain('<h1>The table is offline.</h1>')
    expect(markup).toContain('The table could not be reached. No new wager was accepted.')
    expect(markup).toContain('>Try again</button>')
    expect(markup).not.toContain('data-game-loading')
  })

  it('keeps a disabled table unavailable with its existing refresh control', () => {
    const markup = render(null, { loadState: { kind: 'disabled', message: 'The table is unavailable.' } })
    expect(markup).toContain('role="status"')
    expect(markup).toContain('<h1>The table is unavailable.</h1>')
    expect(markup).toContain('<p>The table is unavailable.</p>')
    expect(markup).toContain('>Try again</button>')
    expect(markup).not.toMatch(/data-game-loading|role="alert"|aria-label="Deal"/)
  })

  it('keeps chip betting on the felt and does not introduce a multiplayer queue', () => {
    const markup = render(null)
    expect(markup).toContain('Place chip on Ante')
    expect(markup).toContain('Place chip on Trips')
    expect(markup).toContain('Blind wager')
    expect(markup).toContain('Wager spot')
    expect(markup).toContain('aria-label="Deal"')
    expect(markup).toContain('aria-label="Choose chip value, currently R1.00" aria-expanded="false"')
    expect(markup).not.toMatch(/queue|other players|open seats/i)
    expect(markup).toContain('MAX ANTE R100')
  })

  it('marks Trips as empty until a chip is placed', () => {
    expect(render(null, { trips: 0, placements: [] })).toMatch(/house-holdem-bet-spot--trips[^>]*data-empty="true"/)
    expect(render(null, { trips: 1, placements: [{ spot: 'trips', value: 1 }] })).toMatch(/house-holdem-bet-spot--trips[^>]*data-empty="false"/)
  })

  it('explains every betting spot through focusable labels, including non-clickable spots', () => {
    const markup = render(null)
    for (const label of ['Trips', 'Ante', 'Blind', 'Wager']) {
      expect(markup).toContain(`aria-label="About ${label}"`)
    }
    expect(markup).toContain('Optional side bet on your final five-card hand')
    expect(markup).toContain('Your required opening bet against the dealer')
    expect(markup).toContain('Automatically matches your Ante')
    expect(markup).toContain('3× or 4× Ante before the flop')
    expect(markup.match(/role="tooltip"/g)).toHaveLength(4)
  })

  it('does not deal if the balance cannot also cover a final 1× Play', () => {
    expect(render(null, { balance: 2, ante: 1 })).toMatch(/aria-label="Deal" disabled=""/)
    expect(render(null, { balance: 3, ante: 1 })).not.toMatch(/aria-label="Deal" disabled=""/)
    expect(render(null, { balance: 3, ante: 1, trips: 1 })).toMatch(/aria-label="Deal" disabled=""/)
  })

  it('shows only the player cards while concealing both dealer cards', () => {
    const markup = render(round)
    expect(markup.match(/ff-playing-card--face/g)).toHaveLength(2)
    expect(markup.match(/ff-playing-card--back/g)).toHaveLength(2)
    expect(markup.match(/three-card-poker-card-outline/g)).toHaveLength(5)
    expect(markup).toContain('aria-label="Wager 4 times your Ante"')
    expect(markup).toContain('aria-label="Check"')
    expect(markup).not.toContain('aria-label="Fold"')
  })

  it('deals both dealer cards face down before both player cards, then reveals the player hand', () => {
    for (const [step, dealerBacks, playerBacks] of [[0, 0, 0], [1, 1, 0], [2, 2, 0], [3, 2, 1], [4, 2, 2]] as const) {
      const markup = render({ ...round, playerHand: 'secret-hand' }, { dealPresentation: { roundId: round.roundId, step } })
      const dealerMarkup = markup.split('class="blackjack-dealer">')[1]?.split('class="house-holdem-board"')[0] ?? ''
      const playerMarkup = markup.split('class="three-card-poker-player"')[1]?.split('class="three-card-poker-bets"')[0] ?? ''
      expect(markup).toContain('data-phase="dealing"')
      expect(dealerMarkup.match(/ff-playing-card--back/g) ?? []).toHaveLength(dealerBacks)
      expect(playerMarkup.match(/ff-playing-card--back/g) ?? []).toHaveLength(playerBacks)
      expect(markup).not.toContain('ff-playing-card--face')
      expect(markup).not.toContain('Secret Hand')
      expect(markup).not.toContain('aria-label="Check"')
    }
    const revealed = render(round)
    expect(revealed.match(/ff-playing-card--face/g)).toHaveLength(2)
    expect(revealed.match(/ff-playing-card--back/g)).toHaveLength(2)
  })

  it('disables unaffordable Play choices but leaves Check and Fold available', () => {
    const preflop = render({ ...round, balance: 2 })
    expect(preflop).toMatch(/aria-label="Wager 3 times your Ante" disabled=""/)
    expect(preflop).toMatch(/aria-label="Wager 4 times your Ante" disabled=""/)
    expect(preflop).not.toMatch(/aria-label="Check" disabled=""/)
    const river = render({ ...round, phase: 'river', balance: 0, board: [card('two', 'hearts'), card('three', 'clubs'), card('four', 'diamonds'), card('five', 'spades'), card('six', 'spades')], allowedActions: ['play-1x', 'fold'] })
    expect(river).toMatch(/aria-label="Wager 1 time your Ante" disabled=""/)
    expect(river).not.toMatch(/aria-label="Fold" disabled=""/)
  })

  it('reveals only the public flop, then all five community cards at the river', () => {
    const flop = render({ ...round, phase: 'flop', board: [card('2', 'hearts'), card('3', 'clubs'), card('4', 'diamonds')], allowedActions: ['check', 'play-2x'] })
    expect(flop.match(/ff-playing-card--face/g)).toHaveLength(5)
    expect(flop.match(/three-card-poker-card-outline/g)).toHaveLength(2)
    expect(flop).not.toContain('aria-label="Fold"')
    const river = render({ ...round, phase: 'river', board: [card('2', 'hearts'), card('3', 'clubs'), card('4', 'diamonds'), card('5', 'spades'), card('6', 'spades')], allowedActions: ['play-1x', 'fold'] })
    expect(river.match(/ff-playing-card--face/g)).toHaveLength(7)
    expect(river).toContain('aria-label="Fold"')
  })

  it('renders an itemized result and all cards only on settlement', () => {
    const markup = render({ ...round, phase: 'settled', dealerCards: [card('queen', 'spades'), card('queen', 'diamonds')], board: [card('2', 'hearts'), card('3', 'clubs'), card('4', 'diamonds'), card('5', 'spades'), card('6', 'spades')], allowedActions: [], payout: 10, totalWager: 2, payoutBreakdown: { ante: 2, blind: 2, trips: 0, play: 6 } })
    expect(markup.match(/ff-playing-card--face/g)).toHaveLength(9)
    expect(markup).toContain('Round result')
    expect(markup).toContain('aria-label="Dismiss result"')
    expect(markup).toContain('+R8.00')
    expect(markup).toContain('<dt>Blind</dt><dd>R2.00</dd>')
    expect(markup).toContain('aria-label="Deal"')
  })

  it('shows an empty betting layout after a settled result is dismissed', () => {
    const markup = render(null, { ante: 0, trips: 0, placements: [] })
    expect(markup).not.toContain('Round result')
    expect(markup).not.toContain('ff-playing-card--face')
    expect(markup.match(/three-card-poker-card-outline/g)).toHaveLength(9)
    expect(markup).toContain('aria-label="Deal" disabled=""')
    expect(markup).toContain('Bet <strong>R0.00</strong>')
  })

  it('clears the prior hand visually while a new Deal request is pending', () => {
    const settled = { ...round, phase: 'settled' as const, dealerCards: [card('queen', 'spades'), card('queen', 'diamonds')], board: [card('2', 'hearts'), card('3', 'clubs'), card('4', 'diamonds'), card('5', 'spades'), card('6', 'spades')], allowedActions: [], payout: 10, totalWager: 2, payoutBreakdown: { ante: 2, blind: 2, trips: 0, play: 6 } }
    const markup = render(settled, { pending: { fingerprint: 'deal:1:0:round-1', key: 'request-key' }, busy: true })
    expect(markup).toContain('data-phase="dealing"')
    expect(markup).not.toContain('ff-playing-card--face')
    expect(markup).not.toContain('Round result')
    expect(markup.match(/three-card-poker-card-outline/g)).toHaveLength(9)
  })

  it('explains the distinct decision stages and matching Blind', () => {
    const markup = renderToStaticMarkup(createElement(HouseHoldemHowToPlay, { status, defaultOpen: true }))
    expect(markup).toContain('matching Blind')
    expect(markup).toContain('3× or 4×')
    expect(markup).toContain('2×')
    expect(markup).toContain('1×')
    for (const odds of ['500:1', '50:1', '10:1', '3:2', '40:1', '30:1', '9:1', '7:1', '4:1']) expect(markup).toContain(odds)
    expect(markup).toContain('Based on your final hand, even if you fold')
  })
})
