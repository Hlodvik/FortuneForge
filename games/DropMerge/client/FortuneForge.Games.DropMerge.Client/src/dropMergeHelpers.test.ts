import { describe, expect, it } from 'vitest'
import { tileClass, tileLabel, tileTone } from './dropMergeHelpers'

describe('Drop Merge presentation helpers', () => {
  it('maps powers of two onto stable palette ranks', () => {
    expect(tileClass(2)).toBe('tile-rank-0')
    expect(tileClass(4)).toBe('tile-rank-1')
    expect(tileClass(2 ** 41)).toBe('tile-rank-0')
  })

  it('formats large tile values compactly', () => {
    expect(tileLabel(2048)).toBe('2K')
    expect(tileLabel(2_000_000)).toBe('2M')
    expect(tileLabel(3_000_000_000)).toBe('3B')
  })

  it('selects the intended visual tone thresholds', () => {
    expect(tileTone(4)).toBe('soft')
    expect(tileTone(32)).toBe('warm')
    expect(tileTone(256)).toBe('gold')
    expect(tileTone(512)).toBe('bright')
  })
})
