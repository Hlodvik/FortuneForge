import { describe, expect, it } from 'vitest'
import type { SicBoRound, SicBoStatus } from './contracts'
import { appendRollHistory, availableChipValues, betLabel, betTargets, createTargetBet, historyStorageKey,
  legalSlip, parsePendingRoll, parseRollHistory, parseStake, pendingStorageKey, targetsForKind, totalStake, validStake } from './sicBoPresentation'

const status: SicBoStatus = { available: true, minimumStake: 1, maximumStakePerBet: 100, stakeIncrement: 1, maximumBetsPerRound: 20, balance: 10_000, mode: 'practice-three-dice-sic-bo' }
const scope = 'alex:practice'
const small = createTargetBet('small', 1)!
const key = 'sic-bo-exact-recovery-0001'

function round(id = 'a'.repeat(64)): SicBoRound {
  const double = createTargetBet('specific-double-2', 1)!
  const triple = createTargetBet('specific-triple-1', 1)!
  return { roundId: id, phase: 'settled', balance: 10_010.5, dice: [2, 2, 5], total: 9, isTriple: false,
    totalStaked: 3, totalReturn: 14.5, profit: 11.5, settlements: [
      { ...small, betIndex: 0, won: true, profitOdds: 1, profit: 1, totalReturn: 2 },
      { ...double, betIndex: 1, won: true, profitOdds: 11.5, profit: 11.5, totalReturn: 12.5 },
      { ...triple, betIndex: 2, won: false, profitOdds: 0, profit: -1, totalReturn: 0 },
    ] }
}

describe('the Sic Bo board and selected variant', () => {
  it('offers all ten legal betting kinds with every canonical selection exactly once', () => {
    expect(betTargets).toHaveLength(52)
    expect(new Set(betTargets.map(item => item.id)).size).toBe(52)
    expect(Object.fromEntries([...new Set(betTargets.map(item => item.kind))].map(kind => [kind, targetsForKind(kind).length])))
      .toEqual({ small: 1, big: 1, odd: 1, even: 1, 'any-triple': 1, 'single-number': 6,
        'specific-double': 6, 'specific-triple': 6, total: 14, 'two-number-combination': 15 })
    expect(targetsForKind('total').map(item => item.total)).toEqual([4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17])
    expect(targetsForKind('single-number').map(item => item.face)).toEqual([1, 2, 3, 4, 5, 6])
    expect(targetsForKind('two-number-combination').map(item => [item.firstFace, item.secondFace])).toEqual([
      [1, 2], [1, 3], [1, 4], [1, 5], [1, 6], [2, 3], [2, 4], [2, 5], [2, 6], [3, 4], [3, 5], [3, 6], [4, 5], [4, 6], [5, 6],
    ])
  })

  it.each([[4, 64], [5, 32], [6, 19], [7, 12], [8, 8.5], [9, 7], [10, 6.5], [11, 6.5], [12, 7], [13, 8.5], [14, 12], [15, 19], [16, 32], [17, 64]])
    ('shows the unchanged total %s profit odds %s:1', (total, odds) => {
      expect(betTargets.find(item => item.id === `total-${total}`)?.odds).toBe(`${odds}:1`)
    })

  it('preserves single, double, triple and combination odds and their coverage distinctions', () => {
    expect(targetsForKind('single-number').every(item => item.odds === '1:1 / 2:1 / 12:1')).toBe(true)
    expect(targetsForKind('specific-double').every(item => item.odds === '11.5:1' && item.coverageText === 'Two or three appearances')).toBe(true)
    expect(targetsForKind('specific-triple').every(item => item.odds === '195:1')).toBe(true)
    expect(targetsForKind('two-number-combination').every(item => item.odds === '6:1')).toBe(true)
    expect(targetsForKind('any-triple')[0]?.odds).toBe('32:1')
    expect(['small', 'big', 'odd', 'even'].map(kind => betTargets.find(item => item.kind === kind)?.coverageText))
      .toEqual(['Total 4–10 · triples lose', 'Total 11–17 · triples lose', 'Odd total · triples lose', 'Even total · triples lose'])
  })

  it('creates only the exact transport selection fields and labels every supported shape', () => {
    expect(createTargetBet('small', 5)).toEqual({ kind: 'small', stake: 5, face: null, total: null, firstFace: null, secondFace: null })
    expect(createTargetBet('specific-double-6', 3)).toEqual({ kind: 'specific-double', stake: 3, face: 6, total: null, firstFace: null, secondFace: null })
    expect(createTargetBet('total-17', 2)).toEqual({ kind: 'total', stake: 2, face: null, total: 17, firstFace: null, secondFace: null })
    expect(createTargetBet('two-number-combination-2-6', 4)).toEqual({ kind: 'two-number-combination', stake: 4, face: null, total: null, firstFace: 2, secondFace: 6 })
    expect(betTargets.every(item => betLabel(createTargetBet(item, 1)!) === item.label)).toBe(true)
  })

  it('does not copy presentation metadata or a forged selection into a transport bet', () => {
    const forged = { ...betTargets[0]!, face: 6, odds: '999:1' }
    expect(createTargetBet(forged, 1)).toEqual(small)
    expect(createTargetBet('two-number-combination-6-2', 1)).toBeNull()
    expect(createTargetBet('total-3', 1)).toBeNull()
    expect(createTargetBet('single-number-7', 1)).toBeNull()
    expect(createTargetBet('small', .001)).toBeNull()
    expect(createTargetBet('small', 0)).toBeNull()
  })
})

describe('exact stake drafts and affordable ordered slips', () => {
  it.each(['', ' ', '1e0', '1E2', '0x10', '+1', '-1', 'Infinity', 'NaN', '1.', '.', '1,00', '1.001', '1.000', '1 0'])
    ('rejects draft %j without changing its value', raw => expect(parseStake(raw, status)).toBeNull())

  it('accepts complete plain decimals and rejects out-of-limit or wrong-step stakes', () => {
    expect(parseStake(' 1.00 ', status)).toBe(1)
    expect(parseStake('100', status)).toBe(100)
    expect(parseStake('01', status)).toBe(1)
    expect(parseStake('1.5', status)).toBeNull()
    expect(parseStake('0', status)).toBeNull()
    expect(parseStake('101', status)).toBeNull()
    expect(parseStake('1', null)).toBeNull()
    expect(parseStake('1', { ...status, available: false })).toBeNull()
  })

  it('anchors increments to the actual minimum rather than zero', () => {
    const shifted = { ...status, minimumStake: 3, maximumStakePerBet: 10, stakeIncrement: 2 }
    expect([2, 3, 4, 5, 9, 10].map(value => validStake(value, shifted))).toEqual([false, true, false, true, true, false])
    expect(availableChipValues(shifted)).toEqual([3, 5, 9])
  })

  it('uses integer cents for fractional table minima, endpoints and increments', () => {
    const cents = { ...status, minimumStake: .1, maximumStakePerBet: .7, stakeIncrement: .2 }
    expect(['.10', '0.30', '0.50', '0.70'].map(raw => parseStake(raw, cents))).toEqual([.1, .3, .5, .7])
    expect(parseStake('.20', cents)).toBeNull()
    expect(availableChipValues(cents)).toEqual([.1, .7])
    expect(validStake(.3000000001, cents)).toBe(false)
  })

  it('offers legal chips including the last reachable endpoint, without a wallet clamp', () => {
    expect(availableChipValues(status)).toEqual([1, 5, 10, 25, 50, 100])
    expect(availableChipValues({ ...status, balance: 0 })).toEqual([1, 5, 10, 25, 50, 100])
    expect(availableChipValues(null)).toEqual([])
    expect(availableChipValues({ ...status, available: false })).toEqual([])
    expect(availableChipValues({ ...status, stakeIncrement: 0 })).toEqual([])
  })

  it.each([NaN, Infinity, -1, 0, .5, 1.000000001, 101])('rejects numeric stake %s', value => expect(validStake(value, status)).toBe(false))

  it('sums exact cents and rejects values that would need rounding', () => {
    expect(totalStake([{ stake: .1 }, { stake: .2 }, { stake: .07 }])).toBe(.37)
    expect(totalStake([])).toBe(0)
    expect(totalStake([{ stake: 100 }, { stake: 100 }, { stake: 1 }])).toBe(201)
    expect(totalStake([{ stake: 1.005 }])).toBeNaN()
    expect(totalStake([{ stake: Infinity }])).toBeNaN()
  })

  it('preserves ordered duplicate positions and permits exactly affordable stakes', () => {
    const big = createTargetBet('big', 1)!
    const slip = [small, big, small]
    expect(legalSlip(slip, status, 3)).toBe(true)
    expect(legalSlip(slip, status, 2.99)).toBe(false)
    expect(slip).toEqual([small, big, small])
    expect(legalSlip(Array.from({ length: 20 }, () => small), status, 20)).toBe(true)
    expect(legalSlip(Array.from({ length: 21 }, () => small), status, 21)).toBe(false)
  })

  it('requires a live table, valid complete selections, valid stakes and a nonempty legal count', () => {
    expect(legalSlip([], status, 100)).toBe(false)
    expect(legalSlip([small], null, 100)).toBe(false)
    expect(legalSlip([small], { ...status, available: false }, 100)).toBe(false)
    expect(legalSlip([small], status, NaN)).toBe(false)
    expect(legalSlip([small], status, -.01)).toBe(false)
    expect(legalSlip([small], { ...status, maximumBetsPerRound: 1.5 }, 100)).toBe(false)
    expect(legalSlip([{ ...small, face: 1 }], status, 100)).toBe(false)
    expect(legalSlip([{ ...small, stake: 1.5 }], status, 100)).toBe(false)
    expect(legalSlip([createTargetBet('two-number-combination-1-2', 1)!], status, 1)).toBe(true)
  })

  it('accepts a fractional cent-aware balance boundary without a floating-point overrun', () => {
    const pennies = { ...status, minimumStake: .07, maximumStakePerBet: .07, stakeIncrement: .01 }
    expect(legalSlip([{ ...small, stake: .07 }], pennies, .07)).toBe(true)
  })

  it('rejects an aggregate beyond safe integer cents rather than turning it into a zero total', () => {
    const stake = 40_000_000_000_000
    const oversized = { ...status, maximumStakePerBet: stake }
    const slip = Array.from({ length: 3 }, () => ({ ...small, stake }))
    expect(totalStake(slip)).toBeNaN()
    expect(legalSlip(slip, oversized, stake)).toBe(false)
  })
})

describe('durable account and mode scoped recovery records', () => {
  it('keeps account/practice keys separate and escapes ambiguous storage suffixes', () => {
    expect(pendingStorageKey('alex:practice')).not.toBe(pendingStorageKey('alex:account'))
    expect(historyStorageKey('alex:practice')).not.toBe(historyStorageKey('jordan:practice'))
    expect(pendingStorageKey('alex:practice')).toBe('fortuneforge:sic-bo:pending:alex%3Apractice')
    expect(historyStorageKey('alex:practice')).toBe('fortuneforge:sic-bo:history:alex%3Apractice')
  })

  it('restores the same key, exact ordered duplicate slip and verified round ID into fresh copies', () => {
    const pending = { scope, idempotencyKey: key, roundId: 'd'.repeat(64), bets: [small, createTargetBet('big', 2)!, small] }
    const restored = parsePendingRoll(pending, scope)!
    expect(restored).toEqual(pending)
    expect(restored).not.toBe(pending)
    expect(restored.bets).not.toBe(pending.bets)
    expect(restored.bets[0]).not.toBe(pending.bets[0])
    expect(parsePendingRoll(pending, 'alex:account')).toBeNull()
    expect(parsePendingRoll({ ...pending, scope: undefined }, scope)).toBeNull()
    expect(parsePendingRoll(pending, '')).toBeNull()
  })

  it('permits an unknown round ID while retaining its original safe-retry key', () => {
    expect(parsePendingRoll({ scope, idempotencyKey: key, bets: [small] }, scope)).toEqual({ scope, idempotencyKey: key, bets: [small] })
  })

  it.each(['', 'too-short', 'x'.repeat(129), 'sic-bo-bad key-0001', 'sic-bo-bad/key-0001', 'sic-bo-ü-key-000001'])
    ('rejects an unverified request key %j', idempotencyKey => expect(parsePendingRoll({ scope, idempotencyKey, bets: [small] }, scope)).toBeNull())

  it.each(['x'.repeat(64), 'a'.repeat(63), 'a'.repeat(65), 'a'.repeat(32), 'A'.repeat(64), null])
    ('rejects a noncanonical active-server round ID %j', roundId => expect(parsePendingRoll({ scope, idempotencyKey: key, bets: [small], roundId }, scope)).toBeNull())

  it('rejects corrupt pending selections, stakes and counts instead of silently repairing a replay', () => {
    const parse = (bets: unknown) => parsePendingRoll({ scope, idempotencyKey: key, bets }, scope)
    expect(parse([])).toBeNull()
    expect(parse(Array.from({ length: 21 }, () => small))).toBeNull()
    expect(parse([{ ...small, stake: 1.5 }])).toBeNull()
    expect(parse([{ ...small, stake: 101 }])).toBeNull()
    expect(parse([{ ...small, face: 1 }])).toBeNull()
    expect(parse([{ ...small, kind: 'SMALL' }])).toBeNull()
    expect(parse([{ ...small, total: undefined }])).toBeNull()
    expect(parse([{ ...small, kind: 'single-number', face: 1.5 }])).toBeNull()
    expect(parse([{ ...small, kind: 'two-number-combination', firstFace: 6, secondFace: 2 }])).toBeNull()
    expect(parse([{ ...small, kind: 'two-number-combination', firstFace: 2, secondFace: 2 }])).toBeNull()
  })
})

describe('authoritative local roll history', () => {
  it('retains exact fractional returns, losses, dice order and ordered settlement details', () => {
    const first = round()
    const restored = parseRollHistory({ scope, rounds: [first] }, scope)
    expect(restored).toEqual([first])
    expect(restored[0]).not.toBe(first)
    expect(restored[0]?.dice).not.toBe(first.dice)
    expect(restored[0]?.settlements).not.toBe(first.settlements)
    expect(restored[0]?.settlements.map(item => item.totalReturn)).toEqual([2, 12.5, 0])
    expect(restored[0]?.settlements.map(item => item.profit)).toEqual([1, 11.5, -1])
  })

  it('cannot load another account, another mode or a legacy unscoped envelope', () => {
    expect(parseRollHistory({ scope, rounds: [round()] }, 'alex:account')).toEqual([])
    expect(parseRollHistory({ scope: 'jordan:practice', rounds: [round()] }, scope)).toEqual([])
    expect(parseRollHistory([round()], scope)).toEqual([])
    expect(parseRollHistory({ rounds: [round()] }, scope)).toEqual([])
    expect(parseRollHistory(null, scope)).toEqual([])
  })

  it.each([
    { roundId: 'not-a-server-id' }, { phase: 'rolling' }, { dice: [0, 2, 5] }, { dice: [2, 5] },
    { total: 10 }, { isTriple: true }, { balance: -.01 }, { balance: 10_010.501 },
    { totalStaked: 4 }, { totalReturn: 14.51 }, { profit: 11.49 }, { settlements: [] },
  ])('drops inconsistent stored round %j', changed => expect(parseRollHistory({ scope, rounds: [{ ...round(), ...changed }] }, scope)).toEqual([]))

  it('rejects settlement index, complete shape, profit-odds and amount corruption', () => {
    const base = round()
    for (const changed of [{ betIndex: 1 }, { face: 1 }, { stake: 1.5 }, { profitOdds: 999 }, { profit: 1.01 }, { totalReturn: 2.01 }, { won: false }]) {
      expect(parseRollHistory({ scope, rounds: [{ ...base, settlements: [{ ...base.settlements[0], ...changed }, ...base.settlements.slice(1)] }] }, scope)).toEqual([])
    }
    expect(parseRollHistory({ scope, rounds: [{ ...base, settlements: [...base.settlements.slice(0, 2), { ...base.settlements[2], profitOdds: 1 }] }] }, scope)).toEqual([])
  })

  it('deduplicates round IDs, limits history to twelve and leaves the input untouched', () => {
    const older = Array.from({ length: 14 }, (_, index) => round((index + 1).toString(16).padStart(64, '0')))
    const snapshot = JSON.stringify(older)
    const latest = round('f'.repeat(64))
    const next = appendRollHistory([older[0]!, older[0]!, ...older.slice(1)], latest)
    expect(next).toHaveLength(12)
    expect(new Set(next.map(item => item.roundId)).size).toBe(12)
    expect(next[0]).toEqual(latest)
    expect(next[1]).toEqual(older[0])
    expect(JSON.stringify(older)).toBe(snapshot)
    expect(parseRollHistory({ scope, rounds: [older[0], older[0], ...older.slice(1)] }, scope)).toHaveLength(12)
  })

  it('refreshes a recovered round in place without a second history entry', () => {
    const first = round()
    const second = round('b'.repeat(64))
    const current = { ...first, balance: 8_000.5 }
    const next = appendRollHistory([second, first], current)
    expect(next).toEqual([current, second])
    expect(next).toHaveLength(2)
  })
})
