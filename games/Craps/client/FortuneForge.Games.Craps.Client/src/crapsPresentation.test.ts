import { describe, expect, it } from 'vitest'
import {
  availableChipValues, extraBetLabel, extraBetOptions, oddsCopy, pointNumbers,
  resultLabel, rollResultLabel, roundTotals, validStake,
} from './crapsPresentation'
import type { CrapsOutcome, CrapsRound, CrapsStatus } from './contracts'

const status: CrapsStatus = { available: true, minimumStake: 1, maximumStake: 100, stakeIncrement: 1, mode: 'free-play-pass-line' }
const ready: CrapsRound = { roundId: 'practice-hand', stake: 10, phase: 'come-out', point: null, rolls: [], lastOutcome: null, extraBets: [] }

function withOutcome(outcome: CrapsOutcome, point: number | null = null): CrapsRound {
  return {
    ...ready, phase: outcome.isTerminal ? 'resolved' : 'point', point,
    rolls: [{ rollNumber: 1, first: outcome.first, second: outcome.second, total: outcome.total, result: outcome.result }],
    lastOutcome: outcome,
  }
}

describe('Craps wager choices', () => {
  it.each([1, 2, 5, 10, 99, 100])('accepts the entered whole-rand stake %s without a wallet limit', value => {
    expect(validStake(value, status)).toBe(true)
  })

  it.each([0, -1, .5, 1.5, 10.5, 100.01, 101, NaN, Infinity, -Infinity])('rejects invalid draft %s instead of rounding or clamping it', value => {
    expect(validStake(value, status)).toBe(false)
  })

  it('anchors an increment to the minimum and rejects an unaligned maximum', () => {
    const limits = { ...status, minimumStake: 3, maximumStake: 18, stakeIncrement: 4 }
    expect([3, 7, 11, 15].every(value => validStake(value, limits))).toBe(true)
    expect([4, 8, 10, 18].some(value => validStake(value, limits))).toBe(false)
    expect(availableChipValues(limits)).toEqual([3, 15])
  })

  it('handles decimal currency increments without floating-point remainder rejection', () => {
    const limits = { ...status, minimumStake: .1, maximumStake: .7, stakeIncrement: .2 }
    expect(validStake(.3, limits)).toBe(true)
    expect(validStake(.7, limits)).toBe(true)
    expect(validStake(.4, limits)).toBe(false)
    expect(validStake(.301, limits)).toBe(false)
    expect(availableChipValues(limits)).toEqual([.1, .7])
  })

  it.each([
    null,
    { ...status, available: false },
    { ...status, minimumStake: 0 },
    { ...status, minimumStake: 101 },
    { ...status, maximumStake: Infinity },
    { ...status, stakeIncrement: 0 },
    { ...status, stakeIncrement: NaN },
  ])('offers no enabled wagers when limits are unavailable or invalid: %j', limits => {
    expect(validStake(10, limits)).toBe(false)
    expect(availableChipValues(limits)).toEqual([])
  })

  it('offers legal endpoints and familiar denominations without duplicates', () => {
    expect(availableChipValues(status)).toEqual([1, 5, 10, 25, 50, 100])
    expect(availableChipValues({ ...status, minimumStake: 5, maximumStake: 5 })).toEqual([5])
    const limits = { ...status, minimumStake: 2, maximumStake: 12, stakeIncrement: 2 }
    const values = availableChipValues(limits)
    expect(values).toEqual([2, 10, 12])
    expect(values.every(value => validStake(value, limits))).toBe(true)
  })
})

describe('Craps rule descriptions', () => {
  it('describes only the server-supported one-roll bets and coverage', () => {
    expect(extraBetOptions).toEqual([
      { kind: 'field', label: 'Field', coverageText: '2, 3, 4, 9, 10, 11, 12', payout: '1:1 · 2 and 12 pay 2:1' },
      { kind: 'any-seven', label: 'Any Seven', coverageText: '7', payout: '4:1' },
      { kind: 'any-craps', label: 'Any Craps', coverageText: '2, 3, 12', payout: '7:1' },
    ])
    expect(extraBetLabel('odds')).toBe('Pass Odds')
    for (const option of extraBetOptions) expect(extraBetLabel(option.kind)).toBe(option.label)
  })

  it.each([[4, 'Pays 2:1'], [10, 'Pays 2:1'], [5, 'Pays 3:2'], [9, 'Pays 3:2'], [6, 'Pays 6:5'], [8, 'Pays 6:5']] as const)('describes the profit ratio at point %s', (point, copy) => {
    expect(oddsCopy(point)).toBe(copy)
  })

  it('does not describe odds for a come-out or a non-point number', () => {
    expect(pointNumbers).toEqual([4, 5, 6, 8, 9, 10])
    for (const point of [null, 0, 2, 3, 7, 11, 12]) expect(oddsCopy(point)).toBe('')
  })
})

describe('Craps server-return totals', () => {
  it('keeps an unopened come-out stake working rather than reporting it as lost', () => {
    expect(roundTotals(ready)).toEqual({ stake: 10, returned: 0, net: null, workingStake: 10 })
    expect(roundTotals({ ...ready, extraBets: undefined })).toEqual(roundTotals(ready))
  })

  it('shows settled extra returns while the Pass Line and odds remain working', () => {
    const round = withOutcome({ first: 3, second: 3, total: 6, result: 'point-established', isTerminal: false, totalReturn: null }, 6)
    expect(roundTotals({ ...round, extraBets: [
      { kind: 'field', stake: 5, resolved: true, won: false, totalReturn: 0 },
      { kind: 'any-seven', stake: 2, resolved: true, won: false, totalReturn: 0 },
      { kind: 'odds', stake: 1, resolved: false, won: false, totalReturn: null },
    ] })).toEqual({ stake: 18, returned: 0, net: null, workingStake: 11 })

    const pointFour = withOutcome({ first: 2, second: 2, total: 4, result: 'point-established', isTerminal: false, totalReturn: null }, 4)
    expect(roundTotals({ ...pointFour, extraBets: [{ kind: 'field', stake: 5, resolved: true, won: true, totalReturn: 10 }] }))
      .toEqual({ stake: 15, returned: 10, net: null, workingStake: 10 })
  })

  it('accepts R1 odds at point 6 and displays the server R2.20 return', () => {
    expect(validStake(1, status)).toBe(true)
    const round = withOutcome({ first: 3, second: 3, total: 6, result: 'point-hit', isTerminal: true, totalReturn: 20 }, 6)
    expect(roundTotals({ ...round, extraBets: [{ kind: 'odds', stake: 1, resolved: true, won: true, totalReturn: 2.2 }] }))
      .toEqual({ stake: 11, returned: 22.2, net: 11.2, workingStake: 0 })
  })

  it('aggregates all extra stakes and returns even when the Pass Line loses', () => {
    const round = withOutcome({ first: 1, second: 1, total: 2, result: 'craps-loss', isTerminal: true, totalReturn: 0 })
    expect(roundTotals({ ...round, extraBets: [
      { kind: 'field', stake: 5, resolved: true, won: true, totalReturn: 15 },
      { kind: 'any-seven', stake: 2, resolved: true, won: false, totalReturn: 0 },
      { kind: 'any-craps', stake: 3, resolved: true, won: true, totalReturn: 24 },
    ] })).toEqual({ stake: 20, returned: 39, net: 19, workingStake: 0 })
  })

  it('uses server returns without replacing them with locally calculated odds', () => {
    const round = withOutcome({ first: 2, second: 2, total: 4, result: 'point-hit', isTerminal: true, totalReturn: 20 }, 4)
    expect(roundTotals({ ...round, extraBets: [{ kind: 'odds', stake: 1, resolved: true, won: true, totalReturn: 2.2 }] }).returned).toBe(22.2)
  })

  it('reports a completed loss only once no stakes remain working', () => {
    const round = withOutcome({ first: 3, second: 4, total: 7, result: 'seven-out', isTerminal: true, totalReturn: 0 }, 6)
    expect(roundTotals({ ...round, extraBets: [{ kind: 'odds', stake: 1, resolved: true, won: false, totalReturn: 0 }] }))
      .toEqual({ stake: 11, returned: 0, net: -11, workingStake: 0 })
    expect(roundTotals({ ...round, extraBets: [{ kind: 'odds', stake: 1, resolved: false, won: false, totalReturn: null }] }).net).toBeNull()
  })
})

describe('Craps authoritative outcome labels', () => {
  it('describes the table before the first roll', () => {
    expect(resultLabel(null)).toBe('Place your Pass Line bet')
    expect(resultLabel(ready)).toBe('Come-out ready')
  })

  it.each([
    [{ first: 3, second: 4, total: 7, result: 'natural-win', isTerminal: true, totalReturn: 20 }, null, 'Natural 7 · Pass Line wins'],
    [{ first: 1, second: 2, total: 3, result: 'craps-loss', isTerminal: true, totalReturn: 0 }, null, 'Craps 3 · Pass Line loses'],
    [{ first: 3, second: 3, total: 6, result: 'point-established', isTerminal: false, totalReturn: null }, 6, 'Point 6 established'],
    [{ first: 3, second: 3, total: 6, result: 'point-hit', isTerminal: true, totalReturn: 20 }, 6, 'Point 6 made · Pass Line wins'],
    [{ first: 3, second: 4, total: 7, result: 'seven-out', isTerminal: true, totalReturn: 0 }, 6, 'Seven-out · Pass Line loses'],
    [{ first: 2, second: 3, total: 5, result: 'no-decision', isTerminal: false, totalReturn: null }, 6, '5 rolled · Point 6 holds'],
  ] satisfies readonly (readonly [CrapsOutcome, number | null, string])[])('describes %j', (outcome, point, label) => {
    expect(resultLabel(withOutcome(outcome, point))).toBe(label)
  })

  it('preserves the authoritative phase when an outcome is absent', () => {
    expect(resultLabel({ ...ready, phase: 'point', point: 8 })).toBe('Point 8 is on')
    expect(resultLabel({ ...ready, phase: 'resolved' })).toBe('Hand complete')
  })

  it.each([
    ['natural-win', 'Natural'], ['craps-loss', 'Craps'], ['point-established', 'Point established'],
    ['point-hit', 'Point made'], ['seven-out', 'Seven-out'], ['no-decision', 'No decision'], [null, 'Roll'],
  ] as const)('labels older or current roll result %s without inferring it from dice', (result, label) => {
    expect(rollResultLabel(result)).toBe(label)
  })
})
