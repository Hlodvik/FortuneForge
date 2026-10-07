import { describe, expect, it } from 'vitest'
import { controlsForJoystickDelta } from './AsteroidsTouchControls'

describe('AsteroidsTouchControls', () => {
  it('treats the initial touch point and small movement as neutral', () => {
    expect([...controlsForJoystickDelta(0, 0)]).toEqual([])
    expect([...controlsForJoystickDelta(8, -8)]).toEqual([])
  })

  it('maps relative joystick movement to turning and thrust', () => {
    expect([...controlsForJoystickDelta(-30, 0)]).toEqual(['left'])
    expect([...controlsForJoystickDelta(30, 0)]).toEqual(['right'])
    expect([...controlsForJoystickDelta(0, -30)]).toEqual(['thrust'])
    expect([...controlsForJoystickDelta(-30, -30)]).toEqual(['left'])
  })

  it('keeps steering toward the finger movement and thrusts once aligned', () => {
    expect([...controlsForJoystickDelta(0, 40, -Math.PI / 2)]).toEqual(['right'])
    expect([...controlsForJoystickDelta(0, 40, Math.PI / 2)]).toEqual(['thrust'])
  })
})
