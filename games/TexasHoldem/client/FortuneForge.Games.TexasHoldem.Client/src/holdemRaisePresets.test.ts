import { describe, expect, it } from 'vitest'
import { holdemPotRaiseTo } from './holdemRaisePresets'
import { testTable } from './testFixtures'

describe('Hold’em after-call pot presets', () => {
  it('counts the call once in both the player commitment and the pot', () => {
    // The player has 100 in the round, owes another 100, and sees a 1,000 pot.
    expect(holdemPotRaiseTo(testTable, 0.5)).toBe(750)
    expect(holdemPotRaiseTo(testTable, 1)).toBe(1300)
  })

  it('adds no phantom call when the viewer has already matched the bet', () => {
    const table = { ...testTable, currentBet: 100 }
    expect(holdemPotRaiseTo(table, 0.5)).toBe(600)
    expect(holdemPotRaiseTo(table, 1)).toBe(1100)
  })

  it('keeps odd-cent half pots integral without changing table state', () => {
    const table = { ...testTable, pot: 1001 }
    const before = JSON.stringify(table)
    expect(holdemPotRaiseTo(table, 0.5)).toBe(751)
    expect(JSON.stringify(table)).toBe(before)
  })

  it('honors the authoritative minimum and exact off-step maximum', () => {
    expect(holdemPotRaiseTo({ ...testTable, pot: 0 }, 0.5)).toBe(400)
    expect(holdemPotRaiseTo({ ...testTable, pot: 10_000, maximumRaiseTo: 2533 }, 1)).toBe(2533)
  })

  it('retains the server short-all-in exception below the ordinary minimum', () => {
    const table = { ...testTable, minimumRaiseTo: 600, maximumRaiseTo: 350, shortAllInRaiseTo: 350,
      seats: testTable.seats.map(seat => seat.isCurrentPlayer ? { ...seat, stack: 250 } : seat) }
    expect(holdemPotRaiseTo(table, 0.5)).toBe(350)
    expect(holdemPotRaiseTo(table, 1)).toBe(350)
  })

  it('does not use another player as the viewer when no current seat is supplied', () => {
    const table = { ...testTable, seats: testTable.seats.filter(seat => !seat.isCurrentPlayer) }
    expect(holdemPotRaiseTo(table, 1)).toBe(400)
  })
})
