import { describe, expect, it } from 'vitest'
import vectors from './asteroidsReplayVectors.json'
import { AsteroidsControl, advanceAsteroidsFrame, foldPaidSeedHex, replayAsteroidsSimulation, startAsteroidsSimulation } from './asteroidsSimulation'

type Expected = {
  score: number; lives: number; wave: number; phase: string; tick: number; randomState: number
  ship: { x: number; y: number; velocityX: number; velocityY: number; angle: number; invulnerabilityTicks: number; thrustTicks: number }
  asteroidCount: number; bulletCount: number; powerUpCount: number
  enemyBulletCount: number; alienSpawnCooldownTicks: number
  asteroid: { id: number; x: number; y: number; velocityX: number; velocityY: number; radius: number; size: string; hitPoints: number; spriteVariant: number } | null
  bullet: { id: number; x: number; y: number; velocityX: number; velocityY: number; remainingTicks: number } | null
  alienShip: { id: number; x: number; y: number; velocityX: number; velocityY: number; radius: number; type: string; hitPoints: number; fireCooldownTicks: number; courseChangeTicks: number; remainingTicks: number } | null
  enemyBullet: { id: number; x: number; y: number; velocityX: number; velocityY: number; remainingTicks: number } | null
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

  it('wraps the ship without stopping its velocity', () => {
    const initial = startAsteroidsSimulation(42)
    const state: typeof initial = {
      ...initial,
      ship: { ...initial.ship, position: { x: 1, y: 1 }, velocity: { x: -3, y: -3 } },
      asteroids: [{ id: 1, position: { x: 100, y: 100 }, velocity: { x: 0, y: 0 }, radius: 13, size: 'small', hitPoints: 4, spriteVariant: 0 }],
    }

    const next = advanceAsteroidsFrame(state, AsteroidsControl.None)

    expect(next.ship.position).toEqual({ x: 798, y: 598 })
    expect(next.ship.velocity).toEqual({ x: -2.985, y: -2.985 })
  })

  it('wraps asteroids, bullets, and power-ups across both field edges', () => {
    const initial = startAsteroidsSimulation(42)
    const state: typeof initial = {
      ...initial,
      asteroids: [{ id: 1, position: { x: 799, y: 599 }, velocity: { x: 3, y: 4 }, radius: 13, size: 'tiny', hitPoints: 2, spriteVariant: 0 }],
      bullets: [{ id: 2, position: { x: 799, y: 300 }, velocity: { x: 3, y: 0 }, remainingTicks: 10 }],
      powerUps: [{ id: 3, position: { x: 799, y: 400 }, velocity: { x: 3, y: 0 }, type: 'shield', remainingTicks: 60 }],
    }

    const next = advanceAsteroidsFrame(state, AsteroidsControl.None)

    expect(next.asteroids[0]!.position).toEqual({ x: 2, y: 3 })
    expect(next.asteroids[0]!.velocity).toEqual({ x: 3, y: 4 })
    expect(next.bullets[0]!.position).toEqual({ x: 2, y: 300 })
    expect(next.bullets[0]!.remainingTicks).toBe(9)
    expect(next.powerUps[0]!.position).toEqual({ x: 2, y: 400 })
  })

  it('detects projectile and ship collisions across field seams', () => {
    const initial = startAsteroidsSimulation(42)
    const projectileState: typeof initial = {
      ...initial,
      asteroids: [{ id: 1, position: { x: 1, y: 300 }, velocity: { x: 0, y: 0 }, radius: 13, size: 'small', hitPoints: 4, spriteVariant: 0 }],
      bullets: [{ id: 2, position: { x: 799, y: 300 }, velocity: { x: 3, y: 0 }, remainingTicks: 10 }],
    }
    const projectileResult = advanceAsteroidsFrame(projectileState, AsteroidsControl.None)
    expect(projectileResult.asteroids[0]!.hitPoints).toBe(3)
    expect(projectileResult.bullets).toHaveLength(0)

    const collisionState: typeof initial = {
      ...initial,
      ship: { ...initial.ship, position: { x: 2, y: 300 } },
      asteroids: [{ id: 1, position: { x: 790, y: 300 }, velocity: { x: 0, y: 0 }, radius: 13, size: 'small', hitPoints: 4, spriteVariant: 0 }],
    }
    const collisionResult = advanceAsteroidsFrame(collisionState, AsteroidsControl.None)
    expect(collisionResult.event).toBe('damaged')
    expect(collisionResult.lives).toBe(2)

    const collectionState: typeof initial = {
      ...initial,
      ship: { ...initial.ship, position: { x: 2, y: 300 } },
      asteroids: [{ id: 1, position: { x: 100, y: 100 }, velocity: { x: 0, y: 0 }, radius: 13, size: 'small', hitPoints: 4, spriteVariant: 0 }],
      powerUps: [{ id: 2, position: { x: 795, y: 300 }, velocity: { x: 0, y: 0 }, type: 'extra-life', remainingTicks: 60 }],
    }
    const collectionResult = advanceAsteroidsFrame(collectionState, AsteroidsControl.None)
    expect(collectionResult.event).toBe('power-up-collected')
    expect(collectionResult.powerUps).toHaveLength(0)
    expect(collectionResult.lives).toBe(4)
  })

  it('sweeps enemy bullets relative to the moving ship', () => {
    const initial = startAsteroidsSimulation(42)
    const state: typeof initial = {
      ...initial,
      ship: { ...initial.ship, position: { x: 100, y: 300 }, velocity: { x: 7, y: 0 } },
      asteroids: [{ id: 1, position: { x: 400, y: 100 }, velocity: { x: 0, y: 0 }, radius: 13, size: 'tiny', hitPoints: 2, spriteVariant: 0 }],
    }

    const clearResult = advanceAsteroidsFrame({
      ...state,
      enemyBullets: [{ id: 91, position: { x: 117, y: 300 }, velocity: { x: 5.4, y: 0 }, remainingTicks: 10 }],
    }, AsteroidsControl.None)
    expect(clearResult.lives).toBe(3)
    expect(clearResult.enemyBullets).toHaveLength(1)

    const hitResult = advanceAsteroidsFrame({
      ...state,
      ship: { ...state.ship, position: { x: 798, y: 300 } },
      enemyBullets: [{ id: 91, position: { x: 787, y: 300 }, velocity: { x: 5.4, y: 0 }, remainingTicks: 10 }],
    }, AsteroidsControl.None)
    expect(hitResult.lives).toBe(2)
    expect(hitResult.enemyBullets).toHaveLength(0)
  })

  it('sweeps player bullets relative to moving asteroids', () => {
    const initial = startAsteroidsSimulation(42)
    const state: typeof initial = {
      ...initial,
      bullets: [{ id: 2, position: { x: 170, y: 100 }, velocity: { x: 17, y: 0 }, remainingTicks: 10 }],
    }

    const clearResult = advanceAsteroidsFrame({
      ...state,
      asteroids: [{ id: 1, position: { x: 170, y: 114.2 }, velocity: { x: 3, y: -1.4 }, radius: 13, size: 'tiny', hitPoints: 2, spriteVariant: 0 }],
    }, AsteroidsControl.None)
    expect(clearResult.asteroids[0]!.hitPoints).toBe(2)
    expect(clearResult.bullets).toHaveLength(1)

    const hitResult = advanceAsteroidsFrame({
      ...state,
      asteroids: [{ id: 1, position: { x: 170, y: 112.5 }, velocity: { x: -3, y: 1.4 }, radius: 13, size: 'tiny', hitPoints: 2, spriteVariant: 0 }],
    }, AsteroidsControl.None)
    expect(hitResult.asteroids[0]!.hitPoints).toBe(1)
    expect(hitResult.bullets).toHaveLength(0)
  })

  it('sweeps player bullets relative to moving alien ships', () => {
    const initial = startAsteroidsSimulation(42)
    const state: typeof initial = {
      ...initial,
      asteroids: [{ id: 1, position: { x: 100, y: 400 }, velocity: { x: 0, y: 0 }, radius: 13, size: 'tiny', hitPoints: 2, spriteVariant: 0 }],
      bullets: [{ id: 2, position: { x: 170, y: 100 }, velocity: { x: 17, y: 0 }, remainingTicks: 10 }],
      alienSpawnCooldownTicks: 0,
    }

    const clearResult = advanceAsteroidsFrame({
      ...state,
      alienShip: { id: 90, position: { x: 170, y: 117.2 }, velocity: { x: 3, y: -1.4 }, radius: 16, type: 'hunter', hitPoints: 4, fireCooldownTicks: 10, courseChangeTicks: 10, remainingTicks: 100 },
    }, AsteroidsControl.None)
    expect(clearResult.alienShip?.hitPoints).toBe(4)
    expect(clearResult.bullets).toHaveLength(1)

    const hitResult = advanceAsteroidsFrame({
      ...state,
      alienShip: { id: 90, position: { x: 170, y: 115.5 }, velocity: { x: -3, y: 1.4 }, radius: 16, type: 'hunter', hitPoints: 4, fireCooldownTicks: 10, courseChangeTicks: 10, remainingTicks: 100 },
    }, AsteroidsControl.None)
    expect(hitResult.alienShip?.hitPoints).toBe(3)
    expect(hitResult.bullets).toHaveLength(0)
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
  expect(state.enemyBullets).toHaveLength(expected.enemyBulletCount)
  expect(state.alienSpawnCooldownTicks).toBe(expected.alienSpawnCooldownTicks)
  close(state.ship.position.x, expected.ship.x, tolerance); close(state.ship.position.y, expected.ship.y, tolerance)
  close(state.ship.velocity.x, expected.ship.velocityX, tolerance); close(state.ship.velocity.y, expected.ship.velocityY, tolerance)
  close(state.ship.angle, expected.ship.angle, tolerance)
  expect(state.ship.invulnerabilityTicks).toBe(expected.ship.invulnerabilityTicks)
  expect(state.ship.thrustTicks).toBe(expected.ship.thrustTicks)
  assertAsteroid(state.asteroids.slice().sort((left, right) => left.id - right.id)[0] ?? null, expected.asteroid, tolerance)
  assertBullet(state.bullets.slice().sort((left, right) => left.id - right.id)[0] ?? null, expected.bullet, tolerance)
  assertAlien(state.alienShip, expected.alienShip, tolerance)
  assertEnemyBullet(state.enemyBullets.slice().sort((left, right) => left.id - right.id)[0] ?? null, expected.enemyBullet, tolerance)
}

function assertAsteroid(actual: ReturnType<typeof replayAsteroidsSimulation>['asteroids'][number] | null, expected: Expected['asteroid'], tolerance: number): void {
  expect(actual === null).toBe(expected === null)
  if (actual === null || expected === null) return
  expect([actual.id, actual.radius, actual.size, actual.hitPoints, actual.spriteVariant]).toEqual([expected.id, expected.radius, expected.size, expected.hitPoints, expected.spriteVariant])
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

function assertAlien(actual: ReturnType<typeof replayAsteroidsSimulation>['alienShip'], expected: Expected['alienShip'], tolerance: number): void {
  expect(actual === null).toBe(expected === null)
  if (actual === null || expected === null) return
  expect([actual.id, actual.type, actual.hitPoints, actual.fireCooldownTicks, actual.courseChangeTicks, actual.remainingTicks]).toEqual([expected.id, expected.type, expected.hitPoints, expected.fireCooldownTicks, expected.courseChangeTicks, expected.remainingTicks])
  close(actual.position.x, expected.x, tolerance); close(actual.position.y, expected.y, tolerance)
  close(actual.velocity.x, expected.velocityX, tolerance); close(actual.velocity.y, expected.velocityY, tolerance)
  close(actual.radius, expected.radius, tolerance)
}

function assertEnemyBullet(actual: ReturnType<typeof replayAsteroidsSimulation>['enemyBullets'][number] | null, expected: Expected['enemyBullet'], tolerance: number): void {
  expect(actual === null).toBe(expected === null)
  if (actual === null || expected === null) return
  expect([actual.id, actual.remainingTicks]).toEqual([expected.id, expected.remainingTicks])
  close(actual.position.x, expected.x, tolerance); close(actual.position.y, expected.y, tolerance)
  close(actual.velocity.x, expected.velocityX, tolerance); close(actual.velocity.y, expected.velocityY, tolerance)
}

function close(actual: number, expected: number, tolerance: number): void {
  expect(Math.abs(actual - expected)).toBeLessThanOrEqual(tolerance)
}
