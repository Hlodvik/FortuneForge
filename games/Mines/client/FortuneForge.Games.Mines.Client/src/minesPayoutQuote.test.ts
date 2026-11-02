import { describe, expect, it } from 'vitest'
import { isSupportedMinesHouseEdge, isSupportedMinesPayoutPolicy, minesMultiplierQuote } from './minesPayoutQuote'
import { acceptedMinesMaximumWagerCents, currentMinesMaximumWagerCents } from './minesWagerLimits'

describe('Mines informational payout quotes', () => {
  it('preserves the uncapped 90% legacy curve at every mine count and cash-out depth', () => {
    for (let mines = 1; mines <= 24; mines++) {
      let survival = 1
      for (let picks = 1; picks <= 25 - mines; picks++) {
        survival *= (26 - mines - picks) / (26 - picks)
        expect(minesMultiplierQuote(mines, picks, 1000, 1) * survival).toBeCloseTo(.9, 12)
      }
    }
  })

  it('keeps every safe depth playable on the bounded v2 curve and reaches 50× on compressed boards', () => {
    for (let mines = 1; mines <= 24; mines++) {
      const safe = 25 - mines
      let previous = .9
      for (let picks = 1; picks <= safe; picks++) {
        const value = minesMultiplierQuote(mines, picks)
        expect(value).toBeGreaterThan(previous)
        expect(value).toBeLessThanOrEqual(50)
        expect(value).toBeLessThanOrEqual(minesMultiplierQuote(mines, picks, 1000, 1))
        previous = value
      }
      expect(previous).toBe(mines === 1 || mines === 24 ? 22.5 : 50)
    }
  })

  it('uses an exact linear stretched tail instead of clipping, stopping, or repricing early gems', () => {
    expect(minesMultiplierQuote(21, 1)).toBe(5.625)
    expect(minesMultiplierQuote(21, 2)).toBe(45)
    expect(minesMultiplierQuote(21, 3)).toBe(47.5)
    expect(minesMultiplierQuote(21, 4)).toBe(50)
    // The complementary mine return preserves continuation value, before cent rounding.
    const q = 2 / 23, mineReturn = (45 - q * 47.5) / (1 - q)
    expect(q * 47.5 + (1 - q) * mineReturn).toBeCloseTo(45, 12)
  })

  it('does not clamp low-mine first-gem returns to the wager', () => {
    expect(minesMultiplierQuote(1, 1)).toBe(.9375)
    expect(minesMultiplierQuote(2, 1)).toBeCloseTo(.9782608695652174, 14)
    expect(Math.floor(100 * minesMultiplierQuote(1, 1))).toBe(93)
    expect(Math.floor(100 * minesMultiplierQuote(2, 1))).toBe(97)
  })

  it('quotes the largest new-wager return at the reduced R5 wager maximum', () => {
    expect(minesMultiplierQuote(12, 13)).toBe(50)
    expect(minesMultiplierQuote(13, 12)).toBe(50)
    expect(minesMultiplierQuote(12, 13) * currentMinesMaximumWagerCents).toBe(25000)
  })

  it('preserves explicit legacy 99% quotes', () => {
    expect(minesMultiplierQuote(3, 1, 100, 1)).toBe(1.125)
    expect(minesMultiplierQuote(3, 2, 100, 1)).toBe(9 / 7)
    expect(minesMultiplierQuote(12, 13, 100, 1)).toBe(5_148_297)
    expect(minesMultiplierQuote(12, 13, 100, 1) * acceptedMinesMaximumWagerCents).toBe(51_482_970_000)
  })

  it.each([100, 1000])('supports the documented %s basis-point rule', edge => {
    expect(isSupportedMinesHouseEdge(edge)).toBe(true)
  })

  it.each([0, 10, 900, 1100, 10_000, null, undefined, '1000'])('rejects an unsupported %s rule', edge => {
    expect(isSupportedMinesHouseEdge(edge)).toBe(false)
  })
  it.each([1, 2])('accepts frozen payout policy %s', policy => expect(isSupportedMinesPayoutPolicy(policy)).toBe(true))
  it.each([0, 3, null, undefined, '2'])('rejects unsupported payout policy %s', policy => expect(isSupportedMinesPayoutPolicy(policy)).toBe(false))
})
