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

export type HorseBiome = 'mountain' | 'haunted'

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

export type HorseState = {
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
}

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
const slideHorseHeight = 20
const horseLeft = horseX - horseWidth / 2
const horseRight = horseX + horseWidth / 2
const maximumJumps = 2
const slideDurationTicks = 16
const slideSpeedBonus = 4
const fastFallVelocity = 16
const fenceSlideClearance = 24
const levelsPerAdditionalObstacle = 3
const maximumObstaclesPerPlatform = 3
const obstacleClearance = 18
const maximumObstacleWidth = 92
const horizontalHitboxInset = 3
const verticalHitboxInset = 2
const initialPlatformWidth = 900
// The first 15,000 20ms simulation ticks are the five-minute mountain run.
// The transition starts a little before the haunted scene becomes fully visible.
export const hauntedBiomeTransitionStartTick = 14_250
export const hauntedBiomeStartTick = 15_000
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

export function startHorse(seed: number): HorseState {
  const state: HorseState = {
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
  }
  fill(state)
  return state
}

export function stepHorse(source: HorseState, wantsJump: boolean, wantsRightClick = false): { state: HorseState; jumped: boolean; slid: boolean; fastFell: boolean } {
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
  state.biome = biomeForTick(state.tick)
  const collisionHorseHeight = isSlidingThisTick && state.grounded ? slideHorseHeight : horseHeight
  const dashedObstacleId = isSlidingThisTick && state.grounded
    ? obstacleHitByDash(state, collisionHorseHeight)
    : null
  if (dashedObstacleId !== null) {
    state.obstacles = state.obstacles.map(obstacle => obstacle.id === dashedObstacleId
      ? { ...obstacle, knockedDownAt: state.tick }
      : obstacle)
  }
  state.phase = collides(state, collisionHorseHeight)
    ? 'obstacle-collision'
    : state.horseY - horseHeight > H
      ? 'fell'
      : 'running'
  return { state, jumped, slid, fastFell }
}

function fill(state: HorseState) {
  const level = 1 + Math.floor(state.score / 500)
  const biome = biomeForTick(state.tick)
  while (state.platforms.length < 4 || Math.max(...state.platforms.map(platform => platform.x + platform.width)) < W + 420) {
    const previous = state.platforms.reduce(
      (right, platform) => !right || platform.x + platform.width > right.x + right.width ? platform : right,
      undefined as Platform | undefined)
    const obstacleSlots = Math.min(
      maximumObstaclesPerPlatform,
      1 + Math.floor((level - 1) / levelsPerAdditionalObstacle))
    const minimumWidthForSlots = 80 + obstacleSlots * maximumObstacleWidth +
      (obstacleSlots - 1) * obstacleClearance
    const width = Math.max(minimumWidthForSlots, 260 + next(state) * 180)
    const platform: Platform = {
      id: state.nextPlatform++,
      x: previous ? previous.x + previous.width : 0,
      width,
      y: H - 80,
      cleared: false,
    }
    state.platforms.push(platform)
    const usableWidth = platform.width - 80 - (obstacleSlots - 1) * obstacleClearance
    const slotWidth = usableWidth / obstacleSlots
    for (let slot = 0; slot < obstacleSlots; slot++) {
      const eligibleDefinitions = biome === 'haunted'
        ? hauntedObstacleDefinitions
        : mountainObstacleDefinitions
      const definition = eligibleDefinitions[Math.min(
        Math.floor(next(state) * eligibleDefinitions.length),
        eligibleDefinitions.length - 1)]
      const slotStart = platform.x + 40 + slot * (slotWidth + obstacleClearance)
      const x = slotStart + next(state) * (slotWidth - definition.width)

      state.obstacles.push({
        id: state.nextObstacle++,
        biome,
        kind: definition.kind,
        platformId: platform.id,
        x,
        width: definition.width,
        height: definition.height,
        elevation: definition.elevation,
        additionalSpeed: definition.additionalSpeed,
      })
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

function collides(state: HorseState, horseCollisionHeight: number) {
  const y = new Map(state.platforms.map(platform => [platform.id, platform.y]))
  const top = state.horseY - horseCollisionHeight
  return state.obstacles.some(obstacle => {
    if (obstacle.knockedDownAt !== undefined) return false
    const bottom = y.get(obstacle.platformId)! - obstacle.elevation -
      (obstacle.kind === 'fence' ? fenceSlideClearance : 0)
    const obstacleLeft = obstacle.x + horizontalHitboxInset
    const obstacleRight = obstacle.x + obstacle.width - horizontalHitboxInset
    // The tower itself is scenery; its falling net is the low hazard that
    // requires a dash to duck beneath.
    if (obstacle.kind === 'net-tower' && horseCollisionHeight <= slideHorseHeight) return false
    const collisionHeight = obstacle.kind === 'net-tower' ? 24 : obstacle.height
    const obstacleTop = bottom - collisionHeight + verticalHitboxInset
    const obstacleBottom = bottom - verticalHitboxInset
    return obstacleLeft < horseRight &&
      obstacleRight > horseLeft &&
      obstacleTop < state.horseY &&
      obstacleBottom > top
  })
}

function obstacleHitByDash(state: HorseState, horseCollisionHeight: number): number | null {
  const platformY = new Map(state.platforms.map(platform => [platform.id, platform.y]))
  const horseTop = state.horseY - horseCollisionHeight
  const dashable = state.obstacles.find(obstacle => {
    if ((obstacle.kind !== 'lasso-thrower' && obstacle.kind !== 'dog') || obstacle.knockedDownAt !== undefined) return false
    const bottom = platformY.get(obstacle.platformId)! - obstacle.elevation
    const obstacleLeft = obstacle.x + horizontalHitboxInset
    const obstacleRight = obstacle.x + obstacle.width - horizontalHitboxInset
    const obstacleTop = bottom - obstacle.height + verticalHitboxInset
    const obstacleBottom = bottom - verticalHitboxInset
    return obstacleLeft < horseRight &&
      obstacleRight > horseLeft &&
      obstacleTop < state.horseY &&
      obstacleBottom > horseTop
  })
  return dashable?.id ?? null
}

export function hauntedBiomeBlend(tick: number): number {
  if (tick <= hauntedBiomeTransitionStartTick) return 0
  return Math.min(1, (tick - hauntedBiomeTransitionStartTick) /
    (hauntedBiomeStartTick - hauntedBiomeTransitionStartTick))
}

export function biomeForTick(tick: number): HorseBiome {
  return tick >= hauntedBiomeTransitionStartTick ? 'haunted' : 'mountain'
}

function copy(state: HorseState): HorseState {
  return {
    ...state,
    platforms: state.platforms.map(platform => ({ ...platform })),
    obstacles: state.obstacles.map(obstacle => ({ ...obstacle })),
  }
}
