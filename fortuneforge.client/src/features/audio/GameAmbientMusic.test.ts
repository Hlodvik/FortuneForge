import { describe, expect, it } from 'vitest'
import { AMBIENT_GAME_MUSIC } from './ambientGameMusic'

describe('ambient game music', () => {
  it('provides an audible, low-volume loop source for every playable arcade and dice game', () => {
    expect(Object.keys(AMBIENT_GAME_MUSIC).sort()).toEqual([
      '2048', 'asteroids', 'craps', 'drop-merge', 'flappy', 'horse-flight', 'liars-dice', 'sic-bo', 'snake',
    ])
    expect(Object.values(AMBIENT_GAME_MUSIC).every(({ source, volume }) => source.length > 0 && volume > 0 && volume <= 0.04)).toBe(true)
  })
})
