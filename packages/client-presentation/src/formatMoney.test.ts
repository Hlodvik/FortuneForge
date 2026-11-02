import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { formatMoney } from './formatMoney.js'
import captured from './__fixtures__/money-formatting.json' with { type: 'json' }

const values = {
  NaN, Infinity, '-Infinity': -Infinity, '0': 0, '-0': -0, '1': 1, '-1': -1,
  '0.005': 0.005, '-0.005': -0.005, '1.235': 1.235, '-1.235': -1.235,
  '999.995': 999.995, '1000000.123': 1000000.123,
  MAX_SAFE_INTEGER: Number.MAX_SAFE_INTEGER, MIN_VALUE: Number.MIN_VALUE, MAX_VALUE: Number.MAX_VALUE,
} as const
const locales = ['en-US', 'de-DE', 'ar-EG'] as const
const fixtures = captured as {
  locale: typeof locales[number]; valueTag: keyof typeof values; symbol: string; expected: string
}[]
const numberToLocaleString = Number.prototype.toLocaleString

afterEach(() => vi.restoreAllMocks())

it('retains the full original input matrix, including a distinct negative-zero input', () => {
  expect(Object.is(values['-0'], -0)).toBe(true)
  expect(fixtures).toHaveLength(288)
  expect(new Set(fixtures.map(({ locale, valueTag, symbol }) => JSON.stringify([locale, valueTag, symbol]))).size).toBe(288)
  for (const locale of locales) expect(fixtures.filter(row => row.locale === locale)).toHaveLength(96)
})

it('uses the runtime default locale and exactly two fraction digits', () => {
  const spy = vi.spyOn(Number.prototype, 'toLocaleString')
  const expected = numberToLocaleString.call(1234.5, undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })
  expect(formatMoney(-1234.5, 'USD ')).toBe(`-USD ${expected}`)
  expect(spy).toHaveBeenCalledExactlyOnceWith(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })
})

describe.each(locales)('original display compatibility with %s as the default locale', locale => {
  beforeEach(() => {
    // Simulate a different runtime default while still requiring production to
    // pass undefined. The captured strings came from both original helpers.
    vi.spyOn(Number.prototype, 'toLocaleString').mockImplementation(function (this: number, requestedLocale, options) {
      expect(requestedLocale).toBeUndefined()
      return numberToLocaleString.call(this, locale, options)
    })
  })

  it.each(fixtures.filter(row => row.locale === locale))('$valueTag with "$symbol"', ({ valueTag, symbol, expected }) => {
    expect(Object.hasOwn(values, valueTag)).toBe(true)
    expect(formatMoney(values[valueTag], symbol)).toBe(expected)
  })
})
