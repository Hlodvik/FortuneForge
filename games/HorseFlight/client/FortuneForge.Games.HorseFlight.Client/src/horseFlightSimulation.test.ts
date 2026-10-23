import { describe, expect, it } from 'vitest'
import {
  biomeForTick,
  biomeSectionForTick,
  biomeTransitionDurationTicks,
  captureProjectileGeometry,
  currentRulesVersion,
  draftMultiBiomeRulesVersion,
  hauntedBiomeBlend,
  hauntedBiomeStartTick,
  hauntedBiomeTransitionStartTick,
  horseCollisionWidth,
  horseScreenX,
  minimumObstacleRecoveryTicks,
  obstacleHorizontalHitboxInset,
  startHorse,
  stepHorse,
  type HorseState,
  type HorseRulesVersion,
  type Obstacle,
  type ObstacleKind,
} from './horseFlightSimulation'

const biomeBoundaries = [
  { from: 'mountain', to: 'castle-town', start: 3_500, mainKinds: ['fence', 'hanging-sign', 'market-cart', 'rolling-barrel', 'well'], transitionKinds: ['fallen-log', 'fence', 'hanging-sign', 'market-cart'] },
  { from: 'castle-town', to: 'dark-forest', start: 7_000, mainKinds: ['bat-flock', 'boulder', 'fallen-log', 'thorn-bramble'], transitionKinds: ['fallen-log', 'hanging-sign', 'market-cart', 'thorn-bramble'] },
  { from: 'dark-forest', to: 'battleground', start: 10_500, mainKinds: ['boulder', 'lasso-thrower', 'net-tower', 'rolling-barrel', 'spike-barricade'], transitionKinds: ['fallen-log', 'lasso-thrower', 'spike-barricade', 'thorn-bramble'] },
  { from: 'battleground', to: 'haunted', start: 14_250, mainKinds: ['bat-flock', 'crate', 'crate-cluster', 'dog', 'oil-spill', 'pit', 'skeleton', 'well'], transitionKinds: ['bat-flock', 'net-tower', 'skeleton', 'spike-barricade'] },
  { from: 'haunted', to: 'desert', start: 17_500, mainKinds: ['boulder', 'cactus', 'pit', 'well'], transitionKinds: ['boulder', 'cactus', 'pit', 'skeleton'] },
  { from: 'desert', to: 'sky', start: 21_000, mainKinds: ['bat-flock', 'boulder', 'storm-cloud'], transitionKinds: ['bat-flock', 'boulder', 'cactus', 'storm-cloud'] },
] as const

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
    [1, 121, 160],
    [17, 120, 160],
    [42, 103, 51],
    [99, 119, 159],
    [123456789, 120, 160],
  ])('matches the server replay fixture for seed %i', (seed, expectedTick, expectedScore) => {
    let state = startHorse(seed, 3)
    while (state.phase === 'running') state = stepHorse(state, false).state

    expect(state).toMatchObject({ phase: 'obstacle-collision', tick: expectedTick, score: expectedScore })
    expect(state.terminalObstacleKind).not.toBeNull()
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

  it('leaves recovery time after each full hitbox clears, even with variable dash speed', () => {
    expect(16).toBeLessThan(minimumObstacleRecoveryTicks)

    for (let seed = 1; seed <= 100; seed++) {
      assertRecoveryWindows(startHorse(seed).obstacles)
      const source = startHorse(seed)
      const score = 3_000
      const late = stepHorse({
        ...source,
        platforms: source.platforms.filter(platform => platform.id === source.standing),
        obstacles: [],
        nextPlatform: 2,
        nextObstacle: 1,
        distance: score * 10,
        score,
      }, false).state
      assertRecoveryWindows(late.obstacles)
    }

    let jump = stepHorse(safeOpenRun(701), true).state
    let airborneTicks = 1
    while (!jump.grounded) {
      jump = stepHorse(jump, false).state
      airborneTicks++
    }
    expect(airborneTicks).toBeLessThan(minimumObstacleRecoveryTicks)
  })

  it('has a viable input pattern for every generated hazard in isolation', () => {
    const kinds = new Map<ObstacleKind, Obstacle>()
    const mainTicks = [4_250, 7_750, 11_250, 15_000, 18_250, 21_750]
    for (let seed = 1; seed <= 70; seed++) {
      for (const obstacle of startHorse(seed).obstacles) kinds.set(obstacle.kind, obstacle)
      for (const tick of mainTicks) {
        for (const obstacle of generatedObstaclesAt(seed, tick, draftMultiBiomeRulesVersion)) kinds.set(obstacle.kind, obstacle)
      }
    }
    expect(kinds.size).toBe(21)

    for (const [kind, shape] of kinds) {
      const source = safeOpenRun(701)
      const solo = {
        ...source,
        platforms: source.platforms.map(platform => platform.id === source.standing
          ? { ...platform, width: 10_000 }
          : platform),
        obstacles: [{ ...shape, platformId: source.standing!, x: 700 }],
      }
      const canPass = (lead: number, secondJumpDelay: number | null, dash: boolean): boolean => {
        let state = solo
        let firstJumpAt: number | null = null
        for (let tick = 0; tick < 180; tick++) {
          const distance = state.obstacles[0].x + obstacleHorizontalHitboxInset -
            (horseScreenX + horseCollisionWidth / 2)
          const jump = !dash && firstJumpAt === null && distance <= lead ||
            firstJumpAt !== null && secondJumpDelay !== null && tick === firstJumpAt + secondJumpDelay
          if (jump && firstJumpAt === null) firstJumpAt = tick
          state = stepHorse(state, jump, dash && distance <= lead).state
          if (state.phase !== 'running') return false
          if (state.obstacles.length === 0 ||
              state.obstacles[0].x + state.obstacles[0].width < horseScreenX - horseCollisionWidth / 2) return true
        }
        return false
      }
      const viable = canPass(-1, null, false) ||
        [15, 30, 45, 60, 75, 90].some(lead => canPass(lead, null, true)) ||
        [15, 30, 45, 60, 75, 90].some(lead =>
          [null, 8, 10, 12, 14, 16, 18, 20].some(delay => canPass(lead, delay, false)))
      expect(viable, `No safe maneuver found for ${kind}`).toBe(true)
    }
  })

  it('keeps free-play debug immunity and 3× speed out of normal simulation', () => {
    const source = safeOpenRun(701)
    const platform = source.platforms.find(item => item.id === source.standing)!
    const fence: Obstacle = {
      id: source.nextObstacle,
      biome: 'mountain',
      kind: 'fence',
      platformId: platform.id,
      x: horseScreenX + 10,
      width: 84,
      height: 42,
      elevation: 0,
      additionalSpeed: 0,
    }
    const contact = { ...source, obstacles: [fence], nextObstacle: fence.id + 1 }
    expect(stepHorse(contact, false).state.phase).toBe('obstacle-collision')
    expect(stepHorse(contact, false, false, { godMode: true }).state.phase).toBe('running')

    const ordinary = stepHorse(source, false)
    const accelerated = stepHorse(source, false, false, { godMode: true, speedMultiplier: 3 })
    const manual = stepHorse(stepHorse(ordinary.state, false).state, false)
    expect(accelerated.state).toEqual(manual.state)
    expect(accelerated.state.tick).toBe(source.tick + 3)
    expect(ordinary.state.tick).toBe(source.tick + 1)
    expect(stepHorse(source, false, false, { speedMultiplier: 3 }).state).toEqual(ordinary.state)
  })

  it('marks only a descending jump onto the boulder top for the rock skid', () => {
    const source = startHorse(29)
    const platform = source.platforms.find(item => item.id === source.standing)!
    const boulder: Obstacle = {
      id: 1, biome: 'mountain', kind: 'boulder', platformId: platform.id,
      x: horseScreenX - 10, width: 54, height: 36, elevation: 0, additionalSpeed: 0,
    }
    const base = { ...source, obstacles: [boulder], nextObstacle: 2 }
    const topLanding = stepHorse({
      ...base, horseY: platform.y - boulder.height + 4,
      velocity: 2, grounded: false, standing: null,
    }, false).state
    expect(topLanding.phase).toBe('obstacle-collision')
    expect(topLanding.terminalObstacleKind).toBe('boulder')
    expect(topLanding.terminalRockLanding).toBe(true)

    const groundedHit = stepHorse(base, false).state
    expect(groundedHit.phase).toBe('obstacle-collision')
    expect(groundedHit.terminalRockLanding).toBe(false)

    const sideHit = stepHorse({
      ...base, horseY: platform.y - 15,
      velocity: 2, grounded: false, standing: null,
    }, false).state
    expect(sideHit.phase).toBe('obstacle-collision')
    expect(sideHit.terminalRockLanding).toBe(false)

    const risingHit = stepHorse({
      ...base, horseY: platform.y - 15,
      velocity: -2, grounded: false, standing: null,
    }, false).state
    expect(risingHit.phase).toBe('obstacle-collision')
    expect(risingHit.terminalRockLanding).toBe(false)
  })

  it('uses a slightly inset obstacle hitbox, sustains a dash, and knocks down dashable enemies', () => {
    // Existing ranked replays retain their original body-hitbox rules.
    const source = startHorse(29, 3)
    const platform = source.platforms.find(item => item.id === source.standing)!
    const clean = { ...source, obstacles: [], nextObstacle: 2 }
    const edgeCrate = {
      id: 1,
      biome: 'mountain' as const,
      kind: 'crate' as const,
      platformId: platform.id,
      x: horseScreenX + horseCollisionWidth / 2,
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

    const edge = stepHorse({ ...clean, obstacles: [edgeCrate] }, false).state
    expect(edge.phase).toBe('running')
    expect(edge.terminalObstacleKind).toBeNull()
    const lassoContact = stepHorse({ ...clean, obstacles: [lasso] }, false).state
    expect(lassoContact.phase).toBe('obstacle-collision')
    expect(lassoContact.terminalObstacleKind).toBe('lasso-thrower')
    const netContact = stepHorse({ ...clean, obstacles: [netTower] }, false).state
    expect(netContact.phase).toBe('obstacle-collision')
    expect(netContact.terminalObstacleKind).toBe('net-tower')

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

  it('flies a capture projectile from windup to a ground miss without changing the obstacle', () => {
    const obstacle: Obstacle = {
      id: 7, biome: 'mountain', kind: 'lasso-thrower', platformId: 1,
      x: horseScreenX + 300, width: 76, height: 64, elevation: 0, additionalSpeed: .8,
    }
    const original = { ...obstacle }
    const winding = captureProjectileGeometry(obstacle, 460)
    const flying = captureProjectileGeometry({ ...obstacle, x: horseScreenX + 100 }, 460)
    const missed = captureProjectileGeometry({ ...obstacle, x: horseScreenX - 80 }, 460)
    const caught = captureProjectileGeometry({ ...obstacle, x: horseScreenX + 20 }, 460, true, 445)

    expect(winding).toMatchObject({ phase: 'winding', progress: 0, canCollide: false })
    expect(flying).toMatchObject({ phase: 'flying', canCollide: true })
    expect(flying!.x).toBeLessThan(winding!.x)
    expect(missed).toMatchObject({ phase: 'grounded', canCollide: false, y: 457 })
    expect(missed!.x).toBeLessThan(horseScreenX)
    expect(caught).toMatchObject({ phase: 'caught', x: horseScreenX, y: 418, canCollide: false })
    expect(captureProjectileGeometry({ ...obstacle, kind: 'fence' }, 460)).toBeNull()
    expect(obstacle).toEqual(original)
  })

  it('uses the thrown lasso or net as the v4 hazard, even when the thrower body is elsewhere', () => {
    for (const kind of ['lasso-thrower', 'net-tower'] as const) {
      const source = safeOpenRun(701)
      const obstacle: Obstacle = {
        id: source.nextObstacle, biome: 'mountain', kind, platformId: 1,
        x: horseScreenX + 25,
        width: kind === 'lasso-thrower' ? 76 : 92,
        height: kind === 'lasso-thrower' ? 64 : 108,
        elevation: 0,
        additionalSpeed: kind === 'lasso-thrower' ? .8 : 0,
      }
      const incoming = { ...source, obstacles: [obstacle] }
      const caught = stepHorse(incoming, false).state
      expect(caught.phase, `${kind} projectile should catch a grounded horse`).toBe('obstacle-collision')
      expect(caught.terminalObstacleKind).toBe(kind)
      expect(caught.terminalObstacleId).toBe(obstacle.id)
      expect(caught.obstacles[0].x + obstacleHorizontalHitboxInset).toBeGreaterThan(
        horseScreenX + horseCollisionWidth / 2)

      const airborne = { ...incoming, horseY: 380, velocity: 0, grounded: false, standing: null }
      expect(stepHorse(airborne, false).state.phase, `${kind} should miss a jumping horse`).toBe('running')
      expect(stepHorse(incoming, false, true).state.phase, `${kind} should miss a ducking horse`).toBe('running')

      const bodyOverlapsAfterMiss = { ...obstacle, x: horseScreenX - 50 }
      const escaped = stepHorse({ ...source, obstacles: [bodyOverlapsAfterMiss] }, false).state
      expect(escaped.phase, `${kind} body should not catch after a projectile miss`).toBe('running')

      const oldReplay = stepHorse({ ...safeOpenRun(701), rulesVersion: 3, obstacles: [bodyOverlapsAfterMiss] }, false).state
      expect(oldReplay.phase, `${kind} replay v3 collision must remain unchanged`).toBe('obstacle-collision')
    }
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

  it('exposes every 750-tick transition and its destination biome at exact boundaries', () => {
    expect(biomeSectionForTick(0, draftMultiBiomeRulesVersion)).toEqual({ phase: 'main', biome: 'mountain', from: 'mountain', to: 'mountain', blend: 1 })
    for (const boundary of biomeBoundaries) {
      expect(biomeSectionForTick(boundary.start - 1, draftMultiBiomeRulesVersion)).toMatchObject({ phase: 'main', biome: boundary.from })
      expect(biomeSectionForTick(boundary.start, draftMultiBiomeRulesVersion)).toEqual({
        phase: 'transition', biome: boundary.to, from: boundary.from, to: boundary.to, blend: 0,
      })
      expect(biomeSectionForTick(boundary.start + 375, draftMultiBiomeRulesVersion).blend).toBe(.5)
      expect(biomeSectionForTick(boundary.start + biomeTransitionDurationTicks - 1, draftMultiBiomeRulesVersion)).toMatchObject({ phase: 'transition', biome: boundary.to })
      expect(biomeSectionForTick(boundary.start + biomeTransitionDurationTicks, draftMultiBiomeRulesVersion)).toEqual({
        phase: 'main', biome: boundary.to, from: boundary.to, to: boundary.to, blend: 1,
      })
      expect(biomeForTick(boundary.start, draftMultiBiomeRulesVersion)).toBe(boundary.to)
    }
    expect(biomeSectionForTick(30_000, draftMultiBiomeRulesVersion).biome).toBe('sky')
    expect(() => biomeSectionForTick(-1)).toThrow(RangeError)
  })

  it('keeps new runs in the existing mountain and haunted worlds only', () => {
    expect(startHorse(42).rulesVersion).toBe(currentRulesVersion)
    for (const tick of [0, 3_500, 10_500, hauntedBiomeTransitionStartTick - 1]) {
      expect(biomeSectionForTick(tick).biome).toBe('mountain')
    }
    expect(biomeSectionForTick(hauntedBiomeTransitionStartTick)).toEqual({
      phase: 'transition', biome: 'haunted', from: 'mountain', to: 'haunted', blend: 0,
    })
    expect(biomeSectionForTick(hauntedBiomeTransitionStartTick + 375).blend).toBe(.5)
    for (const tick of [hauntedBiomeStartTick, 17_500, 21_000, 30_000]) {
      expect(biomeSectionForTick(tick).biome).toBe('haunted')
    }
    const mountainKinds = ['boulder', 'carriage', 'fallen-log', 'fence', 'lasso-thrower', 'net-tower', 'rolling-barrel']
    const hauntedKinds = ['bat-flock', 'crate', 'crate-cluster', 'dog', 'oil-spill', 'pit', 'skeleton', 'well']
    for (const [tick, expected] of [
      [3_500, mountainKinds], [10_500, mountainKinds],
      [14_250, hauntedKinds], [17_500, hauntedKinds], [21_750, hauntedKinds],
    ] as const) {
      const generated = Array.from({ length: 150 }, (_, index) => generatedObstaclesAt(index + 1, tick)).flat()
      expect([...new Set(generated.map(obstacle => obstacle.kind))].sort()).toEqual(expected)
      for (let seed = 1; seed <= 12; seed++) assertRecoveryWindows(generatedObstaclesAt(seed, tick))
    }
  })

  it('generates each region and border pool with the correct elevated hazards and recovery time', () => {
    const sampledKinds = (tick: number) => new Set(
      Array.from({ length: 150 }, (_, index) => generatedObstaclesAt(index + 1, tick, draftMultiBiomeRulesVersion))
        .flat().map(obstacle => obstacle.kind))

    for (const boundary of biomeBoundaries) {
      expect([...sampledKinds(boundary.start)].sort()).toEqual([...boundary.transitionKinds])
      expect([...sampledKinds(boundary.start + biomeTransitionDurationTicks)].sort()).toEqual([...boundary.mainKinds])
      for (let seed = 1; seed <= 12; seed++) {
        assertRecoveryWindows(generatedObstaclesAt(seed, boundary.start, draftMultiBiomeRulesVersion))
        assertRecoveryWindows(generatedObstaclesAt(seed, boundary.start + biomeTransitionDurationTicks, draftMultiBiomeRulesVersion))
      }
    }

    expect(Array.from({ length: 20 }, (_, index) => generatedObstaclesAt(index + 1, 4_250, draftMultiBiomeRulesVersion))
      .flat().some(obstacle => obstacle.kind === 'hanging-sign' && obstacle.elevation === 58)).toBe(true)
    expect(Array.from({ length: 20 }, (_, index) => generatedObstaclesAt(index + 1, 21_750, draftMultiBiomeRulesVersion))
      .flat().some(obstacle => obstacle.kind === 'storm-cloud' && obstacle.elevation === 76 && obstacle.additionalSpeed === 1.4)).toBe(true)
  })

  it('matches shared seed-42 obstacle fixtures across all new sections', () => {
    const fixtures = [
      [3_500, ['market-cart', 'fallen-log', 'fence'], 1_504.161469],
      [4_250, ['rolling-barrel', 'hanging-sign', 'market-cart'], 1_342.233541],
      [7_000, ['thorn-bramble', 'hanging-sign', 'market-cart'], 1_504.161469],
      [7_750, ['fallen-log', 'boulder', 'thorn-bramble'], 1_508.161469],
      [10_500, ['spike-barricade', 'fallen-log', 'thorn-bramble'], 1_516.161469],
      [11_250, ['net-tower', 'boulder', 'spike-barricade'], 1_520.161469],
      [14_250, ['bat-flock', 'net-tower', 'spike-barricade'], 1_414.127110],
      [15_000, ['dog', 'pit', 'crate-cluster'], 1_371.967891],
      [17_500, ['cactus', 'skeleton', 'pit'], 1_677.116993],
      [18_250, ['pit', 'boulder', 'cactus'], 1_508.161469],
      [21_000, ['storm-cloud', 'boulder', 'cactus'], 1_419.449691],
      [21_750, ['bat-flock', 'storm-cloud', 'storm-cloud'], 1_549.561469],
    ] as const
    for (const [tick, kinds, secondX] of fixtures) {
      const obstacles = generatedObstaclesAt(42, tick, draftMultiBiomeRulesVersion)
      expect(obstacles.map(obstacle => obstacle.kind)).toEqual([...kinds])
      expect(obstacles[1].x).toBeCloseTo(secondX, 5)
    }
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

function safeOpenRun(seed: number): HorseState {
  const state = startHorse(seed)
  const y = state.horseY
  return {
    ...state,
    platforms: [
      { id: 1, x: 0, width: 10_000, y, cleared: false },
      { id: 2, x: 20_000, width: 10, y, cleared: false },
      { id: 3, x: 20_020, width: 10, y, cleared: false },
      { id: 4, x: 20_040, width: 10, y, cleared: false },
    ],
    obstacles: [],
    nextPlatform: 5,
    nextObstacle: 1,
  }
}

function generatedObstaclesAt(seed: number, tick: number, rulesVersion: HorseRulesVersion = currentRulesVersion): Obstacle[] {
  const source = startHorse(seed, rulesVersion)
  return stepHorse({
    ...source,
    tick,
    platforms: source.platforms.filter(platform => platform.id === source.standing),
    obstacles: [],
    nextPlatform: 2,
    nextObstacle: 1,
  }, false).state.obstacles
}

function assertRecoveryWindows(obstacles: readonly Obstacle[]): void {
  const generated = [...obstacles].sort((left, right) => left.id - right.id)
  const speedPatterns = [
    (_tick: number) => 5,
    (_tick: number) => 11,
    (tick: number) => tick % 16 < 8 ? 11 : 5,
    (tick: number) => tick % 40 < 16 ? 11 : 5,
    (tick: number) => tick % 40 < 16 ? 5 : 11,
  ]
  for (const speedAt of speedPatterns) {
    let travel = 0
    const exits = new Map<number, number>()
    const entries = new Map<number, number>()
    for (let tick = 0; tick < 3_000 && exits.size < generated.length; tick++) {
      travel += speedAt(tick)
      for (const obstacle of generated) {
        const x = obstacle.x - travel - obstacle.additionalSpeed * (tick + 1)
        if (!entries.has(obstacle.id) && x + obstacleHorizontalHitboxInset < horseScreenX + horseCollisionWidth / 2) {
          entries.set(obstacle.id, tick + 1)
        }
        if (!exits.has(obstacle.id) && x + obstacle.width - obstacleHorizontalHitboxInset <= horseScreenX - horseCollisionWidth / 2) {
          exits.set(obstacle.id, tick + 1)
        }
      }
    }
    expect(exits.size).toBe(generated.length)
    for (let index = 1; index < generated.length; index++) {
      expect(entries.get(generated[index].id)! - exits.get(generated[index - 1].id)!).toBeGreaterThanOrEqual(
        minimumObstacleRecoveryTicks)
    }
  }
}
