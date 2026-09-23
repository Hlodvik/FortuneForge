import { describe, expect, it } from 'vitest'
import { cellClass, directionLabel, pointKey } from './snakeHelpers'

describe('Snake helpers', () => {
  it('identifies board cells', () => {
    expect(pointKey({ x: 4, y: 7 })).toBe('4:7')
    expect(cellClass(true, true, false)).toBe('snake-head')
    expect(cellClass(false, true, false)).toBe('snake-body')
    expect(cellClass(false, false, true)).toBe('snake-food')
  })

  it('formats directions', () => {
    expect(directionLabel('down')).toBe('Down')
  })
})
