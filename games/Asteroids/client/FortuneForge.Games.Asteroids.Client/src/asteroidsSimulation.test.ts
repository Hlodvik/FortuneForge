import { describe, expect, it } from 'vitest'
import vectors from './asteroidsReplayVectors.json'
import { AsteroidsControl, advanceAsteroidsFrame, foldPaidSeedHex, replayAsteroidsSimulation, startAsteroidsSimulation } from './asteroidsSimulation'

type Expected = {
  score: number; lives: number; wave: number; phase: string; tick: number; randomState: number
  ship: { x: number; y: number; velocityX: number; velocityY: number; angle: number; invulnerabilityTicks: number; thrustTicks: number }
  asteroidCount: number; bulletCount: number; powerUpCount: number
  asteroid: { id: number; x: number; y: number; velocityX: number; velocityY: number; radius: number; size: string; hitPoints: number; spriteVariant: number } | null
  bullet: { id: number; x: number; y: number; velocityX: number; velocityY: number; remainingTicks: number } | null
}

describe('authoritative Asteroids simulation mirror', () => {
  it('replays every shared C#-verified golden vector', () => {
    for (const vector of vectors.vectors) {
      const state = replayAsteroidsSimulation(foldPaidSeedHex(vector.seedHex), vector.totalSteps, vector.commands)
      assertSnapshot(state, vector.expected as Expected, vectors.floatTolerance)
    }
  })

  it('folds paid seeds losslessly and rejects invalid identities and control masks', () => {
    expect(foldPaidSeedHex('000000000000002a')).toBe(42)
    expect(foldPaidSeedHex('000000010000002a')).toBe(43)
    expect(() => foldPaidSeedHex('0000000000000000')).toThrow('non-zero')
    expect(() => foldPaidSeedHex('000000000000002A')).toThrow('lowercase')
    expect(() => advanceAsteroidsFrame(startAsteroidsSimulation(42), AsteroidsControl.TurnLeft | AsteroidsControl.TurnRight)).toThrow('contradictory')
    expect(() => advanceAsteroidsFrame(startAsteroidsSimulation(42), 16)).toThrow('unknown')
  })

  it('proves upper seed bits alter the shared full-engine outcome', () => {
    const low = vectors.vectors.find(vector => vector.name === 'upper-seed-low-word')!.expected
    const high = vectors.vectors.find(vector => vector.name === 'upper-seed-changed')!.expected
    expect([low.score, low.randomState, low.asteroidCount]).not.toEqual([high.score, high.randomState, high.asteroidCount])
  })

  it('introduces a hunter in wave two and makes it accelerate toward the ship', () => {
    const initial = startAsteroidsSimulation(7)
    const waveTwo = advanceAsteroidsFrame({ ...initial, asteroids: [] }, AsteroidsControl.None)

    expect(waveTwo.wave).toBe(2)
    expect(waveTwo.asteroids.filter(asteroid => asteroid.kind === 'hunter')).toHaveLength(1)
    expect(waveTwo.message).toContain('1 hunter tracking')

    const pursued = advanceAsteroidsFrame({
      ...initial,
      ship: { ...initial.ship, position: { x: 200, y: 100 } },
      asteroids: [{ id: 1, position: { x: 100, y: 100 }, velocity: { x: 0, y: 1 }, radius: 20, size: 'small', hitPoints: 4, spriteVariant: 0, kind: 'hunter' }],
      bullets: [],
      powerUps: [],
    }, AsteroidsControl.None)
    const hunter = pursued.asteroids[0]!

    expect(hunter.position.x).toBeCloseTo(100.035, 10)
    expect(hunter.position.y).toBeCloseTo(101, 10)
    expect(hunter.velocity.x).toBeCloseTo(0.035, 10)
    expect(hunter.velocity.y).toBeCloseTo(1, 10)
  })

  it('awards hunter bonus points and converts split fragments into drifters', () => {
    const initial = startAsteroidsSimulation(7)
    const result = advanceAsteroidsFrame({
      ...initial,
      asteroids: [{ id: 1, position: { x: 100, y: 100 }, velocity: { x: 0, y: 0 }, radius: 20, size: 'small', hitPoints: 1, spriteVariant: 0, kind: 'hunter' }],
      bullets: [{ id: 2, position: { x: 100, y: 100 }, velocity: { x: 0, y: 0 }, remainingTicks: 10 }],
      powerUps: [],
    }, AsteroidsControl.None)

    expect(result.scoreGained).toBe(155)
    expect(result.score).toBe(155)
    expect(result.asteroids).toHaveLength(2)
    expect(result.asteroids.every(asteroid => asteroid.kind === 'drifter')).toBe(true)
  })
})

function assertSnapshot(state: ReturnType<typeof replayAsteroidsSimulation>, expected: Expected, tolerance: number): void {
  expect(state.score).toBe(expected.score)
  expect(state.lives).toBe(expected.lives)
  expect(state.wave).toBe(expected.wave)
  expect(state.phase).toBe(expected.phase)
  expect(state.tick).toBe(expected.tick)
  expect(state.randomState).toBe(expected.randomState)
  expect(state.asteroids).toHaveLength(expected.asteroidCount)
  expect(state.bullets).toHaveLength(expected.bulletCount)
  expect(state.powerUps).toHaveLength(expected.powerUpCount)
  close(state.ship.position.x, expected.ship.x, tolerance); close(state.ship.position.y, expected.ship.y, tolerance)
  close(state.ship.velocity.x, expected.ship.velocityX, tolerance); close(state.ship.velocity.y, expected.ship.velocityY, tolerance)
  close(state.ship.angle, expected.ship.angle, tolerance)
  expect(state.ship.invulnerabilityTicks).toBe(expected.ship.invulnerabilityTicks)
  expect(state.ship.thrustTicks).toBe(expected.ship.thrustTicks)
  assertAsteroid(state.asteroids.slice().sort((left, right) => left.id - right.id)[0] ?? null, expected.asteroid, tolerance)
  assertBullet(state.bullets.slice().sort((left, right) => left.id - right.id)[0] ?? null, expected.bullet, tolerance)
}

function assertAsteroid(actual: ReturnType<typeof replayAsteroidsSimulation>['asteroids'][number] | null, expected: Expected['asteroid'], tolerance: number): void {
  expect(actual === null).toBe(expected === null)
  if (actual === null || expected === null) return
  expect([actual.id, actual.radius, actual.size, actual.hitPoints, actual.spriteVariant]).toEqual([expected.id, expected.radius, expected.size, expected.hitPoints, expected.spriteVariant])
  expect(actual.kind).toBe('drifter')
  close(actual.position.x, expected.x, tolerance); close(actual.position.y, expected.y, tolerance)
  close(actual.velocity.x, expected.velocityX, tolerance); close(actual.velocity.y, expected.velocityY, tolerance)
}

function assertBullet(actual: ReturnType<typeof replayAsteroidsSimulation>['bullets'][number] | null, expected: Expected['bullet'], tolerance: number): void {
  expect(actual === null).toBe(expected === null)
  if (actual === null || expected === null) return
  expect([actual.id, actual.remainingTicks]).toEqual([expected.id, expected.remainingTicks])
  close(actual.position.x, expected.x, tolerance); close(actual.position.y, expected.y, tolerance)
  close(actual.velocity.x, expected.velocityX, tolerance); close(actual.velocity.y, expected.velocityY, tolerance)
}

function close(actual: number, expected: number, tolerance: number): void {
  expect(Math.abs(actual - expected)).toBeLessThanOrEqual(tolerance)
}
