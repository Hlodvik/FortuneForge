import { describe, expect, it } from 'vitest'
import { keyboardFlapIntent, pointerFlapIntent } from './flappyControls'

describe('Flappy browser controls', () => {
  it('maps an active Space press to one flap and prevents page scrolling', () => {
    expect(keyboardFlapIntent('Space', false, true)).toEqual({ flap: true, preventDefault: true })
  })
  it('suppresses Space auto-repeat and input after game over', () => {
    expect(keyboardFlapIntent('Space', true, true)).toEqual({ flap: false, preventDefault: true })
    expect(keyboardFlapIntent('Space', false, false).flap).toBe(false)
  })
  it('maps only an active left pointer press to the same flap intent', () => {
    expect(pointerFlapIntent(0, true)).toEqual({ flap: true, preventDefault: true })
    expect(pointerFlapIntent(1, true).flap).toBe(false)
    expect(pointerFlapIntent(2, true).flap).toBe(false)
    expect(pointerFlapIntent(0, false).flap).toBe(false)
  })
})
