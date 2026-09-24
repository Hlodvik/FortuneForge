import { describe, expect, it } from 'vitest'
import { AMBIENT_GAME_MUSIC } from './ambientGameMusic'

describe('ambient game music', () => {
  it('only provides low-volume loops for games whose designs intentionally use music', () => {
    expect(Object.keys(AMBIENT_GAME_MUSIC).sort()).toEqual([
      'asteroids', 'flappy', 'liars-dice',
    ])
    expect(Object.values(AMBIENT_GAME_MUSIC).every(({ source, volume }) => source.length > 0 && volume > 0 && volume <= 0.04)).toBe(true)
  })
})
