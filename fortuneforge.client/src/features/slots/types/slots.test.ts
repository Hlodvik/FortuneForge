import { describe, expect, it } from 'vitest'
import { isSlotSymbolId, slotSymbolIds } from './slots'

describe('slot symbol vocabulary', () => {
  it('recognizes the additional ordinary symbol without treating numbers as wire IDs', () => {
    expect(isSlotSymbolId('8')).toBe(true)
    expect(slotSymbolIds.filter(symbol => symbol === '8')).toHaveLength(1)
    expect(isSlotSymbolId(8)).toBe(false)
  })

  it.each(['9', '', 'eight', 'SEAL_8', null, undefined])('continues rejecting unsupported symbol %s', (symbol) => {
    expect(isSlotSymbolId(symbol)).toBe(false)
  })
})
