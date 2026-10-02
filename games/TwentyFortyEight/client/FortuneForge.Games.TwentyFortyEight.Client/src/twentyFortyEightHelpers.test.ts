import { describe, expect, it } from 'vitest'
import { directionLabel, planTileMotion, tileClass, tileLabel } from './twentyFortyEightHelpers'

describe('2048 helpers', () => {
  it('formats tile values and special tiles', () => {
    expect(tileClass(0)).toBe('tile-empty')
    expect(tileClass(2048)).toBe('tile-2048')
    expect(tileClass(4096)).toBe('tile-super')
    expect(tileLabel(128)).toBe('128')
  })

  it('formats direction controls', () => {
    expect(directionLabel('left')).toBe('Left')
    expect(directionLabel('up')).toBe('Up')
  })

  it('plans slides and merges from their source cells', () => {
    expect(planTileMotion([
      2, 0, 2, 2,
      0, 0, 0, 0,
      0, 0, 0, 0,
      0, 0, 0, 0,
    ], 4, 'left')).toEqual([
      { from: 0, to: 0, value: 2, merged: true },
      { from: 2, to: 0, value: 2, merged: true },
      { from: 3, to: 1, value: 2, merged: false },
    ])
  })

  it('uses the destination edge for right and down moves', () => {
    expect(planTileMotion([
      2, 0, 0, 2,
      0, 0, 0, 0,
      0, 0, 0, 0,
      2, 0, 0, 0,
    ], 4, 'right')).toEqual([
      { from: 3, to: 3, value: 2, merged: true },
      { from: 0, to: 3, value: 2, merged: true },
      { from: 12, to: 15, value: 2, merged: false },
    ])
    expect(planTileMotion([
      2, 0, 0, 0,
      0, 0, 0, 0,
      0, 0, 0, 0,
      2, 0, 0, 0,
    ], 4, 'down')).toEqual([
      { from: 12, to: 12, value: 2, merged: true },
      { from: 0, to: 12, value: 2, merged: true },
    ])
  })
})
