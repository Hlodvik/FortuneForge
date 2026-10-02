import { describe, expect, it } from 'vitest'
import { actionLabel, formatScore, sizeLabel } from './asteroidsHelpers'

describe('Asteroids helpers', () => {
  it('formats controls and asteroid sizes', () => {
    expect(actionLabel('rotate-left')).toBe('Rotate left')
    expect(actionLabel('fire')).toBe('Fire')
    expect(sizeLabel('medium')).toBe('Medium')
  })

  it('formats scores', () => {
    expect(formatScore(1200)).toBe('1,200')
  })
})
