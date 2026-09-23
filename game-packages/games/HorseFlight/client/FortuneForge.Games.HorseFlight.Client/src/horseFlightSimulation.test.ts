import { describe, expect, it } from 'vitest'
import { hauntedBiomeBlend, hauntedBiomeStartTick, hauntedBiomeTransitionStartTick, horseScreenX, startHorse, stepHorse } from './horseFlightSimulation'

describe('authoritative Horse Flight simulation mirror', () => {
  it('runs automatically without a movement input', () => {
    const source = startHorse(42)
    const next = stepHorse(source, false).state

    expect(next.distance).toBeGreaterThan(source.distance)
    expect(next.tick).toBe(source.tick + 1)
    expect(next.platforms.find(platform => platform.id === 1)?.x).toBeLessThan(
      source.platforms.find(platform => platform.id === 1)?.x ?? Infinity)
  })

  it('starts deterministically and records only accepted zero-based jumps', () => {
    const first = startHorse(42)
    const second = startHorse(42)

    expect(first).toEqual(second)
    expect(first).toMatchObject({ horseY: 460, velocity: 0, grounded: true, standing: 1, score: 0, tick: 0, phase: 'running' })
    expect(first.platforms).toHaveLength(4)

    const jumped = stepHorse(first, true)
    const secondJump = stepHorse(jumped.state, true)
    const exhaustedWhileAirborne = stepHorse(secondJump.state, true)

    expect(jumped.jumped).toBe(true)
    expect(jumped.state).toMatchObject({ horseY: 447.5, velocity: -12.5, grounded: false, standing: null, tick: 1 })
    expect(jumped.state.jumpsRemaining).toBe(1)
    expect(secondJump.jumped).toBe(true)
    expect(secondJump.state).toMatchObject({ velocity: -12.5, jumpsRemaining: 0, grounded: false, standing: null, tick: 2 })
    expect(exhaustedWhileAirborne.jumped).toBe(false)
    expect(exhaustedWhileAirborne.state.jumpsRemaining).toBe(0)
  })

  it('keeps the running track flat and continuous', () => {
    const platforms = [...startHorse(42).platforms].sort((left, right) => left.x - right.x)

    expect(platforms.every(platform => platform.y === 460)).toBe(true)
    for (let index = 1; index < platforms.length; index++) {
      expect(platforms[index].x).toBeCloseTo(platforms[index - 1].x + platforms[index - 1].width, 6)
    }
  })

  it('keeps running while crossing a continuous platform seam', () => {
    const source = startHorse(42)
    const seam = horseScreenX - 24 + 5
    const state = {
      ...source,
      standing: 1,
      platforms: [
        { id: 1, x: 0, width: seam, y: 460, cleared: false },
        { id: 2, x: seam, width: 1000, y: 460, cleared: false },
        { id: 3, x: seam + 1000, width: 1000, y: 460, cleared: false },
        { id: 4, x: seam + 2000, width: 1000, y: 460, cleared: false },
      ],
      obstacles: [],
      nextPlatform: 5,
      nextObstacle: 1,
    }

    const next = stepHorse(state, false).state

    expect(next.phase).toBe('running')
    expect(next.grounded).toBe(true)
    expect(next.standing).toBe(2)
  })

  it('slides under fences and fast-falls from a jump on right click', () => {
    const source = startHorse(29)
    const platform = source.platforms.find(item => item.id === source.standing)!
    const clean = { ...source, obstacles: [], nextObstacle: 2 }
    const fence = {
      id: 1,
      biome: 'mountain' as const,
      kind: 'fence' as const,
      platformId: platform.id,
      x: horseScreenX - 24 + 10,
      width: 84,
      height: 42,
      elevation: 0,
      additionalSpeed: 0,
    }
    const blocked = stepHorse({ ...clean, obstacles: [fence] }, false)
    const slide = stepHorse({ ...clean, obstacles: [fence] }, false, true)
    const jump = stepHorse(clean, true)
    const ordinaryFall = stepHorse(jump.state, false)
    const fastFall = stepHorse(jump.state, false, true)

    expect(blocked.state.phase).toBe('obstacle-collision')
    expect(slide.state.phase).toBe('running')
    expect(slide.slid).toBe(true)
    expect(slide.state.slideTicksRemaining).toBe(16)
    expect(slide.state.distance).toBeGreaterThan(blocked.state.distance)
    expect(fastFall.fastFell).toBe(true)
    expect(fastFall.state.horseY).toBeGreaterThan(ordinaryFall.state.horseY)
    expect(fastFall.state.grounded).toBe(true)
  })

  it.each([
    [1, 129, 164],
    [17, 124, 162],
    [42, 103, 51],
    [99, 119, 159],
    [123456789, 130, 165],
  ])('matches the server replay fixture for seed %i', (seed, expectedTick, expectedScore) => {
    let state = startHorse(seed)
    while (state.phase === 'running') state = stepHorse(state, false).state

    expect(state).toMatchObject({ phase: 'obstacle-collision', tick: expectedTick, score: expectedScore })
  })

  it('selects only mountain-pass obstacles before the biome transition', () => {
    const kinds = new Set(
      Array.from({ length: 150 }, (_, index) => startHorse(index + 1).obstacles)
        .flat()
        .map(obstacle => obstacle.kind))

    expect([...kinds].sort()).toEqual([
      'boulder',
      'carriage',
      'fallen-log',
      'fence',
      'lasso-thrower',
      'net-tower',
      'rolling-barrel',
    ])
    expect(Array.from({ length: 20 }, (_, index) => startHorse(index + 1).obstacles)
      .flat()
      .every(obstacle => obstacle.biome === 'mountain')).toBe(true)
  })

  it('adds denser obstacle fields as the level rises', () => {
    const generatedObstacleCount = (level: number) => Array.from({ length: 100 }, (_, index) => {
      const source = startHorse(index + 1)
      const score = (level - 1) * 500
      const readyToGenerate = {
        ...source,
        platforms: source.platforms.filter(platform => platform.id === source.standing),
        obstacles: [],
        distance: score * 10,
        score,
      }
      return stepHorse(readyToGenerate, false).state.obstacles.length
    }).reduce((total, count) => total + count, 0)

    expect(generatedObstacleCount(7)).toBeGreaterThan(generatedObstacleCount(1))
  })

  it('uses a consistent progressive count while keeping obstacle placement random within each safe slot', () => {
    const opening = startHorse(42)
    const openingCounts = opening.platforms
      .filter(platform => platform.id !== 1)
      .map(platform => opening.obstacles.filter(obstacle => obstacle.platformId === platform.id).length)
    expect(openingCounts).toEqual([1, 1, 1])

    const lateSource = startHorse(42)
    const late = stepHorse({
      ...lateSource,
      platforms: lateSource.platforms.filter(platform => platform.id === 1),
      obstacles: [],
      nextPlatform: 2,
      nextObstacle: 1,
      distance: 30_000,
      score: 3_000,
    }, false).state
    const latePlatforms = late.platforms.filter(platform => platform.id !== 1)
    expect(latePlatforms.map(platform => late.obstacles.filter(obstacle => obstacle.platformId === platform.id).length)).toEqual([3, 3, 3])
    for (const platform of latePlatforms) {
      const placed = late.obstacles.filter(obstacle => obstacle.platformId === platform.id)
        .sort((left, right) => left.x - right.x)
      expect(placed[1].x - (placed[0].x + placed[0].width)).toBeGreaterThanOrEqual(18)
      expect(placed[2].x - (placed[1].x + placed[1].width)).toBeGreaterThanOrEqual(18)
    }
  })

  it('uses a slightly inset obstacle hitbox, sustains a dash, and knocks down dashable enemies', () => {
    const source = startHorse(29)
    const platform = source.platforms.find(item => item.id === source.standing)!
    const clean = { ...source, obstacles: [], nextObstacle: 2 }
    const edgeCrate = {
      id: 1,
      biome: 'mountain' as const,
      kind: 'crate' as const,
      platformId: platform.id,
      x: horseScreenX + 24 + 3,
      width: 38,
      height: 34,
      elevation: 0,
      additionalSpeed: 0,
    }
    const lasso = {
      id: 1,
      biome: 'mountain' as const,
      kind: 'lasso-thrower' as const,
      platformId: platform.id,
      x: horseScreenX - 24 + 10,
      width: 76,
      height: 64,
      elevation: 0,
      additionalSpeed: 0.8,
    }
    const dog = { ...lasso, id: 2, kind: 'dog' as const, width: 58, height: 38, additionalSpeed: 2.2 }
    const netTower = { ...lasso, id: 3, kind: 'net-tower' as const, width: 92, height: 108, additionalSpeed: 0 }

    expect(stepHorse({ ...clean, obstacles: [edgeCrate] }, false).state.phase).toBe('running')
    expect(stepHorse({ ...clean, obstacles: [lasso] }, false).state.phase).toBe('obstacle-collision')
    expect(stepHorse({ ...clean, obstacles: [netTower] }, false).state.phase).toBe('obstacle-collision')

    const dashed = stepHorse({ ...clean, obstacles: [lasso] }, false, true).state
    expect(dashed.phase).toBe('running')
    expect(dashed.obstacles[0].knockedDownAt).toBe(1)
    const dogDashed = stepHorse({ ...clean, obstacles: [dog] }, false, true).state
    expect(dogDashed.phase).toBe('running')
    expect(dogDashed.obstacles[0].knockedDownAt).toBe(1)
    expect(stepHorse({ ...clean, obstacles: [netTower] }, false, true).state.phase).toBe('running')
    const sustained = stepHorse(dashed, false, true)
    expect(sustained.state.distance - dashed.distance).toBe(9)
  })

  it('switches both the tracked biome and obstacle pool as the haunted trail takes over', () => {
    const openingKinds = new Set(Array.from({ length: 150 }, (_, index) => startHorse(index + 1).obstacles.flat().map(obstacle => obstacle.kind)).flat())
    expect([...openingKinds].sort()).toEqual([
      'boulder', 'carriage', 'fallen-log', 'fence', 'lasso-thrower', 'net-tower', 'rolling-barrel',
    ])

    const hauntedKinds = new Set(Array.from({ length: 150 }, (_, index) => {
      const source = startHorse(index + 1)
      const ready = {
        ...source,
        tick: hauntedBiomeStartTick,
        platforms: source.platforms.filter(platform => platform.id === source.standing),
        obstacles: [],
        nextPlatform: 2,
        nextObstacle: 1,
      }
      return stepHorse(ready, false).state.obstacles.map(obstacle => obstacle.kind)
    }).flat())
    expect([...hauntedKinds].sort()).toEqual([
      'bat-flock', 'crate', 'crate-cluster', 'dog', 'oil-spill', 'pit', 'skeleton', 'well',
    ])
    const transitionSource = startHorse(7)
    const hauntedState = stepHorse({
      ...transitionSource,
      tick: hauntedBiomeTransitionStartTick,
      platforms: transitionSource.platforms.filter(platform => platform.id === 1),
      obstacles: [],
      nextPlatform: 2,
      nextObstacle: 1,
    }, false).state
    expect(hauntedState.biome).toBe('haunted')
    expect(hauntedState.obstacles.every(obstacle => obstacle.biome === 'haunted')).toBe(true)
    expect(hauntedBiomeBlend(0)).toBe(0)
    const transitionMidpoint = (hauntedBiomeTransitionStartTick + hauntedBiomeStartTick) / 2
    const hauntedAtMidpoint = hauntedBiomeBlend(transitionMidpoint)
    expect(hauntedAtMidpoint).toBeGreaterThan(0)
    expect(hauntedAtMidpoint).toBeLessThan(1)
    // The SVG derives the mountain layer from 1 - this value, so the two
    // landscapes always cover the scene together during the crossfade.
    expect((1 - hauntedAtMidpoint) + hauntedAtMidpoint).toBe(1)
    expect(hauntedBiomeBlend(hauntedBiomeStartTick)).toBe(1)
  })

  it('moves animal hazards toward the horse and keeps bats airborne', () => {
    const source = startHorse(31)
    const platform = source.platforms.find(item => item.id === source.standing)!
    const dog = {
      id: source.nextObstacle,
      biome: 'haunted' as const,
      kind: 'dog' as const,
      platformId: platform.id,
      x: 360,
      width: 58,
      height: 38,
      elevation: 0,
      additionalSpeed: 2.2,
    }
    const bats = {
      id: source.nextObstacle + 1,
      biome: 'haunted' as const,
      kind: 'bat-flock' as const,
      platformId: platform.id,
      x: 460,
      width: 64,
      height: 42,
      elevation: 88,
      additionalSpeed: 1.4,
    }

    const next = stepHorse({ ...source, obstacles: [...source.obstacles, dog, bats], nextObstacle: source.nextObstacle + 2 }, false).state

    expect(next.obstacles.find(obstacle => obstacle.id === dog.id)?.x).toBeCloseTo(dog.x - 7.2, 6)
    expect(next.obstacles.find(obstacle => obstacle.id === bats.id)?.x).toBeCloseTo(bats.x - 6.4, 6)
    expect(next.obstacles.find(obstacle => obstacle.id === bats.id)?.elevation).toBe(88)
  })
})
