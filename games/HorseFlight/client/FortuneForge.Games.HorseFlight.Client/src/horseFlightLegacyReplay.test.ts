import { describe, expect, it } from 'vitest'
import vectors from '../../../tests/fixtures/legacy-replay-vectors.json'
import { startHorse, stepHorse } from './horseFlightSimulation'

describe('archived version one replay interoperability', () => {
  it.each(vectors)('restores the $policy control stream for seed $seed', vector => {
    let state = startHorse(vector.seed, 1)
    expect(state.random).toBe(vector.initialRandom)
    expect(state.platforms.map(({ id, x, width, y }) => ({ id, x, width, y }))).toEqual(vector.initialPlatforms)
    for (let tick = 0; tick < vector.totalTicks; tick++) {
      expect(state.phase).toBe('running')
      const jumped = vector.jumpTicks.includes(tick), right = vector.rightClickTicks.includes(tick)
      const result = stepHorse(state, jumped, right)
      expect(result.jumped).toBe(jumped)
      expect(result.slid || result.fastFell).toBe(right)
      state = result.state
    }
    expect(state.phase).toBe(vector.phase)
    expect(state.score).toBe(vector.score)
  })
})
