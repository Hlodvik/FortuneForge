import { describe, expect, it } from 'vitest'
import type { RouletteBetKind, RouletteRound, RouletteStatus } from './contracts'
import { availableChipValues, betOptions, coveredPockets, europeanWheelOrder, formatBetLabel, legalSelection, makeBet, neighborPockets, roundTotals, totalReturn, totalStake, validStake } from './roulettePresentation'

const status: RouletteStatus = { available: true, minimumStake: 1, maximumStake: 100, stakeIncrement: 1, startingBalance: 1_000, mode: 'free-play-single-zero' }
const outsideKinds = ['red', 'black', 'even', 'odd', 'low', 'high'] as const

describe('Roulette presentation contracts', () => {
  it.each([
    ['straight', [0]], ['straight', [36]],
    ['split', [0, 1]], ['split', [0, 2]], ['split', [0, 3]], ['split', [2, 1]], ['split', [36, 33]],
    ['street', [0, 1, 2]], ['street', [0, 2, 3]], ['street', [36, 34, 35]],
    ['corner', [5, 1, 4, 2]], ['corner', [32, 33, 35, 36]],
    ['six-line', [6, 3, 1, 5, 2, 4]], ['six-line', [31, 32, 33, 34, 35, 36]],
    ['column', [1]], ['column', [3]], ['dozen', [1]], ['dozen', [3]],
  ] satisfies [RouletteBetKind, number[]][])('accepts supported %s geometry %j regardless of selection order', (kind, values) => {
    expect(legalSelection(kind, values)).toBe(true)
  })

  it.each([
    ['straight', []], ['straight', [37]], ['straight', [-1]], ['straight', [1.5]], ['straight', [NaN]],
    ['split', [1, 1]], ['split', [3, 4]], ['split', [0, 4]], ['split', [1, 5]],
    ['street', [0, 1, 3]], ['street', [2, 3, 4]], ['street', [1, 2, 4]],
    ['corner', [0, 1, 3, 4]], ['corner', [2, 3, 4, 5]], ['corner', [1, 2, 7, 8]],
    ['six-line', [0, 1, 2, 3, 4, 5]], ['six-line', [2, 3, 4, 5, 6, 7]], ['six-line', [1, 2, 3, 7, 8, 9]],
    ['column', [0]], ['column', [4]], ['dozen', [0]], ['dozen', [1, 2]], ['red', [17]],
  ] satisfies [RouletteBetKind, number[]][])('rejects invalid %s geometry %j before a chip request', (kind, values) => {
    expect(legalSelection(kind, values)).toBe(false)
    expect(() => makeBet(kind, values, 5)).toThrow(RangeError)
  })

  it('recognizes exactly the 60 supported split lines, including the three zero splits', () => {
    let accepted = 0
    for (let first = 0; first <= 36; first++) {
      for (let second = first + 1; second <= 36; second++) {
        if (legalSelection('split', [first, second])) accepted++
      }
    }
    expect(accepted).toBe(60)
  })

  it('serializes scalar, shaped, and outside choices into their distinct API fields', () => {
    expect(makeBet('straight', [0], 5)).toEqual({ kind: 'straight', number: 0, numbers: [], stake: 5 })
    expect(makeBet('column', [3], 5)).toEqual({ kind: 'column', number: 3, numbers: [], stake: 5 })
    expect(makeBet('dozen', [2], 5)).toEqual({ kind: 'dozen', number: 2, numbers: [], stake: 5 })
    const values = [5, 1, 4, 2]
    expect(makeBet('corner', values, 5)).toEqual({ kind: 'corner', number: null, numbers: [1, 2, 4, 5], stake: 5 })
    expect(values).toEqual([5, 1, 4, 2])
    for (const kind of outsideKinds) {
      expect(makeBet(kind, [], 5)).toEqual({ kind, number: null, numbers: [], stake: 5 })
    }
    for (const stake of [0, -1, NaN, Infinity, 1.001]) expect(() => makeBet('straight', [17], stake)).toThrow(RangeError)
  })

  it('highlights complete column and dozen coverage, while zero loses every outside bet', () => {
    expect(coveredPockets(makeBet('column', [3], 1))).toEqual([3, 6, 9, 12, 15, 18, 21, 24, 27, 30, 33, 36])
    expect(coveredPockets(makeBet('dozen', [3], 1))).toEqual(Array.from({ length: 12 }, (_, index) => 25 + index))
    expect(coveredPockets(makeBet('straight', [0], 1))).toEqual([0])
    expect(coveredPockets(makeBet('street', [3, 0, 2], 1))).toEqual([0, 2, 3])
    for (const kind of outsideKinds) {
      const covered = coveredPockets(makeBet(kind, [], 1))
      expect(covered).toHaveLength(18)
      expect(covered).not.toContain(0)
    }
    expect(coveredPockets(makeBet('red', [], 1))).toContain(1)
    expect(coveredPockets(makeBet('red', [], 1))).not.toContain(2)
    expect(coveredPockets({ kind: 'split', stake: 1, number: null, numbers: [3, 4] })).toEqual([])
  })

  it('shows the selected pockets in shaped bet labels and retains standard payout terminology', () => {
    expect(formatBetLabel(makeBet('corner', [5, 4, 2, 1], 5))).toBe('Corner · 1, 2, 4, 5')
    expect(formatBetLabel(makeBet('straight', [0], 5))).toBe('Number 0')
    expect(formatBetLabel(makeBet('dozen', [2], 5))).toBe('Dozen 2')
    expect(formatBetLabel(makeBet('low', [], 5))).toBe('1–18')
    expect(betOptions.find(option => option.kind === 'straight')?.payout).toBe('35:1')
    expect(betOptions.find(option => option.kind === 'red')?.payout).toBe('1:1')
  })

  it('uses European wheel order and wraps neighbors around zero without duplicates', () => {
    expect(new Set(europeanWheelOrder).size).toBe(37)
    expect([...europeanWheelOrder].sort((left, right) => left - right)).toEqual(Array.from({ length: 37 }, (_, index) => index))
    expect(neighborPockets(0, 1)).toEqual([26, 0, 32])
    expect(neighborPockets(26, 2)).toEqual([35, 3, 26, 0, 32])
    expect(neighborPockets(17, 0)).toEqual([17])
    expect(new Set(neighborPockets(0, 18)).size).toBe(37)
    expect(neighborPockets(37, 1)).toEqual([])
    expect(neighborPockets(17, -1)).toEqual([])
    expect(neighborPockets(17, 1.5)).toEqual([])
  })

  it('requires finite, affordable chip values within the actual table increments', () => {
    expect(validStake(1, status, 1)).toBe(true)
    expect(validStake(100, status, 100)).toBe(true)
    for (const value of [0, .5, 1.5, 101, NaN, Infinity]) expect(validStake(value, status, 1_000)).toBe(false)
    expect(validStake(5, status, 4)).toBe(false)
    expect(validStake(5, status, NaN)).toBe(false)
    expect(validStake(5, { ...status, available: false }, 1_000)).toBe(false)
    expect(validStake(5, null, 1_000)).toBe(false)
    const fractional = { ...status, minimumStake: .5, maximumStake: 2.5, stakeIncrement: .5 }
    expect(validStake(1.5, fractional, 1.5)).toBe(true)
    expect(validStake(1.6, fractional, 2.5)).toBe(false)
    expect(validStake(.3, { ...fractional, minimumStake: .1, maximumStake: .3, stakeIncrement: .1 }, .3)).toBe(true)
  })

  it('offers legal chip denominations and endpoints when limits exclude usual presets', () => {
    expect(availableChipValues(status)).toEqual([1, 5, 10, 25, 50, 100])
    expect(availableChipValues({ ...status, maximumStake: 20 })).toEqual([1, 5, 10, 20])
    const unusual = { ...status, minimumStake: 3, maximumStake: 20, stakeIncrement: 2 }
    expect(availableChipValues(unusual)).toEqual([3, 5, 19])
    expect(availableChipValues(unusual).every(value => validStake(value, unusual, 20))).toBe(true)
    expect(availableChipValues({ ...status, minimumStake: .5, maximumStake: 2.5, stakeIncrement: 1 })).toEqual([.5, 2.5])
    expect(availableChipValues({ ...status, stakeIncrement: 0 })).toEqual([])
    expect(availableChipValues(null)).toEqual([])
  })

  it('totals server-provided returns without confusing profit with the returned stake', () => {
    const round: RouletteRound = {
      roundId: 'settled', phase: 'settled', winningPocket: 17, balance: 1_345,
      bets: [
        { ...makeBet('straight', [17], 10), betIndex: 0, playerId: 'player' },
        { ...makeBet('red', [], 5), betIndex: 1, playerId: 'player' },
      ],
      settlements: [
        { playerId: 'player', kind: 'straight', stake: 10, won: true, totalReturn: 360 },
        { playerId: 'player', kind: 'red', stake: 5, won: false, totalReturn: 0 },
      ],
    }
    expect(roundTotals(round)).toEqual({ stake: 15, totalReturn: 360, net: 345 })
    expect(totalStake([{ stake: .1 }, { stake: .2 }])).toBe(.3)
    expect(totalReturn([{ totalReturn: .1 }, { totalReturn: .2 }])).toBe(.3)
  })
})
