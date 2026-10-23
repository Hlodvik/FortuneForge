export type Platform = {
  id: number
  x: number
  width: number
  y: number
  cleared: boolean
}

export type ObstacleKind =
  | 'crate'
  | 'crate-cluster'
  | 'pit'
  | 'well'
  | 'fence'
  | 'carriage'
  | 'oil-spill'
  | 'boulder'
  | 'fallen-log'
  | 'dog'
  | 'bat-flock'
  | 'lasso-thrower'
  | 'net-tower'
  | 'skeleton'
  | 'rolling-barrel'
  | 'market-cart'
  | 'hanging-sign'
  | 'thorn-bramble'
  | 'spike-barricade'
  | 'cactus'
  | 'storm-cloud'

export type HorseBiome = 'mountain' | 'castle-town' | 'dark-forest' | 'battleground' | 'haunted' | 'desert' | 'sky'
export type HorseRulesVersion = 2 | 3 | 4
export const draftMultiBiomeRulesVersion: HorseRulesVersion = 2
export const currentRulesVersion: HorseRulesVersion = 4
export type HorseBiomeSection = Readonly<{
  phase: 'main'
  biome: HorseBiome
  from: HorseBiome
  to: HorseBiome
  blend: 1
}> | Readonly<{
  phase: 'transition'
  biome: Exclude<HorseBiome, 'mountain'>
  from: HorseBiome
  to: Exclude<HorseBiome, 'mountain'>
  blend: number
}>

export type Obstacle = {
  id: number
  biome: HorseBiome
  kind: ObstacleKind
  platformId: number
  x: number
  width: number
  height: number
  elevation: number
  additionalSpeed: number
  knockedDownAt?: number
}

export type CaptureProjectileGeometry = Readonly<{
  phase: 'winding' | 'flying' | 'caught' | 'grounded'
  x: number
  y: number
  width: number
  height: number
  progress: number
  canCollide: boolean
}>

export type HorseState = {
  rulesVersion: HorseRulesVersion
  biome: HorseBiome
  seed: number
  random: number
  horseY: number
  velocity: number
  grounded: boolean
  jumpsRemaining: number
  slideTicksRemaining: number
  standing: number | null
  platforms: Platform[]
  obstacles: Obstacle[]
  nextPlatform: number
  nextObstacle: number
  distance: number
  cleared: number
  score: number
  tick: number
  phase: 'running' | 'obstacle-collision' | 'fell'
  terminalObstacleKind: ObstacleKind | null
  terminalObstacleId: number | null
  terminalRockLanding: boolean
}

// Local free-play controls only. Ranked input replays never include these
// options and are always evaluated by the server's unmodified simulation.
export type HorseDebugOptions = Readonly<{
  godMode?: boolean
  speedMultiplier?: 3
}>

type ObstacleDefinition = Readonly<{
  kind: ObstacleKind
  width: number
  height: number
  elevation: number
  additionalSpeed: number
}>

const W = 960
const H = 540
export const horseScreenX = 336
const horseX = horseScreenX
const horseWidth = 48
const horseHeight = 40
const horseLeft = horseX - horseWidth / 2
const horseRight = horseX + horseWidth / 2
export const horseCollisionWidth = 38
const horseCollisionHeight = 32
const slideHorseCollisionHeight = 14
const horseCollisionBottomInset = 2
const rockLandingTopTolerance = 3
const horseCollisionLeft = horseX - horseCollisionWidth / 2
const horseCollisionRight = horseX + horseCollisionWidth / 2
const projectileLandingDistance = 48
const captureProjectileProfiles = {
  'lasso-thrower': {
    launchDistance: 200,
    contactDistance: -15,
    sourceXFraction: .74,
    sourceYOffset: 47,
    targetYOffset: 27,
    arcHeight: 12,
    width: 27,
    height: 18,
  },
  'net-tower': {
    launchDistance: 260,
    contactDistance: -20,
    sourceXFraction: .48,
    sourceYOffset: 92,
    targetYOffset: 31,
    arcHeight: 4,
    width: 42,
    height: 22,
  },
} as const
const maximumJumps = 2
const slideDurationTicks = 16
const slideSpeedBonus = 4
const fastFallVelocity = 16
const fenceSlideClearance = 24
const levelsPerAdditionalObstacle = 3
const maximumObstaclesPerPlatform = 3
const obstacleClearance = 18
const maximumObstacleWidth = 92
export const obstacleHorizontalHitboxInset = 5
const obstacleVerticalHitboxInset = 3
export const minimumObstacleRecoveryTicks = 40
const initialPlatformWidth = 900
export const biomeTransitionDurationTicks = 750
export const castleTownTransitionStartTick = 3_500
export const darkForestTransitionStartTick = 7_000
export const battlegroundTransitionStartTick = 10_500
export const hauntedBiomeTransitionStartTick = 14_250
export const hauntedBiomeStartTick = 15_000
export const desertTransitionStartTick = 17_500
export const skyTransitionStartTick = 21_000

const biomeTransitions: readonly Readonly<{ from: HorseBiome; to: Exclude<HorseBiome, 'mountain'>; startTick: number }>[] = [
  { from: 'mountain', to: 'castle-town', startTick: castleTownTransitionStartTick },
  { from: 'castle-town', to: 'dark-forest', startTick: darkForestTransitionStartTick },
  { from: 'dark-forest', to: 'battleground', startTick: battlegroundTransitionStartTick },
  { from: 'battleground', to: 'haunted', startTick: hauntedBiomeTransitionStartTick },
  { from: 'haunted', to: 'desert', startTick: desertTransitionStartTick },
  { from: 'desert', to: 'sky', startTick: skyTransitionStartTick },
]
// Every obstacle belongs to one world. The mountain pass only receives
// physical trail hazards; the haunted trail owns the tombstones, grave goo,
// pits, and undead hazards. This prevents one scene's story from leaking
// into another as new platforms are generated.
const mountainObstacleDefinitions: readonly ObstacleDefinition[] = [
  { kind: 'fence', width: 84, height: 42, elevation: 0, additionalSpeed: 0 },
  { kind: 'carriage', width: 72, height: 48, elevation: 0, additionalSpeed: 0 },
  { kind: 'boulder', width: 54, height: 36, elevation: 0, additionalSpeed: 0 },
  { kind: 'fallen-log', width: 80, height: 40, elevation: 0, additionalSpeed: 0 },
  { kind: 'lasso-thrower', width: 76, height: 64, elevation: 0, additionalSpeed: 0.8 },
  { kind: 'net-tower', width: 92, height: 108, elevation: 0, additionalSpeed: 0 },
  { kind: 'rolling-barrel', width: 42, height: 42, elevation: 0, additionalSpeed: 2.6 },
]

const hauntedObstacleDefinitions: readonly ObstacleDefinition[] = [
  { kind: 'crate', width: 38, height: 34, elevation: 0, additionalSpeed: 0 },
  { kind: 'crate-cluster', width: 50, height: 60, elevation: 0, additionalSpeed: 0 },
  { kind: 'pit', width: 80, height: 32, elevation: 0, additionalSpeed: 0 },
  { kind: 'well', width: 56, height: 52, elevation: 0, additionalSpeed: 0 },
  { kind: 'oil-spill', width: 78, height: 12, elevation: 0, additionalSpeed: 0 },
  { kind: 'dog', width: 58, height: 38, elevation: 0, additionalSpeed: 2.2 },
  { kind: 'bat-flock', width: 64, height: 42, elevation: 88, additionalSpeed: 1.4 },
  { kind: 'skeleton', width: 42, height: 52, elevation: 0, additionalSpeed: 1.1 },
]

const castleTownObstacleDefinitions: readonly ObstacleDefinition[] = [
  { kind: 'market-cart', width: 76, height: 52, elevation: 0, additionalSpeed: 0 },
  { kind: 'hanging-sign', width: 74, height: 42, elevation: 58, additionalSpeed: 0 },
  mountainObstacleDefinitions[0], // fence
  mountainObstacleDefinitions[6], // rolling barrel
  hauntedObstacleDefinitions[3], // well
]

const darkForestObstacleDefinitions: readonly ObstacleDefinition[] = [
  { kind: 'thorn-bramble', width: 76, height: 42, elevation: 0, additionalSpeed: 0 },
  mountainObstacleDefinitions[2], // boulder
  mountainObstacleDefinitions[3], // fallen log
  hauntedObstacleDefinitions[6], // bats
]

const battlegroundObstacleDefinitions: readonly ObstacleDefinition[] = [
  { kind: 'spike-barricade', width: 88, height: 46, elevation: 0, additionalSpeed: 0 },
  mountainObstacleDefinitions[2], // boulder
  mountainObstacleDefinitions[4], // lasso thrower
  mountainObstacleDefinitions[5], // net tower
  mountainObstacleDefinitions[6], // rolling barrel
]

const desertObstacleDefinitions: readonly ObstacleDefinition[] = [
  { kind: 'cactus', width: 54, height: 60, elevation: 0, additionalSpeed: 0 },
  mountainObstacleDefinitions[2], // boulder
  hauntedObstacleDefinitions[2], // pit
  hauntedObstacleDefinitions[3], // well
]

const skyObstacleDefinitions: readonly ObstacleDefinition[] = [
  { kind: 'storm-cloud', width: 70, height: 38, elevation: 76, additionalSpeed: 1.4 },
  mountainObstacleDefinitions[2], // boulder
  hauntedObstacleDefinitions[6], // bats
]

const obstacleDefinitionsByBiome: Readonly<Record<HorseBiome, readonly ObstacleDefinition[]>> = {
  mountain: mountainObstacleDefinitions,
  'castle-town': castleTownObstacleDefinitions,
  'dark-forest': darkForestObstacleDefinitions,
  battleground: battlegroundObstacleDefinitions,
  haunted: hauntedObstacleDefinitions,
  desert: desertObstacleDefinitions,
  sky: skyObstacleDefinitions,
}

// These short border sections introduce hazards from both adjoining regions.
const transitionObstacleDefinitions: Readonly<Record<Exclude<HorseBiome, 'mountain'>, readonly ObstacleDefinition[]>> = {
  'castle-town': [mountainObstacleDefinitions[0], mountainObstacleDefinitions[3], castleTownObstacleDefinitions[0], castleTownObstacleDefinitions[1]],
  'dark-forest': [castleTownObstacleDefinitions[0], castleTownObstacleDefinitions[1], darkForestObstacleDefinitions[0], darkForestObstacleDefinitions[2]],
  battleground: [darkForestObstacleDefinitions[0], darkForestObstacleDefinitions[2], battlegroundObstacleDefinitions[0], battlegroundObstacleDefinitions[2]],
  haunted: [battlegroundObstacleDefinitions[0], battlegroundObstacleDefinitions[3], hauntedObstacleDefinitions[6], hauntedObstacleDefinitions[7]],
  desert: [hauntedObstacleDefinitions[2], hauntedObstacleDefinitions[7], desertObstacleDefinitions[0], desertObstacleDefinitions[1]],
  sky: [desertObstacleDefinitions[0], desertObstacleDefinitions[1], skyObstacleDefinitions[0], skyObstacleDefinitions[2]],
}

export function startHorse(seed: number, rulesVersion: HorseRulesVersion = currentRulesVersion): HorseState {
  const state: HorseState = {
    rulesVersion,
    biome: 'mountain',
    seed,
    random: seed || 0x9e3779b9,
    horseY: H - 80,
    velocity: 0,
    grounded: true,
    jumpsRemaining: maximumJumps,
    slideTicksRemaining: 0,
    standing: 1,
    platforms: [{ id: 1, x: 0, width: initialPlatformWidth, y: H - 80, cleared: false }],
    obstacles: [],
    nextPlatform: 2,
    nextObstacle: 1,
    distance: 0,
    cleared: 0,
    score: 0,
    tick: 0,
    phase: 'running',
    terminalObstacleKind: null,
    terminalObstacleId: null,
    terminalRockLanding: false,
  }
  fill(state)
  return state
}

export function stepHorse(source: HorseState, wantsJump: boolean, wantsRightClick = false, debug?: HorseDebugOptions): { state: HorseState; jumped: boolean; slid: boolean; fastFell: boolean } {
  if (debug?.godMode && debug.speedMultiplier === 3) {
    const normalDebug = { godMode: debug.godMode }
    const first = stepHorse(source, wantsJump, wantsRightClick, normalDebug)
    let last = first
    for (let step = 1; step < 3 && last.state.phase === 'running'; step++) {
      last = stepHorse(last.state, false, false, normalDebug)
    }
    return { state: last.state, jumped: first.jumped, slid: first.slid, fastFell: first.fastFell }
  }
  const state = copy(source)
  const level = 1 + Math.floor(state.score / 500)
  const slideStartsThisTick = wantsRightClick && state.grounded
  const isSlidingThisTick = state.slideTicksRemaining > 0 || slideStartsThisTick
  const speed = Math.min(7, 5 + (level - 1) * 0.2) + (isSlidingThisTick ? slideSpeedBonus : 0)
  let newlyCleared = 0

  state.platforms = state.platforms
    .map(platform => {
      const moved = { ...platform, x: platform.x - speed }
      if (!moved.cleared && moved.x + moved.width < horseLeft) {
        moved.cleared = true
        newlyCleared++
      }
      return moved
    })
    .filter(platform => platform.x + platform.width > 0)
  state.obstacles = state.obstacles
    .map(obstacle => ({ ...obstacle, x: obstacle.x - speed - obstacle.additionalSpeed }))
    .filter(obstacle => obstacle.x + obstacle.width > 0)
  fill(state)

  const support = state.grounded
    ? state.platforms.find(platform =>
      overlaps(platform) &&
      Math.abs(platform.y - state.horseY) <= 1e-6)
    : undefined
  state.grounded = !!support
  state.standing = support?.id ?? null
  if (state.grounded) state.jumpsRemaining = maximumJumps

  const jumped = wantsJump && state.jumpsRemaining > 0
  const slid = wantsRightClick && state.grounded
  const fastFell = wantsRightClick && !state.grounded
  if (jumped) {
    state.grounded = false
    state.standing = null
    state.jumpsRemaining--
    state.slideTicksRemaining = 0
    state.velocity = -12.5
    state.horseY += state.velocity
  } else if (state.grounded) {
    if (slid) state.slideTicksRemaining = slideDurationTicks
    else if (state.slideTicksRemaining > 0) state.slideTicksRemaining--
    state.velocity = 0
    state.horseY = support!.y
  } else {
    state.slideTicksRemaining = 0
    state.velocity = fastFell
      ? Math.max(state.velocity, fastFallVelocity)
      : state.velocity + 0.9
    let candidate = state.horseY + state.velocity
    if (state.velocity >= 0) {
      const landing = state.platforms
        .filter(overlaps)
        .filter(platform => state.horseY <= platform.y + 1e-6 && candidate >= platform.y)
        .sort((a, b) => a.y - b.y)[0]
      if (landing) {
        candidate = landing.y
        state.velocity = 0
        state.grounded = true
        state.standing = landing.id
        state.jumpsRemaining = maximumJumps
      }
    }
    state.horseY = candidate
  }

  state.distance += speed
  state.cleared += newlyCleared
  state.score = Math.floor(state.distance / 10) + state.cleared * 100
  state.tick++
  state.biome = biomeForTick(state.tick, state.rulesVersion)
  const collisionHorseHeight = isSlidingThisTick && state.grounded
    ? slideHorseCollisionHeight
    : horseCollisionHeight
  const dashedObstacleId = isSlidingThisTick && state.grounded
    ? obstacleHitByDash(state, collisionHorseHeight)
    : null
  if (dashedObstacleId !== null) {
    state.obstacles = state.obstacles.map(obstacle => obstacle.id === dashedObstacleId
      ? { ...obstacle, knockedDownAt: state.tick }
      : obstacle)
  }
  const terminalObstacle = debug?.godMode ? null : collidingObstacle(state, collisionHorseHeight)
  state.phase = terminalObstacle !== null
    ? 'obstacle-collision'
    : state.horseY - horseHeight > H
      ? 'fell'
      : 'running'
  state.terminalObstacleKind = terminalObstacle?.kind ?? null
  state.terminalObstacleId = terminalObstacle?.id ?? null
  const rockPlatform = terminalObstacle?.kind === 'boulder'
    ? state.platforms.find(platform => platform.id === terminalObstacle.platformId)
    : undefined
  const rockTop = rockPlatform && terminalObstacle
    ? rockPlatform.y - terminalObstacle.elevation - terminalObstacle.height + obstacleVerticalHitboxInset
    : null
  // A descending jump crossing the rock's top skids forward. Entering its
  // side from below, or striking it while grounded, keeps the normal impact.
  state.terminalRockLanding = rockTop !== null && !source.grounded && state.velocity > 0 &&
    source.horseY - horseCollisionBottomInset <= rockTop + rockLandingTopTolerance &&
    state.horseY - horseCollisionBottomInset > rockTop
  return { state, jumped, slid, fastFell }
}

function fill(state: HorseState) {
  const level = 1 + Math.floor(state.score / 500)
  const section = biomeSectionForTick(state.tick, state.rulesVersion)
  const biome = section.biome
  const eligibleDefinitions = state.rulesVersion === draftMultiBiomeRulesVersion
    ? section.phase === 'transition'
      ? transitionObstacleDefinitions[section.to]
      : obstacleDefinitionsByBiome[biome]
    : biome === 'haunted'
      ? hauntedObstacleDefinitions
      : mountainObstacleDefinitions
  while (state.platforms.length < 4 || Math.max(...state.platforms.map(platform => platform.x + platform.width)) < W + 420) {
    const previous = state.platforms.reduce(
      (right, platform) => !right || platform.x + platform.width > right.x + right.width ? platform : right,
      undefined as Platform | undefined)
    const obstacleSlots = Math.min(
      maximumObstaclesPerPlatform,
      1 + Math.floor((level - 1) / levelsPerAdditionalObstacle))
    const width = 260 + next(state) * 180
    const platform: Platform = {
      id: state.nextPlatform++,
      x: previous ? previous.x + previous.width : 0,
      width,
      y: H - 80,
      cleared: false,
    }
    state.platforms.push(platform)
    for (let slot = 0; slot < obstacleSlots; slot++) {
      const definition = eligibleDefinitions[Math.min(
        Math.floor(next(state) * eligibleDefinitions.length),
        eligibleDefinitions.length - 1)]
      const naturalX = platform.x + 40 +
        slot * (maximumObstacleWidth + obstacleClearance) +
        next(state) * obstacleClearance
      const x = state.obstacles.reduce(
        (requiredX, previousObstacle) => Math.max(
          requiredX,
          requiredFollowingObstacleX(previousObstacle, definition)),
        naturalX)
      const obstacle: Obstacle = {
        id: state.nextObstacle++,
        biome,
        kind: definition.kind,
        platformId: platform.id,
        x,
        width: definition.width,
        height: definition.height,
        elevation: definition.elevation,
        additionalSpeed: definition.additionalSpeed,
      }
      if (!hasRecoveryWindowAfter(state.obstacles, obstacle)) {
        throw new Error('Horse Flight generated an obstacle sequence without a clear recovery window.')
      }
      state.obstacles.push(obstacle)
      platform.width = Math.max(platform.width, obstacle.x + obstacle.width + 40 - platform.x)
    }
  }
}

function next(state: HorseState) {
  let random = state.random >>> 0
  random ^= random << 13
  random ^= random >>> 17
  random ^= random << 5
  state.random = random >>> 0
  return state.random / 4294967296
}

function overlaps(platform: Platform) {
  return platform.x < horseRight && platform.x + platform.width > horseLeft
}

/**
 * The capture hazards are projectiles, not solid thrower bodies. Their flight
 * is derived from the obstacle's position, so rendering and collision observe
 * the same instant without adding random state to ranked replay inputs.
 */
export function captureProjectileGeometry(
  obstacle: Obstacle,
  platformY: number,
  caught = false,
  horseY = platformY,
): CaptureProjectileGeometry | null {
  if (obstacle.kind !== 'lasso-thrower' && obstacle.kind !== 'net-tower') return null
  const profile = captureProjectileProfiles[obstacle.kind]
  const distance = obstacle.x - horseScreenX
  const startX = obstacle.x + obstacle.width * profile.sourceXFraction
  const startY = platformY - profile.sourceYOffset
  const targetY = platformY - profile.targetYOffset
  if (caught) {
    return {
      phase: 'caught', x: horseScreenX, y: horseY - profile.targetYOffset,
      width: profile.width, height: profile.height, progress: 1, canCollide: false,
    }
  }
  if (distance > profile.launchDistance) {
    return {
      phase: 'winding', x: startX, y: startY,
      width: profile.width, height: profile.height, progress: 0, canCollide: false,
    }
  }
  if (distance <= profile.contactDistance) {
    const missedDistance = profile.contactDistance - distance
    const landingProgress = Math.min(1, missedDistance / projectileLandingDistance)
    return {
      phase: landingProgress === 1 ? 'grounded' : 'flying',
      x: horseScreenX - missedDistance,
      y: targetY + (platformY - 3 - targetY) * landingProgress,
      width: profile.width, height: profile.height, progress: 1, canCollide: false,
    }
  }
  const progress = (profile.launchDistance - distance) /
    (profile.launchDistance - profile.contactDistance)
  return {
    phase: 'flying',
    x: startX + (horseScreenX - startX) * progress,
    y: startY + (targetY - startY) * progress - profile.arcHeight * Math.sin(Math.PI * progress),
    width: profile.width,
    height: profile.height,
    progress,
    canCollide: true,
  }
}

function collidingObstacle(state: HorseState, horseCollisionHeight: number): Obstacle | null {
  const y = new Map(state.platforms.map(platform => [platform.id, platform.y]))
  const horseBottom = state.horseY - horseCollisionBottomInset
  const top = horseBottom - horseCollisionHeight
  const collision = state.obstacles.find(obstacle => {
    if (obstacle.knockedDownAt !== undefined) return false
    if (state.rulesVersion >= 4 && (obstacle.kind === 'lasso-thrower' || obstacle.kind === 'net-tower')) {
      const platformY = y.get(obstacle.platformId)
      if (platformY === undefined) return false
      const projectile = captureProjectileGeometry(obstacle, platformY)
      return projectile?.canCollide === true &&
        projectile.x - projectile.width / 2 < horseCollisionRight &&
        projectile.x + projectile.width / 2 > horseCollisionLeft &&
        projectile.y - projectile.height / 2 < horseBottom &&
        projectile.y + projectile.height / 2 > top
    }
    const bottom = y.get(obstacle.platformId)! - obstacle.elevation -
      (obstacle.kind === 'fence' ? fenceSlideClearance : 0)
    const obstacleLeft = obstacle.x + obstacleHorizontalHitboxInset
    const obstacleRight = obstacle.x + obstacle.width - obstacleHorizontalHitboxInset
    // The tower itself is scenery; its falling net is the low hazard that
    // requires a dash to duck beneath.
    if (obstacle.kind === 'net-tower' && horseCollisionHeight <= slideHorseCollisionHeight) return false
    const collisionHeight = obstacle.kind === 'net-tower' ? 24 : obstacle.height
    const obstacleTop = bottom - collisionHeight + obstacleVerticalHitboxInset
    const obstacleBottom = bottom - obstacleVerticalHitboxInset
    return obstacleLeft < horseCollisionRight &&
      obstacleRight > horseCollisionLeft &&
      obstacleTop < horseBottom &&
      obstacleBottom > top
  })
  return collision ?? null
}

function obstacleHitByDash(state: HorseState, horseCollisionHeight: number): number | null {
  const platformY = new Map(state.platforms.map(platform => [platform.id, platform.y]))
  const horseBottom = state.horseY - horseCollisionBottomInset
  const horseTop = horseBottom - horseCollisionHeight
  const dashable = state.obstacles.find(obstacle => {
    if ((obstacle.kind !== 'lasso-thrower' && obstacle.kind !== 'dog') || obstacle.knockedDownAt !== undefined) return false
    const bottom = platformY.get(obstacle.platformId)! - obstacle.elevation
    const obstacleLeft = obstacle.x + obstacleHorizontalHitboxInset
    const obstacleRight = obstacle.x + obstacle.width - obstacleHorizontalHitboxInset
    const obstacleTop = bottom - obstacle.height + obstacleVerticalHitboxInset
    const obstacleBottom = bottom - obstacleVerticalHitboxInset
    return obstacleLeft < horseCollisionRight &&
      obstacleRight > horseCollisionLeft &&
      obstacleTop < horseBottom &&
      obstacleBottom > horseTop
  })
  return dashable?.id ?? null
}

function requiredFollowingObstacleX(previous: Obstacle, nextObstacle: ObstacleDefinition): number {
  // Reserve recovery time after the whole previous hitbox has passed. The
  // speed bounds allow sliding and hazard drift to vary independently.
  const fastestSpeed = 7 + slideSpeedBonus
  const distanceUntilPreviousExit = Math.max(0,
    previous.x + previous.width - obstacleHorizontalHitboxInset - horseCollisionLeft)
  const earliestExitTick = distanceUntilPreviousExit / (fastestSpeed + previous.additionalSpeed)
  const latestExitTick = distanceUntilPreviousExit / (5 + previous.additionalSpeed)
  const relativeSpeed = nextObstacle.additionalSpeed - previous.additionalSpeed
  const worstRelativeDrift = relativeSpeed *
    (relativeSpeed >= 0 ? latestExitTick : earliestExitTick)
  return previous.x + previous.width - 2 * obstacleHorizontalHitboxInset +
    horseCollisionWidth + worstRelativeDrift +
    (minimumObstacleRecoveryTicks + 1) * (fastestSpeed + nextObstacle.additionalSpeed)
}

function hasRecoveryWindowAfter(previousObstacles: readonly Obstacle[], nextObstacle: Obstacle): boolean {
  return previousObstacles.every(previous =>
    nextObstacle.x + 1e-6 >= requiredFollowingObstacleX(previous, nextObstacle))
}

export function hauntedBiomeBlend(tick: number): number {
  if (tick <= hauntedBiomeTransitionStartTick) return 0
  return Math.min(1, (tick - hauntedBiomeTransitionStartTick) /
    (hauntedBiomeStartTick - hauntedBiomeTransitionStartTick))
}

export function biomeSectionForTick(tick: number, rulesVersion: HorseRulesVersion = currentRulesVersion): HorseBiomeSection {
  if (!Number.isInteger(tick) || tick < 0) throw new RangeError('Horse Flight tick must be a non-negative integer.')
  if (rulesVersion !== draftMultiBiomeRulesVersion) {
    if (tick < hauntedBiomeTransitionStartTick) {
      return { phase: 'main', biome: 'mountain', from: 'mountain', to: 'mountain', blend: 1 }
    }
    if (tick < hauntedBiomeStartTick) {
      return {
        phase: 'transition', biome: 'haunted', from: 'mountain', to: 'haunted',
        blend: (tick - hauntedBiomeTransitionStartTick) / biomeTransitionDurationTicks,
      }
    }
    return { phase: 'main', biome: 'haunted', from: 'haunted', to: 'haunted', blend: 1 }
  }
  let biome: HorseBiome = 'mountain'
  for (const transition of biomeTransitions) {
    if (tick < transition.startTick) break
    if (tick < transition.startTick + biomeTransitionDurationTicks) {
      return {
        phase: 'transition',
        biome: transition.to,
        from: transition.from,
        to: transition.to,
        blend: (tick - transition.startTick) / biomeTransitionDurationTicks,
      }
    }
    biome = transition.to
  }
  return { phase: 'main', biome, from: biome, to: biome, blend: 1 }
}

export function biomeForTick(tick: number, rulesVersion: HorseRulesVersion = currentRulesVersion): HorseBiome {
  return biomeSectionForTick(tick, rulesVersion).biome
}

function copy(state: HorseState): HorseState {
  return {
    ...state,
    platforms: state.platforms.map(platform => ({ ...platform })),
    obstacles: state.obstacles.map(obstacle => ({ ...obstacle })),
  }
}
