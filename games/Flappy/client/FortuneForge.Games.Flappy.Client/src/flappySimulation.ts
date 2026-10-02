/** Pure browser mirror of FortuneForge.Games.Flappy.FlappyEngine. */
export type FlappySimulationPhase = 'playing' | 'obstacle-collision' | 'ground-collision' | 'ceiling-collision'
export type FlappySimulationObstacle = Readonly<{ id: number; x: number; width: number; gapTop: number; gapBottom: number; scored: boolean }>
export type FlappySimulationState = Readonly<{
  width: number; height: number; seed: number; randomState: number; birdY: number; birdVelocity: number
  obstacles: readonly FlappySimulationObstacle[]; nextObstacleId: number; score: number; bestScore: number
  tick: number; phase: FlappySimulationPhase
}>

export const FlappyRules = {
  defaultWidth: 800, defaultHeight: 600, tickMilliseconds: 20, birdX: 200, birdRadius: 12,
  gravityPerTick: 0.45, flapVelocity: -7.5, obstacleWidth: 70, scoresPerLevel: 5,
} as const

const initialObstacleCount = 3, initialObstacleLead = 120, obstacleSpacing = 260
const baseObstacleSpeed = 3.5, speedIncreasePerLevel = 0.15, maximumObstacleSpeed = 5.3
const baseGapHeight = 180, gapReductionPerLevel = 4, minimumGapHeight = 132, verticalMargin = 24

export function startFlappySimulation(seed: number, width = FlappyRules.defaultWidth, height = FlappyRules.defaultHeight, bestScore = 0): FlappySimulationState {
  validateStart(seed, width, height, bestScore)
  let randomState = normalizeSeed(seed), nextObstacleId = 1
  const obstacles: FlappySimulationObstacle[] = []
  for (let index = 0; index < initialObstacleCount; index++) {
    const created = createObstacle(nextObstacleId++, width + initialObstacleLead + index * obstacleSpacing, height, 1, randomState)
    obstacles.push(created.obstacle); randomState = created.randomState
  }
  return { width, height, seed: seed >>> 0, randomState, birdY: height / 2, birdVelocity: 0, obstacles, nextObstacleId, score: 0, bestScore, tick: 0, phase: 'playing' }
}

export function advanceFlappyFrame(state: FlappySimulationState, flap: boolean): FlappySimulationState {
  if (state.phase !== 'playing') throw new Error('This Flappy run has ended. Start a new run to play again.')
  const birdVelocity = (flap ? FlappyRules.flapVelocity : state.birdVelocity) + FlappyRules.gravityPerTick
  const birdY = state.birdY + birdVelocity
  const speed = obstacleSpeed(level(state.score))
  let scoreGained = 0
  const moved: FlappySimulationObstacle[] = []
  for (const obstacle of state.obstacles) {
    let next = { ...obstacle, x: obstacle.x - speed }
    if (!next.scored && next.x + next.width < FlappyRules.birdX) { next = { ...next, scored: true }; scoreGained++ }
    if (next.x + next.width > 0) moved.push(next)
  }
  const score = state.score + scoreGained, nextLevel = level(score)
  let randomState = state.randomState, nextObstacleId = state.nextObstacleId
  let maximumX = moved.length === 0 ? state.width + initialObstacleLead - obstacleSpacing : Math.max(...moved.map(obstacle => obstacle.x))
  while (moved.length < initialObstacleCount) {
    maximumX += obstacleSpacing
    const created = createObstacle(nextObstacleId++, maximumX, state.height, nextLevel, randomState)
    moved.push(created.obstacle); randomState = created.randomState
  }
  return {
    ...state, randomState, birdY, birdVelocity, obstacles: moved, nextObstacleId, score,
    bestScore: Math.max(state.bestScore, score), tick: state.tick + 1,
    phase: terminalPhase(state.height, birdY, moved),
  }
}

export function replayFlappySimulation(seed: number, totalTicks: number, flapTicks: readonly number[]): FlappySimulationState {
  if (!Number.isInteger(totalTicks) || totalTicks < 1 || totalTicks > 9_000) throw new Error('Flappy replay duration is invalid.')
  if (flapTicks.length > 1_500) throw new Error('Flappy replay contains too many flaps.')
  let prior = -1
  for (const tick of flapTicks) {
    if (!Number.isInteger(tick) || tick < 0 || tick >= totalTicks || tick <= prior) throw new Error('Flappy replay flap ticks must be canonical.')
    prior = tick
  }
  let flapIndex = 0, state = startFlappySimulation(seed)
  for (let tick = 0; tick < totalTicks; tick++) {
    if (state.phase !== 'playing') throw new Error('Flappy replay contains input after its terminal collision.')
    const flap = flapTicks[flapIndex] === tick
    if (flap) flapIndex++
    state = advanceFlappyFrame(state, flap)
  }
  return state
}

export function flappyLevel(score: number): number { return level(score) }

function createObstacle(id: number, x: number, height: number, currentLevel: number, randomState: number) {
  const gapHeight = Math.max(minimumGapHeight, baseGapHeight - (currentLevel - 1) * gapReductionPerLevel)
  const halfGap = gapHeight / 2, minimumCenter = verticalMargin + halfGap, maximumCenter = height - verticalMargin - halfGap
  randomState = nextRandom(randomState)
  const center = minimumCenter + (maximumCenter - minimumCenter) * (randomState / 0xffff_ffff)
  return { obstacle: { id, x, width: FlappyRules.obstacleWidth, gapTop: center - halfGap, gapBottom: center + halfGap, scored: false }, randomState }
}

function terminalPhase(height: number, birdY: number, obstacles: readonly FlappySimulationObstacle[]): FlappySimulationPhase {
  if (birdY + FlappyRules.birdRadius >= height) return 'ground-collision'
  if (birdY - FlappyRules.birdRadius <= 0) return 'ceiling-collision'
  for (const obstacle of obstacles) {
    const horizontalOverlap = FlappyRules.birdX + FlappyRules.birdRadius >= obstacle.x && FlappyRules.birdX - FlappyRules.birdRadius <= obstacle.x + obstacle.width
    const outsideGap = birdY - FlappyRules.birdRadius <= obstacle.gapTop || birdY + FlappyRules.birdRadius >= obstacle.gapBottom
    if (horizontalOverlap && outsideGap) return 'obstacle-collision'
  }
  return 'playing'
}

function level(score: number): number { return 1 + Math.floor(score / FlappyRules.scoresPerLevel) }
function obstacleSpeed(currentLevel: number): number { return Math.min(maximumObstacleSpeed, baseObstacleSpeed + (currentLevel - 1) * speedIncreasePerLevel) }
function normalizeSeed(seed: number): number { return (seed >>> 0) === 0 ? 0x9e3779b9 : seed >>> 0 }
function nextRandom(value: number): number { value = (value ^ (value << 13)) >>> 0; value = (value ^ (value >>> 17)) >>> 0; return (value ^ (value << 5)) >>> 0 }
function validateStart(seed: number, width: number, height: number, bestScore: number): void {
  if (!Number.isInteger(seed) || seed < 0 || seed > 0xffff_ffff || !Number.isInteger(width) || width < 320 || width > 3840 || !Number.isInteger(height) || height < 240 || height > 2160 || !Number.isInteger(bestScore) || bestScore < 0) throw new Error('Flappy simulation start parameters are invalid.')
}
